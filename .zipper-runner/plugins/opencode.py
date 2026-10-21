#!/usr/bin/env python3
"""
OpenCode agent plugin for the Zipper autonomous runner.

Uses the OpenCode CLI (opencode) to implement GitHub issues and run autonomous
coding sessions in non-interactive mode.

Protocol interface:
    check_installation() -> bool
    check_token_health() -> bool
    run_mission(prompt, cwd, is_continue=False) -> tuple[int, str, str]
"""
import os
from pathlib import Path
import sqlite3
import subprocess
import shutil
import tempfile


def _with_cwd_env(cwd: str) -> dict[str, str]:
    env = os.environ.copy()
    env["PWD"] = os.path.abspath(cwd)
    env.pop("OLDPWD", None)
    return env


def _find_latest_session_for_cwd(cwd: str) -> str | None:
    """Finds the latest top-level OpenCode session ID scoped strictly to cwd.

    Returns None if no session exists for cwd or if the OpenCode SQLite DB
    is unavailable, allowing run_mission to fall back gracefully to starting
    a fresh session in cwd instead of resuming an unrelated worktree/main session.
    """
    db_path = os.environ.get(
        "OPENCODE_DB_PATH",
        os.path.expanduser("~/.local/share/opencode/opencode.db"),
    )
    if not os.path.isfile(db_path):
        return None
    abs_cwd = os.path.abspath(cwd)
    real_cwd = os.path.realpath(cwd)
    try:
        db_uri = f"{Path(os.path.abspath(db_path)).as_uri()}?mode=ro"
        conn = sqlite3.connect(db_uri, uri=True, timeout=2.0)
        try:
            row = conn.execute(
                "SELECT id FROM session_v2 "
                "WHERE directory IN (?, ?) AND parent_id IS NULL AND time_archived IS NULL "
                "ORDER BY time_updated DESC LIMIT 1",
                (abs_cwd, real_cwd),
            ).fetchone()
            return str(row[0]) if row and row[0] else None
        finally:
            conn.close()
    except Exception:
        return None


def list_models() -> list[str]:
    """Returns list of available model strings (agent/model format)."""
    probe_dir = tempfile.gettempdir()
    for attempt in range(2):
        try:
            stdout = subprocess.check_output(
                ["opencode", "models"],
                cwd=probe_dir,
                env=_with_cwd_env(probe_dir),
                text=True, stderr=subprocess.DEVNULL, timeout=15,
            )
            models = [
                line.strip()
                for line in stdout.strip().splitlines()
                if "/" in line and not line.startswith("Error")
            ]
            if models:
                return models
            if attempt == 0:
                import time
                time.sleep(1)
        except Exception:
            if attempt == 0:
                import time
                time.sleep(1)
    return []


def check_installation() -> bool:
    """Returns True if the opencode CLI is installed and on PATH."""
    return shutil.which("opencode") is not None


def check_token_health(model: str | None = None) -> bool:
    """
    Returns True if opencode is installed and can list models (server reachable).

    We use `opencode models` as a lightweight connectivity probe —
    it hits the opencode API to enumerate models and fails fast if the server
    is down or credentials are missing. Avoids launching a full run session
    which hangs when the server returns UnknownError on health probes.
    """
    probe_dir = tempfile.gettempdir()
    for attempt in range(2):
        try:
            result = subprocess.run(
                ["opencode", "models"],
                cwd=probe_dir,
                env=_with_cwd_env(probe_dir),
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                timeout=15,
            )
            combined = (result.stdout + "\n" + result.stderr).strip()
            if result.returncode != 0:
                print(f"[opencode] API health check: server error — {combined[:200]}")
                return False
            # Need at least one model returned
            models = [l.strip() for l in result.stdout.splitlines() if "/" in l and not l.startswith("Error")]
            if models:
                print(f"[opencode] API health check: SUCCESS ({len(models)} models available)")
                return True
            if attempt == 0:
                import time
                time.sleep(1)
                continue
            print("[opencode] API health check: no models returned")
            return False
        except subprocess.TimeoutExpired:
            if attempt == 0:
                import time
                time.sleep(1)
                continue
            print("[opencode] API health check: timed out (15s)")
            return False
        except Exception as e:
            print(f"[opencode] API health check: exception — {e}")
            return False
    return False


def run_mission(prompt: str, cwd: str, is_continue: bool = False, model: str | None = None) -> tuple[int, str, str]:
    """
    Runs a coding mission non-interactively using opencode.

    Args:
        prompt: The task description to pass to the agent.
        cwd: Absolute path to the git worktree where the work happens.
        is_continue: If True, continues the most recent session in cwd.
        model: Model to use (e.g. 'opencode/big-pickle').

    Returns:
        (exit_code, stdout, stderr)
    """
    abs_cwd = os.path.abspath(cwd)
    base = ["opencode", "run"]
    if is_continue:
        session_id = _find_latest_session_for_cwd(abs_cwd)
        if session_id:
            base += ["--session", session_id]
        else:
            print(f"[opencode] No existing session found for {abs_cwd}; starting fresh session in worktree.")
    if model and model != "default":
        base += ["--model", model]
    base += ["--auto", prompt]

    print(f"[opencode] Running mission (continue={is_continue}) in {abs_cwd}")
    p = None
    try:
        p = subprocess.Popen(
            base,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            cwd=abs_cwd,
            env=_with_cwd_env(abs_cwd),
        )
        # 45-minute hard timeout to prevent indefinite lockups
        stdout, stderr = p.communicate(timeout=2700)
        print(f"--- [opencode stdout] ---\n{stdout}")
        if stderr:
            print(f"--- [opencode stderr] ---\n{stderr}")
        return p.returncode, stdout, stderr
    except subprocess.TimeoutExpired:
        print("[opencode] CRITICAL: Agent process hung for over 45 minutes! Forcefully killing it.")
        if p:
            p.kill()
            stdout, stderr = p.communicate()
            print(f"--- [opencode stdout (partial)] ---\n{stdout}")
            if stderr:
                print(f"--- [opencode stderr (partial)] ---\n{stderr}")
            return -1, stdout, "Error: Agent process hung and was forcefully killed after 45 minutes."
        return -1, "", "Error: Timeout before process started."
    except Exception as e:
        return -1, "", str(e)
