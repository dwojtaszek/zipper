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
import json
import os
from pathlib import Path
import re
import sqlite3
import subprocess
import shutil
import tempfile
import time

_CONTEXT_LENGTH_KEYWORDS = (
    "maximum context length",
    "context_length_exceeded",
    "context length exceeded",
    "reduce the length of the messages",
    "prompt is too long",
    "too many tokens",
)


def _with_cwd_env(cwd: str) -> dict[str, str]:
    env = os.environ.copy()
    env["PWD"] = os.path.abspath(cwd)
    env.pop("OLDPWD", None)
    return env


def _db_path() -> str:
    return os.environ.get(
        "OPENCODE_DB_PATH",
        os.path.expanduser("~/.local/share/opencode/opencode.db"),
    )


def _is_context_length_error(stderr: str) -> bool:
    lowered = stderr.lower()
    return any(kw in lowered for kw in _CONTEXT_LENGTH_KEYWORDS)


def _find_latest_session_for_cwd(cwd: str) -> str | None:
    """Finds the latest top-level OpenCode session ID scoped strictly to cwd.

    Returns None if no session exists for cwd or if the OpenCode SQLite DB
    is unavailable, allowing run_mission to fall back gracefully to starting
    a fresh session in cwd instead of resuming an unrelated worktree/main session.
    """
    db_path = _db_path()
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


def _is_valid_assistant_data(raw_data: str) -> tuple[bool, str | None]:
    """Returns (is_valid_turn, '<providerID>/<id>') for a completed, error-free assistant payload."""
    try:
        data = json.loads(raw_data)
    except Exception:
        return False, None
    if not isinstance(data, dict):
        return False, None
    finish = data.get("finish")
    if data.get("error") is not None or not isinstance(finish, str) or not finish or finish == "error":
        return False, None
    model_str = None
    model_obj = data.get("model")
    if isinstance(model_obj, dict):
        provider_id = model_obj.get("providerID")
        model_id = model_obj.get("id")
        if provider_id and model_id:
            model_str = f"{provider_id}/{model_id}"
    return True, model_str


def _is_successful_idle_data(raw_data: str) -> bool:
    """Returns True when an idle turn-boundary payload is not from a failed turn."""
    try:
        data = json.loads(raw_data)
    except Exception:
        return False
    if not isinstance(data, dict):
        return False
    return data.get("error") is None and data.get("outcome") != "failed"


def _prune_failed_trailing_turns(session_id: str) -> int:
    """Removes failed trailing rows after the last completed turn boundary."""
    db_path = _db_path()
    if not os.path.isfile(db_path):
        return 0
    try:
        conn = sqlite3.connect(db_path, timeout=5.0)
        try:
            rows = conn.execute(
                "SELECT seq, type, data FROM session_message WHERE session_id = ? ORDER BY seq ASC",
                (session_id,),
            ).fetchall()
            if not rows:
                return 0
            last_good_idx: int | None = None
            for idx, (_seq, msg_type, raw_data) in enumerate(rows):
                if msg_type == "assistant":
                    ok, _ = _is_valid_assistant_data(raw_data)
                    if ok:
                        last_good_idx = idx
                elif msg_type == "compaction":
                    try:
                        cdata = json.loads(raw_data)
                        if isinstance(cdata, dict) and cdata.get("status") == "completed":
                            last_good_idx = idx
                    except Exception:
                        pass
            if last_good_idx is None:
                return 0
            if last_good_idx + 1 < len(rows):
                next_seq, next_type, next_data = rows[last_good_idx + 1]
                if next_type == "idle" and _is_successful_idle_data(next_data):
                    last_good_idx += 1
            if last_good_idx >= len(rows) - 1:
                return 0
            last_good_seq = int(rows[last_good_idx][0])
            cur = conn.execute(
                "DELETE FROM session_message WHERE session_id = ? AND seq > ?",
                (session_id, last_good_seq),
            )
            deleted = cur.rowcount if cur.rowcount and cur.rowcount > 0 else 0
            if deleted > 0:
                conn.commit()
                print(f"[opencode] Pruned {deleted} failed trailing message row(s) from session {session_id}.")
            return deleted
        finally:
            conn.close()
    except Exception:
        return 0


