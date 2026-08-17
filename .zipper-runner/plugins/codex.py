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
import subprocess
import shutil


KNOWN_MODELS = [
    "openai/gpt-5.6-terra",
    "openai/gpt-5.5",
    "openai/gpt-5.4",
]


def list_models() -> list[str]:
    """Returns known Codex model strings."""
    return KNOWN_MODELS


def check_installation() -> bool:
    """Returns True if the Codex CLI is installed and on PATH."""
    return shutil.which("codex") is not None


def check_token_health() -> bool:
    """
    Returns True if Codex can actually reach the API and get a response.

    Sends a trivial prompt to verify end-to-end connectivity.
    Token-cost: negligible (~1 output token).
    """
    try:
        result = subprocess.run(
            ["codex", "exec", "--ephemeral", "say ok"],
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
    if model:
        # Strip provider prefix if present (e.g. "openai/gpt-5.6-terra" -> "gpt-5.6-terra")
        model_name = model.split("/", 1)[-1] if "/" in model else model
        base += ["-m", model_name]

    print(f"[codex] Running mission (continue={is_continue}) in {cwd}")
    try:
        result = subprocess.run(
            base,
            input=prompt,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            cwd=cwd,
            timeout=2700,
        )
        output = result.stdout
        if result.stderr:
            output += f"\n--- stderr ---\n{result.stderr}"
        print(f"--- [codex output] ---\n{output}")
        return result.returncode, output, result.stderr
    except subprocess.TimeoutExpired:
        print("[codex] Mission timed out after 45 minutes")
        return -1, "", "Timeout"
    except Exception as e:
        return -1, "", str(e)
