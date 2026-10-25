#!/usr/bin/env python3
"""
Claude Code agent plugin for the Zipper autonomous runner.  EXPERIMENTAL — NOT ACTIVE.

Uses the Claude Code CLI to implement GitHub issues and run autonomous coding
sessions in print (non-interactive) mode.

Protocol interface:
    check_installation() -> bool
    check_token_health() -> bool
    run_mission(prompt, cwd, is_continue=False) -> tuple[int, str, str]

IMPORTANT: The Claude CLI spawns a background Node.js daemon process. When
running in headless/cron mode (no TTY), the daemon can block indefinitely on
stdin. All commands that produce output MUST redirect stdout/stderr to a
temporary file rather than reading them via pipes — failure to do so results
in an indefinite hang.
"""
import json
import os
import shlex
import subprocess
import shutil
import tempfile

# claude writes to a temp file and is read only after exit, so the plugin sees no
# output until the process ends: there is no liveness signal to tell a hang from
# slow work, and the bound is the only guard. 15 minutes matches the other silent
# agents (opencode, hermes). See opencode.py for the measurement behind it.
MISSION_TIMEOUT = 900
_HUNG_KILL_MESSAGE = (
    f"Error: Agent process hung and was forcefully killed after {MISSION_TIMEOUT // 60} minutes."
)


def check_installation() -> bool:
    """Returns True if the Claude Code CLI is installed and on PATH."""
    return shutil.which("claude") is not None


def check_token_health(model: str | None = None) -> bool:
    """
    Returns True if Claude Code token actually works against the API.

    'claude auth status' only checks local state — a revoked/expired token
    still shows loggedIn=true. We send a trivial prompt to verify the token
    is accepted by the API. Token-cost: negligible (~1 output token).
    """
    tmp = tempfile.NamedTemporaryFile(suffix=".log", delete=False)
    tmp_path = tmp.name
    tmp.close()
    cmd = ["claude", "-p", "say ok", "--permission-mode", "acceptEdits"]
    if model and model != "default":
        cmd += ["--model", model]
    try:
        with open(tmp_path, "w") as log_f:
            result = subprocess.run(
                cmd,
                stdout=log_f,
                stderr=subprocess.STDOUT,
                timeout=30,
            )
        with open(tmp_path, "r") as f:
            output = f.read().strip()
        if result.returncode == 0 and output:
            print(f"[claude] API health check: SUCCESS (live token verified)")
            return True
        if "401" in output or "authentication" in output.lower():
            print("[claude] API health check: token rejected (401) — re-login required")
            return False
        print(f"[claude] API health check: unexpected response (exit={result.returncode})")
        return False
    except subprocess.TimeoutExpired:
        print("[claude] API health check: timed out (30s)")
        return False
    except Exception as e:
        print(f"[claude] API health check: exception — {e}")
        return False
    finally:
        try:
            os.unlink(tmp_path)
        except OSError:
            pass


def run_mission(prompt: str, cwd: str, is_continue: bool = False, model: str | None = None) -> tuple[int, str, str]:
    """
    Runs a coding mission using Claude Code in non-interactive print mode (-p).

    IMPORTANT: Stdout/stderr are redirected to a temporary log file rather than
    using pipes. This is required to avoid the background Node.js daemon from
    blocking indefinitely on the subprocess pipe in headless/cron mode.

    Session continuation: Claude Code does not expose a simple --continue flag
    equivalent to agy's --continue. When is_continue is True, Claude will start
    a new session in the same worktree; it is expected to discover context from
    the git history and existing worktree state.

    Args:
        prompt: The task description to pass to the agent.
        cwd: Absolute path to the git worktree where the work happens.
        is_continue: Currently a no-op for Claude — included for interface parity.

    Returns:
        (exit_code, stdout, stderr)
    """
    tmp = tempfile.NamedTemporaryFile(suffix=".log", delete=False, mode="w")
    tmp_path = tmp.name
    tmp.close()

    safe_prompt = prompt.replace("'", "'\\''")
    cmd = [
        "claude", "-p", safe_prompt,
        "--permission-mode", "acceptEdits",
    ]
    if model and model != "default":
        cmd += ["--model", model]

    print(f"[claude] Running mission (continue={is_continue}) in {cwd}")
    env = os.environ.copy()
    env["PWD"] = os.path.abspath(cwd)
    env.pop("OLDPWD", None)
    try:
        with open(tmp_path, "w") as log_f:
            result = subprocess.run(
                cmd,
                cwd=cwd,
                env=env,
                stdout=log_f,
                stderr=subprocess.STDOUT,
                timeout=MISSION_TIMEOUT,
            )
        with open(tmp_path, "r") as f:
            output = f.read()
        print(f"--- [claude output] ---\n{output}")
        return result.returncode, output, ""
    except subprocess.TimeoutExpired:
        print(f"[claude] {_HUNG_KILL_MESSAGE}")
        # claude streams to a temp file rather than a pipe, so there is nothing on
        # the exception to read -- the partial output is the file, and the `finally`
        # below deletes it. Read it here, before that runs: otherwise a timeout
        # leaves no record of why the agent stalled. The other silent agents keep
        # their partial output, and this matches them.
        partial = ""
        try:
            with open(tmp_path, "r", errors="replace") as f:
                partial = f.read()
        except OSError:
            pass
        if partial:
            print(f"--- [claude stdout (partial)] ---\n{partial}")
        return -1, partial, _HUNG_KILL_MESSAGE
    except Exception as e:
        return -1, "", str(e)
    finally:
        try:
            os.unlink(tmp_path)
        except OSError:
            pass
