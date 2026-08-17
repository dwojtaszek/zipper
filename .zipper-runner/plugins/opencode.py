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
import subprocess
import shutil


def list_models() -> list[str]:
    """Returns list of available model strings (agent/model format).

    Discovers all models from every configured opencode provider.
    """
    providers_found = set()
    try:
        stdout = subprocess.check_output(
            ["opencode", "models"],
            text=True, stderr=subprocess.DEVNULL, timeout=15,
        )
        for line in stdout.strip().splitlines():
            line = line.strip()
            if "/" in line and not line.startswith("Error"):
                providers_found.add(line.split("/")[0])
    except Exception:
        pass

    models: list[str] = []
    for provider in sorted(providers_found):
        try:
            result = subprocess.run(
                ["opencode", "models", provider],
                stdout=subprocess.PIPE,
                stderr=subprocess.DEVNULL,
                text=True, timeout=15,
            )
            for line in result.stdout.strip().splitlines():
                line = line.strip()
                if "/" in line and not line.startswith("Error"):
                    models.append(line)
        except Exception:
            pass
    return models


def check_installation() -> bool:
    """Returns True if the opencode CLI is installed and on PATH."""
    return shutil.which("opencode") is not None


def check_token_health() -> bool:
    """
    Returns True if opencode can actually reach the API and get a response.

    'opencode models' only reads local config — a broken server or revoked key
    still lists models. We send a trivial prompt to verify end-to-end connectivity.
    Token-cost: negligible (~1 output token).
    """
    try:
        result = subprocess.run(
            ["opencode", "run", "--command", "say ok", "--dangerously-skip-permissions"],
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            timeout=30,
        )
        combined = (result.stdout + "\n" + result.stderr).strip()
        if result.returncode == 0 and combined and "error" not in combined.lower():
            print(f"[opencode] API health check: SUCCESS (live token verified)")
            return True
        if "error" in combined.lower() or result.returncode != 0:
            print(f"[opencode] API health check: server error — {combined[:200]}")
            return False
        print(f"[opencode] API health check: unexpected response (exit={result.returncode})")
        return False
    except subprocess.TimeoutExpired:
        print("[opencode] API health check: timed out (30s)")
        return False
    except Exception as e:
        print(f"[opencode] API health check: exception — {e}")
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
    base = ["opencode", "run"]
    if is_continue:
        base += ["--continue"]
    if model:
        base += ["--model", model]
    base += ["--command", prompt, "--dangerously-skip-permissions"]

    print(f"[opencode] Running mission (continue={is_continue}) in {cwd}")
    p = None
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
