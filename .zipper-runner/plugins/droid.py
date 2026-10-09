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


class _MissionOutput:
    def __init__(self):
        self.raw = bytearray()
        self.stderr = bytearray()
        self.messages = []
        self.final_text = None
        self.structured = False
        self.pending = bytearray()
        self.scanned = 0

    def stdout_text(self):
        if self.structured:
            return self.final_text + "\n" if self.final_text is not None else "".join(self.messages)
        return self.raw.decode(errors="replace")

    def _record_event(self, line):
        try:
            event = json.loads(line)
        except (ValueError, UnicodeDecodeError):
            return
        if not isinstance(event, dict) or not isinstance(event.get("type"), str):
            return
        self.structured = True
        self.raw.clear()
        # Log event metadata only, never prompts, tool arguments, or results.
        if event.get("type") == "message" and event.get("role") == "assistant":
            if isinstance(event.get("text"), str):
                self.messages.append(event["text"] + "\n")
            print("[droid] assistant progress", flush=True)
        elif event.get("type") == "completion" and isinstance(event.get("finalText"), str):
            self.final_text = event["finalText"]

    def consume(self, stream, chunk):
        if stream == "stderr":
            self.stderr.extend(chunk)
            return
        if not chunk:
            self._record_event(self.pending)
            return
        if not self.structured:
            self.raw.extend(chunk)
        self.pending.extend(chunk)
        newline = self.pending.find(b"\n", self.scanned)
        while newline >= 0:
            self._record_event(self.pending[:newline])
            del self.pending[:newline + 1]
            newline = self.pending.find(b"\n")
        self.scanned = len(self.pending)


def _read_available(selector, output, wait):
    for key, _ in selector.select(wait):
        chunk = os.read(key.fd, 65536)
        output.consume(key.data, chunk)
        if not chunk:
            selector.unregister(key.fileobj)


def _drain_output(p, cmd, output):
    started = time.monotonic()
    heartbeat = started + HEARTBEAT_SECONDS
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
            _read_available(selector, output, min(1, MISSION_TIMEOUT - (now - started), heartbeat - now))
    p.wait(timeout=max(0.01, MISSION_TIMEOUT - (time.monotonic() - started)))


def _stop_mission(p):
    if p is None:
        return
    if p.poll() is None or not p.stdout.closed:
        try:
            os.killpg(p.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        p.wait()
    p.stdout.close()
    p.stderr.close()


def run_mission(prompt: str, cwd: str, is_continue: bool = False, model: str | None = None) -> tuple[int, str, str]:
    """Run a fresh exec, including continuation requests, and return code/stdout/stderr."""
    cmd = ["droid", "exec", "--model", model or DEFAULT_MODEL, "--reasoning-effort", REASONING_EFFORT, "--auto", "high", "--output-format", "stream-json", "--cwd", cwd, prompt]
    print(f"[droid] Running mission (continue={is_continue}) in {cwd}", flush=True)
    p = None
    output = _MissionOutput()
    try:
        p = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, start_new_session=True)
        _drain_output(p, cmd, output)
        return p.returncode, output.stdout_text(), output.stderr.decode(errors="replace")
    except subprocess.TimeoutExpired:
        return -1, output.stdout_text(), f"Error: Droid mission timed out after {MISSION_TIMEOUT} seconds."
    except Exception as e:
        return -1, output.stdout_text(), str(e)
    finally:
        _stop_mission(p)
