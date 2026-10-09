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
import json
import selectors
import time

DEFAULT_MODEL = "gpt-6-luna"
REASONING_EFFORT = "max"
MISSION_TIMEOUT = 2700
HEARTBEAT_SECONDS = 60


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
    cmd = ["droid", "exec", "--model", model or DEFAULT_MODEL, "--reasoning-effort", REASONING_EFFORT, "--auto", "high", "--output-format", "stream-json", "--cwd", cwd, prompt]
    print(f"[droid] Running mission (continue={is_continue}) in {cwd}", flush=True)
    p = None
    output = {"stdout": bytearray(), "stderr": bytearray()}
    messages = []
    final_text = None
    structured = False
    def captured_stdout():
        if structured:
            return final_text + "\n" if final_text is not None else "".join(messages)
        return output["stdout"].decode(errors="replace")
    def record_event(line):
        nonlocal structured, final_text
        try:
            event = json.loads(line)
        except (ValueError, UnicodeDecodeError):
            return
        if not isinstance(event, dict) or not isinstance(event.get("type"), str):
            return
        structured = True
        output["stdout"].clear()
        # Log event metadata only, never prompts, tool arguments, or results.
        if event.get("type") == "message" and event.get("role") == "assistant":
            if isinstance(event.get("text"), str):
                messages.append(event["text"] + "\n")
            print("[droid] assistant progress", flush=True)
        elif event.get("type") == "completion" and isinstance(event.get("finalText"), str):
            final_text = event["finalText"]
    try:
        p = subprocess.Popen(
            cmd,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            start_new_session=True,
        )
        started = time.monotonic()
        heartbeat = started + HEARTBEAT_SECONDS
        pending = bytearray()
        scanned = 0
        with selectors.DefaultSelector() as selector:
            selector.register(p.stdout, selectors.EVENT_READ, "stdout")
            selector.register(p.stderr, selectors.EVENT_READ, "stderr")
            while selector.get_map():
                now = time.monotonic()
                if now - started >= MISSION_TIMEOUT:
                    raise subprocess.TimeoutExpired(cmd, MISSION_TIMEOUT)
                if now >= heartbeat:
                    print(f"[droid] Mission active ({int(now - started)}s elapsed)", flush=True)
                    heartbeat = now + HEARTBEAT_SECONDS
                for key, _ in selector.select(min(1, MISSION_TIMEOUT - (now - started), heartbeat - now)):
                    chunk = os.read(key.fd, 65536)
                    if not chunk:
                        if key.data == "stdout" and pending:
                            record_event(pending)
                        selector.unregister(key.fileobj)
                        continue
                    if key.data != "stdout" or not structured:
                        output[key.data].extend(chunk)
                    if key.data != "stdout":
                        continue
                    pending.extend(chunk)
                    newline = pending.find(b"\n", scanned)
                    while newline >= 0:
                        record_event(pending[:newline])
                        del pending[:newline + 1]
                        newline = pending.find(b"\n")
                    scanned = len(pending)
        p.wait(timeout=max(0.01, MISSION_TIMEOUT - (time.monotonic() - started)))
        return p.returncode, captured_stdout(), output["stderr"].decode(errors="replace")
    except subprocess.TimeoutExpired:
        return -1, captured_stdout(), f"Error: Droid mission timed out after {MISSION_TIMEOUT} seconds."
    except Exception as e:
        return -1, captured_stdout(), str(e)
    finally:
        if p is not None:
            if p.poll() is None or (p.stdout is not None and not p.stdout.closed):
                try:
                    os.killpg(p.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                p.wait()
            p.stdout.close()
            p.stderr.close()
