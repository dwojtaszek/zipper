#!/usr/bin/env python3
"""
Droid agent plugin for the Zipper autonomous runner.

Uses the Factory Droid CLI (droid exec) to implement GitHub issues and run
autonomous coding sessions.

Protocol interface:
    check_installation() -> bool
    check_token_health() -> bool
    run_mission(prompt, cwd, is_continue=False) -> tuple[int, str, str]
"""
import os
import signal
import subprocess
import shutil

DEFAULT_MODEL = "gpt-6-luna"
REASONING_EFFORT = "max"


def list_models() -> list[str]:
    return [DEFAULT_MODEL]


def check_installation() -> bool:
    """Returns True if the droid CLI is installed and on PATH."""
    return shutil.which("droid") is not None


def check_token_health() -> bool:
    """
    Returns True if droid has active API tokens.

    Sends a tiny prompt using the configured model and reasoning effort.
    Requires a successful response containing pong within 30 seconds;
    failures can reflect authentication, quota, connectivity, or latency.
    """
    try:
        result = subprocess.run(
            ["droid", "exec", "--model", DEFAULT_MODEL, "--reasoning-effort", REASONING_EFFORT, "Reply only with pong. Do not use tools."],
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

    This plugin starts a fresh exec even when is_continue is True.
    The agent reads current worktree state; prior conversation is not resumed.

    Args:
        prompt: The task description to pass to the agent.
        cwd: Absolute path to the git worktree where the work happens.
        is_continue: Currently a no-op for Droid — included for interface parity.

    Returns:
        (exit_code, stdout, stderr)
    """
    cmd = ["droid", "exec", "--model", model or DEFAULT_MODEL, "--reasoning-effort", REASONING_EFFORT, "--auto", "high", "--cwd", cwd, prompt]
    print(f"[droid] Running mission (continue={is_continue}) in {cwd}")
    try:
        p = subprocess.Popen(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            start_new_session=True,
        )
        stdout, stderr = p.communicate(timeout=2700)
        print(f"--- [droid stdout] ---\n{stdout}")
        if stderr:
            print(f"--- [droid stderr] ---\n{stderr}")
        return p.returncode, stdout, stderr
    except subprocess.TimeoutExpired:
        try:
            os.killpg(p.pid, signal.SIGKILL)
        except OSError:
            p.kill()
        stdout, stderr = p.communicate()
        return -1, stdout, "Error: Droid mission timed out after 2700 seconds."
    except Exception as e:
        return -1, "", str(e)
