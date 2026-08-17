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
    """Returns list of available model strings from agy models."""
    try:
        result = subprocess.run(
            ["agy", "models"],
            stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL,
            text=True, timeout=15,
        )
        lines = [l.strip() for l in result.stdout.strip().splitlines() if l.strip()]
        return lines
    except Exception:
        return []


def check_installation() -> bool:
    """Returns True if the agy CLI is installed and on PATH."""
    return shutil.which("agy") is not None


def _check_quota_in_log(log_path: str) -> bool:
    """Returns True if quota exhaustion detected in agy log file."""
    quota_keywords = [
        "quota", "limit reached", "rate limit", "insufficient", "credit",
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


def check_token_health() -> bool:
    """
    Returns True if agy has active API tokens.

    Sends a cheap ping prompt with a 15s timeout. Captures agy's internal
    log file via --log-file because agy writes quota errors there, not to
    stdout/stderr.
    """
    tmp = tempfile.NamedTemporaryFile(suffix=".log", delete=False)
    log_path = tmp.name
    tmp.close()
    try:
        result = subprocess.run(
            ["agy", "--prompt", "ping", "--print-timeout", "15s", "--log-file", log_path],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=30,
        )
        # ponytail: exit 0 + output = healthy. Log keyword check only
        # when agy itself failed, avoids false positives from informational
        # log entries ("quota remaining" etc.).
        if result.returncode == 0 and result.stdout.strip():
            print("[agy] API health check: SUCCESS (exit 0 + non-empty output)")
            return True

        full_output = (result.stdout + "\n" + result.stderr).lower()
        quota_keywords = [
            "quota", "limit reached", "rate limit", "insufficient", "credit",
            "billing", "exhausted", "429", "403", "subscription", "run out of",
            "payment",
        ]
        for keyword in quota_keywords:
            if keyword in full_output:
                print(f"[agy] API health check: detected quota issue ({keyword!r})")
                return False

        if _check_quota_in_log(log_path):
            return False

        if result.returncode != 0:
            print(f"[agy] API health check: non-zero exit code {result.returncode}")
            if "error" in full_output or "failed" in full_output:
                return False

        if not (result.stdout or os.path.getsize(log_path) > 0):
            print("[agy] API health check: empty output from agy (likely silent quota/error)")
            return False

        print("[agy] API health check: SUCCESS")
        return True
    except subprocess.TimeoutExpired:
        print("[agy] API health check: timed out")
        return False
    except Exception as e:
        print(f"[agy] API health check: exception — {e}")
        return False
    finally:
        try:
            os.unlink(log_path)
        except OSError:
            pass


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
