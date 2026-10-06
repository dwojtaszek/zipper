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
import subprocess
import shutil
import tempfile


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


def check_token_health() -> bool:
    """
    Returns True if agy has active API tokens on at least one candidate model.

    Probes models in priority order, falling back to other candidate models
    if the primary model hits quota.
    """
    candidates = ["gemini-3.8-flash-high", "claude-sonnet-4-6", "claude-opus-4-6-thinking", "gemini-3.7-flash-high"]
    for model in candidates:
        if _probe_model(model):
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
    if model:
        base += ["--model", model]
    base += ["--prompt", prompt, "--print-timeout", "30m", "--dangerously-skip-permissions"]

    print(f"[agy] Running mission (continue={is_continue}) in {cwd}")
    try:
        p = subprocess.Popen(
            base,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            cwd=cwd,
        )
        # 45-minute hard timeout to prevent indefinite lockups
        stdout, stderr = p.communicate(timeout=2700)
        print(f"--- [agy stdout] ---\n{stdout}")
        if stderr:
            print(f"--- [agy stderr] ---\n{stderr}")
        return p.returncode, stdout, stderr
    except subprocess.TimeoutExpired:
        print("[agy] CRITICAL: Agent process hung for over 45 minutes! Forcefully killing it.")
        p.kill()
        stdout, stderr = p.communicate()
        print(f"--- [agy stdout (partial)] ---\n{stdout}")
        if stderr:
            print(f"--- [agy stderr (partial)] ---\n{stderr}")
        return -1, stdout, "Error: Agent process hung and was forcefully killed after 45 minutes."
    except Exception as e:
        return -1, "", str(e)