def _get_session_assistant_model(session_id: str) -> str | None:
    """Returns '<providerID>/<id>' of the last valid assistant turn in session_id, if recorded."""
    db_path = _db_path()
    if not os.path.isfile(db_path):
        return None
    try:
        db_uri = f"{Path(os.path.abspath(db_path)).as_uri()}?mode=ro"
        conn = sqlite3.connect(db_uri, uri=True, timeout=2.0)
        try:
            rows = conn.execute(
                "SELECT data FROM session_message "
                "WHERE session_id = ? AND type = 'assistant' "
                "ORDER BY seq DESC",
                (session_id,),
            ).fetchall()
            for (raw_data,) in rows:
                if not raw_data:
                    continue
                ok, model_str = _is_valid_assistant_data(raw_data)
                if ok and model_str:
                    return model_str
            return None
        finally:
            conn.close()
    except Exception:
        return None


def _archive_session(session_id: str) -> bool:
    """Marks an overflowed session as archived in session_v2."""
    db_path = _db_path()
    if not os.path.isfile(db_path):
        return False
    try:
        conn = sqlite3.connect(db_path, timeout=5.0)
        try:
            now_ms = int(time.time() * 1000)
            conn.execute(
                "UPDATE session_v2 SET time_archived = ? WHERE id = ? AND time_archived IS NULL",
                (now_ms, session_id),
            )
            conn.commit()
            return True
        finally:
            conn.close()
    except Exception:
        return False


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


def _invoke_opencode(cmd: list[str], abs_cwd: str) -> tuple[int, str, str]:
    p = None
    try:
        p = subprocess.Popen(
            cmd,
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
        code = p.returncode
        if code == 0 and not stdout.strip() and re.search(r"(?:^|\n)\s*(?:\x1b\[[0-9;]*m)*Error:\s+", stderr):
            code = 1
        return code, stdout, stderr
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
    session_id = _find_latest_session_for_cwd(abs_cwd) if is_continue else None
    if is_continue:
        if session_id:
            _prune_failed_trailing_turns(session_id)
        else:
            print(f"[opencode] No existing session found for {abs_cwd}; starting fresh session in worktree.")

    def _build_cmd(sid: str | None, mdl: str | None) -> list[str]:
        cmd = ["opencode", "run"]
        if sid:
            cmd += ["--session", sid]
        if mdl and mdl != "default":
            cmd += ["--model", mdl]
        cmd += ["--auto", prompt]
        return cmd

    print(f"[opencode] Running mission (continue={is_continue}) in {abs_cwd}")
    code, stdout, stderr = _invoke_opencode(_build_cmd(session_id, model), abs_cwd)
    if session_id and code != 0:
        _prune_failed_trailing_turns(session_id)
        if _is_context_length_error(stderr):
            orig_code, orig_stdout, orig_stderr = code, stdout, stderr
            session_model = _get_session_assistant_model(session_id)
            if session_model and session_model != model:
                print(
                    f"[opencode] Session {session_id} exceeded context length for {model}; "
                    f"retrying session with its original model {session_model}."
                )
                code, stdout, stderr = _invoke_opencode(_build_cmd(session_id, session_model), abs_cwd)
                if code != 0:
                    _prune_failed_trailing_turns(session_id)
            if code != 0 and _is_context_length_error(stderr):
                print(
                    f"[opencode] Session {session_id} could not be resumed within context limits; "
                    f"archiving session and starting fresh session in {abs_cwd}."
                )
                _archive_session(session_id)
                code, stdout, stderr = _invoke_opencode(_build_cmd(None, model), abs_cwd)
            elif code != 0:
                code, stdout, stderr = orig_code, orig_stdout, orig_stderr
    return code, stdout, stderr
