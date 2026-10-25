#!/usr/bin/env python3
"""
OpenAI Codex agent plugin for the Zipper autonomous runner.

Uses the Codex CLI (codex exec) to implement GitHub issues and run autonomous
coding sessions in non-interactive mode.

Protocol interface:
    check_installation() -> bool
    check_token_health() -> bool
    run_mission(prompt, cwd, is_continue=False) -> tuple[int, str, str]
"""
import os
import subprocess
import shutil


KNOWN_MODELS = [
    "openai/gpt-5.6-terra",
    "openai/gpt-5.5",
    "openai/gpt-5.4",
]

# codex runs via a buffered `subprocess.run`, so the plugin sees no output until
# the process exits: there is no liveness signal to distinguish a hang from slow
# work, and the bound is the only guard. 15 minutes matches the other silent
# agents (opencode, hermes). See opencode.py for the measurement behind it.
MISSION_TIMEOUT = 900
_HUNG_KILL_MESSAGE = (
    f"Error: Agent process hung and was forcefully killed after {MISSION_TIMEOUT // 60} minutes."
)


def _partial_output(exc: subprocess.TimeoutExpired, stderr: bool = False) -> str:
    """Decode whatever the killed process had already written.

    `subprocess.run` populates the exception's `output`/`stdout`/`stderr` with the
    bytes captured before the kill, but *always as bytes* even when `text=True`,
    so a raw read would put a `b'...'` repr into the diagnostic trail. An unset
    attribute means nothing was captured.
    """
    raw = exc.stderr if stderr else (exc.stdout if exc.stdout is not None else exc.output)
    if raw is None:
        return ""
    if isinstance(raw, bytes):
        return raw.decode("utf-8", errors="replace")
    return str(raw)


def list_models() -> list[str]:
    """Returns known Codex model strings."""
    return KNOWN_MODELS


def check_installation() -> bool:
    """Returns True if the Codex CLI is installed and on PATH."""
    return shutil.which("codex") is not None


def check_token_health(model: str | None = None) -> bool:
    """
    Returns True if Codex can actually reach the API and get a response.

    Sends a trivial prompt to verify end-to-end connectivity.
    Token-cost: negligible (~1 output token).
    """
    cmd = ["codex", "exec", "--ephemeral"]
    if model and model != "default":
        model_name = model.split("/", 1)[-1] if "/" in model else model
        cmd += ["-m", model_name]
    cmd.append("say ok")
    try:
        result = subprocess.run(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=30,
        )
        combined = (result.stdout + "\n" + result.stderr).strip().lower()
        if result.returncode == 0 and "ok" in combined:
            print("[codex] API health check: SUCCESS (live token verified)")
            return True
        if "error" in combined or result.returncode != 0:
            print(f"[codex] API health check: failed — {combined[:200]}")
            return False
        print(f"[codex] API health check: unexpected response (exit={result.returncode})")
        return False
    except subprocess.TimeoutExpired:
        print("[codex] API health check: timed out (30s)")
        return False
    except Exception as e:
        print(f"[codex] API health check: exception — {e}")
        return False


def run_mission(prompt: str, cwd: str, is_continue: bool = False, model: str | None = None) -> tuple[int, str, str]:
    """
    Runs a coding mission using Codex in non-interactive exec mode.

    Args:
        prompt: The task description to pass to the agent.
        cwd: Absolute path to the git worktree where the work happens.
        is_continue: If True, resumes the most recent session in cwd.
        model: Model to use (e.g. 'openai/gpt-5.6-terra').

    Returns:
        (exit_code, stdout, stderr)
    """
    base = ["codex", "exec", "--ephemeral"]
    if model and model != "default":
        # Strip provider prefix if present (e.g. "openai/gpt-5.6-terra" -> "gpt-5.6-terra")
        model_name = model.split("/", 1)[-1] if "/" in model else model
        base += ["-m", model_name]

    print(f"[codex] Running mission (continue={is_continue}) in {cwd}")
    env = os.environ.copy()
    env["PWD"] = os.path.abspath(cwd)
    env.pop("OLDPWD", None)
    try:
        result = subprocess.run(
            base,
            input=prompt,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            cwd=cwd,
            env=env,
            timeout=MISSION_TIMEOUT,
        )
        output = result.stdout
        if result.stderr:
            output += f"\n--- stderr ---\n{result.stderr}"
        print(f"--- [codex output] ---\n{output}")
        return result.returncode, output, result.stderr
    except subprocess.TimeoutExpired as exc:
        print(f"[codex] {_HUNG_KILL_MESSAGE}")
        # Return what the agent produced before the kill. The runner treats a
        # non-zero exit as "try the next candidate", so the worktree and the WIP
        # checkpoint survive either way -- but the partial output is the only
        # record of *why* it stalled, and discarding it left a timeout with no
        # diagnostic trail at all. The other silent agents preserve theirs.
        partial = _partial_output(exc)
        if partial:
            print(f"--- [codex stdout (partial)] ---\n{partial}")
        partial_err = _partial_output(exc, stderr=True)
        if partial_err:
            print(f"--- [codex stderr (partial)] ---\n{partial_err}")
        # The partial stderr has to come back in `err`, not just go to the log:
        # providers report quota and rate limits there, and the runner scans the
        # returned streams with `is_quota_exhausted(out, err)`. Returning only the
        # kill message would hide the quota and relaunch a model that is cooling
        # down. The success path folds stderr in the same way.
        err = f"{partial_err}\n{_HUNG_KILL_MESSAGE}" if partial_err else _HUNG_KILL_MESSAGE
        return -1, partial, err
    except Exception as e:
        return -1, "", str(e)
