#!/usr/bin/env python3
"""
Droid agent plugin for the Zipper autonomous runner.  EXPERIMENTAL — NOT ACTIVE.

Uses the Factory Droid CLI (droid exec) to implement GitHub issues and run
autonomous coding sessions.

Protocol interface:
    check_installation() -> bool
    check_token_health() -> bool
    run_mission(prompt, cwd, is_continue=False) -> tuple[int, str, str]
"""
import subprocess
import shutil


def check_installation() -> bool:
    """Returns True if the droid CLI is installed and on PATH."""
    return shutil.which("droid") is not None


def check_token_health() -> bool:
    """
    Returns True if droid has active API tokens.

    Runs 'droid exec "ping"' — returns a near-instant 'pong' response with no
    meaningful token cost. A non-zero exit code or missing 'pong' indicates
    an authentication or credit issue.
    """
    try:
        result = subprocess.run(
            ["droid", "exec", "ping"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=30,
        )
        if result.returncode == 0 and "pong" in result.stdout.lower():
            print("[droid] API health check: SUCCESS")
            return True
        full_output = (result.stdout + "\n" + result.stderr).lower()
        quota_keywords = [
            "quota", "rate limit", "insufficient", "credit", "billing",
            "exhausted", "429", "403", "unauthorized", "payment",
        ]
        for keyword in quota_keywords:
            if keyword in full_output:
                print(f"[droid] API health check: detected quota issue ({keyword!r})")
                return False
        print(f"[droid] API health check: unexpected response (exit={result.returncode})")
        return False
    except subprocess.TimeoutExpired:
        print("[droid] API health check: timed out")
        return False
    except Exception as e:
        print(f"[droid] API health check: exception — {e}")
        return False


def run_mission(prompt: str, cwd: str, is_continue: bool = False, model: str | None = None) -> tuple[int, str, str]:
    """
    Runs a coding mission non-interactively using droid exec.

    Note: droid exec does not natively support session continuation like agy
    does. When is_continue is True, we still dispatch a fresh exec in the
    same worktree with the same prompt; Droid uses its own internal session
    context management within the worktree directory.

    Args:
        prompt: The task description to pass to the agent.
        cwd: Absolute path to the git worktree where the work happens.
        is_continue: Currently a no-op for Droid — included for interface parity.

    Returns:
        (exit_code, stdout, stderr)
    """
    cmd = ["droid", "exec", "--auto", "high", "--cwd", cwd, "--skip-permissions-unsafe", prompt]
    print(f"[droid] Running mission (continue={is_continue}) in {cwd}")
    try:
        p = subprocess.Popen(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
        )
        stdout, stderr = p.communicate(timeout=2700)
        print(f"--- [droid stdout] ---\n{stdout}")
        if stderr:
            print(f"--- [droid stderr] ---\n{stderr}")
        return p.returncode, stdout, stderr
    except Exception as e:
        return -1, "", str(e)
