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
import signal
import sqlite3
import subprocess
import shutil
import tempfile
import time

MISSION_TIMEOUT = 2700

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


def _state_dir() -> str:
    runner_base = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    return os.environ.get("STATE_DIR", os.path.join(runner_base, "state"))


def _poison_marker_path(session_id: str) -> str:
    safe_id = re.sub(r"[^A-Za-z0-9_.-]", "-", session_id)
    return os.path.join(_state_dir(), f"opencode-poisoned-{safe_id}.json")


def _mark_session_poisoned(session_id: str, reason: str) -> None:
    """Persistently flag a session that must never be resumed.

    If an overflowed session cannot be archived (DB locked by the opencode
    server), the in-memory retry chain would replay it every runner cycle
    forever. The marker makes _find_latest_session_for_cwd skip it until the
    state sweep removes it."""
    try:
        os.makedirs(_state_dir(), exist_ok=True)
        with open(_poison_marker_path(session_id), "w") as f:
            json.dump({"session": session_id, "reason": reason, "at": time.time()}, f)
        print(f"[opencode] Marked session {session_id} as poisoned ({reason}).")
    except OSError as e:
        print(f"[opencode] Could not write poison marker for {session_id}: {e}")


def _session_is_poisoned(session_id: str) -> bool:
    return os.path.exists(_poison_marker_path(session_id))


def _db_path() -> str:
    return os.environ.get(
        "OPENCODE_DB_PATH",
        os.path.expanduser("~/.local/share/opencode/opencode.db"),
    )


def _is_context_length_error(stderr: str, stdout: str = "") -> bool:
    """Matches the overflow text on either stream.

    The runner's quota detection reads stdout+stderr symmetrically; opencode
    can print CLI errors on either stream, so a stderr-only check would miss
    the overflow and the retry/archive chain would never engage. Only the
    stdout *tail* is scanned to avoid matching file content the agent printed
    mid-transcript."""
    lowered = stderr.lower()
    if any(kw in lowered for kw in _CONTEXT_LENGTH_KEYWORDS):
        return True
    tail = stdout[-4000:].lower()
    return any(kw in tail for kw in _CONTEXT_LENGTH_KEYWORDS)


def _find_latest_session_for_cwd(cwd: str) -> str | None:
    """Finds the latest top-level OpenCode session ID scoped strictly to cwd.

    Returns None if no session exists for cwd or if the OpenCode SQLite DB
    is unavailable, allowing run_mission to fall back gracefully to starting
    a fresh session in cwd instead of resuming an unrelated worktree/main session.
    Sessions flagged as poisoned (unarchivable overflow) are skipped so a
    known-bad session is never resumed across cycles.
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
            rows = conn.execute(
                "SELECT id FROM session_v2 "
                "WHERE directory IN (?, ?) AND parent_id IS NULL AND time_archived IS NULL "
                "ORDER BY time_updated DESC LIMIT 10",
                (abs_cwd, real_cwd),
            ).fetchall()
            for (session_id,) in rows:
                if session_id and not _session_is_poisoned(str(session_id)):
                    return str(session_id)
            return None
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
        print(f"[opencode] Cannot archive session {session_id}: DB not found at {db_path}.")
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
    except Exception as e:
        print(f"[opencode] Failed to archive session {session_id}: {e}")
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
            start_new_session=True,
        )
        # 45-minute hard timeout to prevent indefinite lockups
        stdout, stderr = p.communicate(timeout=MISSION_TIMEOUT)
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
            # Kill the whole process group: p.kill() alone leaves the CLI's
            # children (LSP servers, dotnet/MSBuild nodes) alive holding the
            # stdout/stderr pipes, and communicate() below would then block on
            # them forever — freezing the locked runner (see run_cmd's pattern).
            # start_new_session=True made the child the group leader, so p.pid
            # is the pgid; using os.getpgid() here can fail on an already-reaped
            # child while its group descendants still hold the output pipes.
            try:
                os.killpg(p.pid, signal.SIGKILL)
            except OSError:
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
        if _is_context_length_error(stderr, stdout):
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
            if code != 0 and _is_context_length_error(stderr, stdout):
                print(
                    f"[opencode] Session {session_id} could not be resumed within context limits; "
                    f"archiving session and starting fresh session in {abs_cwd}."
                )
                if not _archive_session(session_id):
                    # Archiving failed (DB locked/unavailable): without the
                    # marker the next runner cycle would resume this same
                    # overflowed session and replay the whole failing chain.
                    _mark_session_poisoned(session_id, "context overflow; archive failed")
                code, stdout, stderr = _invoke_opencode(_build_cmd(None, model), abs_cwd)
            elif code != 0:
                code, stdout, stderr = orig_code, orig_stdout, orig_stderr
    return code, stdout, stderr
