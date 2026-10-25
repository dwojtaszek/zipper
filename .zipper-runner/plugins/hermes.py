#!/usr/bin/env python3
"""
Hermes agent plugin for the Zipper autonomous runner.

Uses the Hermes CLI (hermes) to implement GitHub issues, babysit PRs, and run autonomous
coding sessions non-interactively using Hermes's oneshot execution mode.

Protocol interface:
    check_installation() -> bool
    check_token_health() -> bool
    run_mission(prompt, cwd, is_continue=False, model=None) -> tuple[int, str, str]
"""
import os
import shutil
import signal
import subprocess

HERMES_BIN = shutil.which("hermes") or (
    "/opt/hermes/.venv/bin/hermes" if os.path.exists("/opt/hermes/.venv/bin/hermes") else None
)

# hermes emits no liveness output, so this bound is the only thing between a
# silent hang and a wasted mission. It matches opencode's 15 minutes for the same
# reason; agents that signal liveness or stop themselves keep their longer bound
# (agy's nested `--print-timeout`, droid's `assistant progress`). See opencode.py
# for the measurement behind the number.
MISSION_TIMEOUT = 900
_HUNG_KILL_MESSAGE = (
    f"Error: Agent process hung and was forcefully killed after {MISSION_TIMEOUT // 60} minutes."
)


def list_models() -> list[str]:
    """Returns list of available model strings."""
    return ["default"]


def check_installation() -> bool:
    """Returns True if the hermes CLI binary exists and is executable."""
    return HERMES_BIN is not None


def check_token_health(model: str | None = None) -> bool:
    """Returns True if hermes can execute a quick prompt successfully."""
    if not HERMES_BIN:
        return False
    cmd = [HERMES_BIN, "-z", "say ok", "--yolo"]
    if model and model != "default":
        cmd += ["-m", model]
    try:
        result = subprocess.run(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=30,
        )
        combined = (result.stdout + "\n" + result.stderr).strip()
        if result.returncode == 0 and combined and "error" not in combined.lower():
            print("[hermes] API health check: SUCCESS (live agent verified)")
            return True
        if "error" in combined.lower() or result.returncode != 0:
            print(f"[hermes] API health check: server error — {combined[:200]}")
            return False
        return True
    except subprocess.TimeoutExpired:
        print("[hermes] API health check: timed out (30s)")
        return False
    except Exception as e:
        print(f"[hermes] API health check: exception — {e}")
        return False


def run_mission(
    prompt: str, cwd: str, is_continue: bool = False, model: str | None = None
) -> tuple[int, str, str]:
    """
    Runs a coding mission non-interactively using Hermes in the specified working directory.

    Args:
        prompt: The task description to pass to the agent.
        cwd: Absolute path to the git worktree where the work happens.
        is_continue: If True, resumes previous session if supported.
        model: Model override (optional).

    Returns:
        (exit_code, stdout, stderr)
    """
    if not HERMES_BIN:
        return -1, "", "Error: hermes binary not found."

    base = [HERMES_BIN, "-z", prompt, "--yolo", "--skills", "autonomous-issue-resolution"]
    if model and model != "default":
        base += ["-m", model]

    print(f"[hermes] Running mission (continue={is_continue}) in {cwd}")
    p = None
    env = os.environ.copy()
    env["PWD"] = os.path.abspath(cwd)
    env.pop("OLDPWD", None)
    try:
        p = subprocess.Popen(
            base,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            cwd=cwd,
            env=env,
            start_new_session=True,
        )
        # Hard timeout to prevent indefinite lockups (see MISSION_TIMEOUT).
        stdout, stderr = p.communicate(timeout=MISSION_TIMEOUT)
        print(f"--- [hermes stdout] ---\n{stdout}")
        if stderr:
            print(f"--- [hermes stderr] ---\n{stderr}")
        return p.returncode, stdout, stderr
    except subprocess.TimeoutExpired:
        print(
            f"[hermes] CRITICAL: Agent process hung for over "
            f"{MISSION_TIMEOUT // 60} minutes! Forcefully killing it."
        )
        if p:
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
            print(f"--- [hermes stdout (partial)] ---\n{stdout}")
            if stderr:
                print(f"--- [hermes stderr (partial)] ---\n{stderr}")
            return -1, stdout, _HUNG_KILL_MESSAGE
        return -1, "", "Error: Timeout before process started."
    except Exception as e:
        return -1, "", str(e)
