#!/usr/bin/env python3
"""
Agy agent plugin for the Zipper autonomous runner.

This is the default (active) agent. It uses the Antigravity CLI (agy) to implement
GitHub issues, babysit PRs, and run autonomous coding sessions.

Protocol interface:
    check_installation() -> bool
    check_token_health() -> bool
    run_mission(prompt, cwd, is_continue=False) -> tuple[int, str, str]
"""
import os
import signal
import subprocess
import shutil
import tempfile

# agy stops itself and reports partial output, so this kill is a backstop rather
# than the primary bound: PRINT_TIMEOUT_SECONDS must fire first. Both are named so
# the relationship is testable instead of being two loose literals.
PRINT_TIMEOUT_SECONDS = 2400
MISSION_TIMEOUT = 2700
_HUNG_KILL_MESSAGE = (
    f"Error: Agent process hung and was forcefully killed after {MISSION_TIMEOUT // 60} minutes."
)


def list_models() -> list[str]:
    """Returns list of available model identifiers from agy models."""
    try:
        result = subprocess.run(
            ["agy", "models"],
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True, timeout=15,
        )
        models = []
        for line in result.stdout.splitlines():
            line = line.strip()
            if not line or "fetching" in line.lower():
                continue
            parts = line.split()
            if parts:
                models.append(parts[0])
        return models
    except Exception:
        return []


def check_installation() -> bool:
    """Returns True if the agy CLI is installed and on PATH."""
    return shutil.which("agy") is not None


def _check_quota_in_log(log_path: str) -> bool:
    """Returns True if quota exhaustion detected in agy log file."""
    quota_keywords = [
        "quota exceeded", "exceeded your current quota", "insufficient quota",
        "limit reached", "rate limit", "insufficient credits",
        "billing", "exhausted", "429", "403", "subscription", "run out of",
        "payment", "resource_exhausted",
    ]
    try:
        with open(log_path) as f:
            content = f.read().lower()
    except Exception:
        return False
    for kw in quota_keywords:
        if kw in content:
            print(f"[agy] API health check: detected quota issue ({kw!r}) in log")
            return True
    return False


def _probe_model(model: str) -> bool:
    """Probes a single model. Returns True if healthy, False if quota or error."""
    tmp = tempfile.NamedTemporaryFile(suffix=".log", delete=False)
    log_path = tmp.name
    tmp.close()
    try:
        result = subprocess.run(
            ["agy", "--model", model, "--prompt", "reply with pong", "--print-timeout", "25s", "--log-file", log_path],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=35,
        )
        if result.returncode == 0 and (result.stdout.strip() or "print timeout" in result.stderr):
            print(f"[agy] API health check: SUCCESS on model '{model}' (exit 0 + active agent)")
            return True

        full_output = (result.stdout + "\n" + result.stderr).lower()
        quota_keywords = [
            "quota", "limit reached", "rate limit", "insufficient", "credit",
            "billing", "exhausted", "429", "403", "subscription", "run out of",
            "payment",
        ]
        for keyword in quota_keywords:
            if keyword in full_output:
                print(f"[agy] API health check: detected quota issue ({keyword!r}) on model '{model}'")
                return False

        if _check_quota_in_log(log_path):
            return False

        if result.returncode != 0:
            print(f"[agy] API health check: non-zero exit code {result.returncode} on model '{model}'")
            return False

        if not (result.stdout or os.path.getsize(log_path) > 0):
            print(f"[agy] API health check: empty output from agy on model '{model}'")
            return False

        print(f"[agy] API health check: SUCCESS on model '{model}'")
        return True
    except subprocess.TimeoutExpired:
        print(f"[agy] API health check: timed out on model '{model}'")
        return False
    except Exception as e:
        print(f"[agy] API health check: exception on model '{model}' — {e}")
        return False
    finally:
        try:
            os.unlink(log_path)
        except OSError:
            pass


def check_token_health(model: str | None = None) -> bool:
    """
    Returns True if agy has active API tokens.

    If a specific model is requested, probes that model directly.
    Otherwise probes models in priority order, falling back to other candidate models
    if the primary model hits quota.
    """
    if model and model != "default":
        return _probe_model(model)
    # Fallback probe order when no model is pinned. Kept to models agy actually
    # offers: gemini-3.7-flash-high was removed upstream, and probing a dead
    # model only burns a subprocess before falling through.
    candidates = ["gemini-3.8-flash-high", "claude-sonnet-4-6", "claude-opus-4-6-thinking"]
    for m in candidates:
        if _probe_model(m):
            return True
    print("[agy] API health check: all candidate models failed")
    return False


def run_mission(prompt: str, cwd: str, is_continue: bool = False, model: str | None = None) -> tuple[int, str, str]:
    """
    Runs a coding mission non-interactively using agy.

    Args:
        prompt: The task description to pass to the agent.
        cwd: Absolute path to the git worktree where the work happens.
        is_continue: If True, continues the most recent session in cwd.
        model: Model to use (e.g. 'Gemini 3.1 Pro (High)').

    Returns:
        (exit_code, stdout, stderr)
    """
    base = ["agy"]
    if is_continue:
        base += ["--continue"]
    if model and model != "default":
        base += ["--model", model]
    # agy's own print timeout must fire before the MISSION_TIMEOUT kill below, so
    # the agent gets to flush its partial output instead of being SIGKILLed
    # mid-turn. 30m truncated long missions routinely; 40m leaves 5 minutes of
    # slack for the process to exit and report what it finished.
    #
    # This is why agy keeps a 2700s kill while the silent agents (opencode,
    # hermes, codex, claude) use 900s: agy stops itself and reports partial work,
    # whereas those four hang silently with nothing to recover. The nested
    # relationship is the invariant, not the number -- pinned by
    # test_agy_plugin.AgyMissionTimeoutTests.
    base += [
        "--prompt", prompt,
        "--print-timeout", f"{PRINT_TIMEOUT_SECONDS // 60}m",
        "--dangerously-skip-permissions",
    ]

    abs_cwd = os.path.abspath(cwd)
    env = os.environ.copy()
    env["PWD"] = abs_cwd
    env.pop("OLDPWD", None)

    print(f"[agy] Running mission (continue={is_continue}) in {cwd}")
    try:
        p = subprocess.Popen(
            base,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            cwd=abs_cwd,
            env=env,
            start_new_session=True,
        )
        # Hard timeout to prevent indefinite lockups (see MISSION_TIMEOUT).
        stdout, stderr = p.communicate(timeout=MISSION_TIMEOUT)
        print(f"--- [agy stdout] ---\n{stdout}")
        if stderr:
            print(f"--- [agy stderr] ---\n{stderr}")
        return p.returncode, stdout, stderr
    except subprocess.TimeoutExpired:
        print(
            f"[agy] CRITICAL: Agent process hung for over "
            f"{MISSION_TIMEOUT // 60} minutes! Forcefully killing it."
        )
        # Kill the whole process group: p.kill() alone leaves children
        # alive holding the pipes, and communicate() would block forever.
        # start_new_session=True made the child the group leader, so p.pid
        # is the pgid; using os.getpgid() here can fail on an already-reaped
        # child while its group descendants still hold the output pipes.
        try:
            os.killpg(p.pid, signal.SIGKILL)
        except OSError:
            p.kill()
        stdout, stderr = p.communicate()
        print(f"--- [agy stdout (partial)] ---\n{stdout}")
        if stderr:
            print(f"--- [agy stderr (partial)] ---\n{stderr}")
        return -1, stdout, _HUNG_KILL_MESSAGE
    except Exception as e:
        return -1, "", str(e)
