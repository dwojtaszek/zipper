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
# droid is the one agent that emits a real liveness signal: `assistant progress`
# on each assistant message, streamed as droid produces it, plus the commits it
# makes in the worktree. So droid is bounded by *silence*, not by the clock: a
# mission that keeps producing output or new commits runs to completion, and one
# that goes quiet is killed after LIVENESS_TIMEOUT.
#
# A wall-clock bound was the wrong shape here. Measured on the host (#1361),
# four droid missions were killed at exactly 2640s while still working -- the
# 06:36 mission had produced two commits and was printing progress every ~3
# minutes when the bound cut it. Publication only happens on a clean mission
# exit, so each cut stranded that work locally and the next firing redid it:
# PR #1357 sat green and mergeable for two hours with three unpublished commits
# behind it, holding the only worktree slot.
#
# MISSION_TIMEOUT stays as a generous absolute ceiling, for a mission that emits
# output forever without finishing. The silence bound is what catches a hang, so
# the ceiling can sit well above any legitimate mission.
#
# Note HEARTBEAT_SECONDS is *not* a liveness signal: it is this plugin's own
# timer, printed whether or not droid produces anything, so it proves only that
# the pipe drain is running. It is deliberately not fed into the progress clock.
LIVENESS_TIMEOUT = int(os.environ.get("RUNNER_DROID_LIVENESS_TIMEOUT", "900"))
MISSION_TIMEOUT = int(os.environ.get("RUNNER_DROID_MISSION_TIMEOUT", "5400"))
HEARTBEAT_SECONDS = 60
# How often to ask git whether the mission committed. Read-only and cheap, and it
# holds up the pipe drain for only milliseconds. Deliberately not configurable:
# the watchdog is only sound while this stays below LIVENESS_TIMEOUT, and a poll
# interval at or above it could let a committing mission be declared hung.
HEAD_CHECK_SECONDS = 30
HEAD_CHECK_TIMEOUT = 10
# How long to keep draining after the mission process exits, so a tool child
# that inherited the pipes can flush its last output. Bounded, because the old
# unbounded wait is what reported a *successful* mission as timed out whenever a
# child held the pipes open to the ceiling. A child that outlives this grace
# window is deliberately left running: a successful mission preserves its
# background children (see test_droid_plugin.py).
MISSION_EXIT_GRACE_SECONDS = int(os.environ.get("RUNNER_DROID_EXIT_GRACE_SECONDS", "5"))


def list_models() -> list[str]:
    return [DEFAULT_MODEL]


def check_installation() -> bool:
    """Returns True if the droid CLI is installed and on PATH."""
    return shutil.which("droid") is not None


def check_token_health(model: str | None = None) -> bool:
    """
    Returns True if droid has active API tokens.

    Sends a tiny prompt using the configured model and reasoning effort.
    Requires a successful response containing pong within 30 seconds;
    failures can reflect authentication, quota, connectivity, or latency.
    """
    target_model = model if (model and model != "default") else DEFAULT_MODEL
    try:
        result = subprocess.run(
            ["droid", "exec", "--model", target_model, "--reasoning-effort", REASONING_EFFORT, "Reply only with pong. Do not use tools."],
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
    """Drain readable bytes. Returns how many arrived; 0 means no progress.

    An EOF read yields no bytes, so a pipe closing is not counted as progress.
    """
    received = 0
    for key, _ in selector.select(wait):
        chunk = os.read(key.fd, 65536)
        output.consume(key.data, chunk)
        received += len(chunk)
        if not chunk:
            selector.unregister(key.fileobj)
    return received


def _worktree_head(cwd):
    """The worktree's current HEAD, or None when it cannot be read.

    A mission that commits is making progress even while it prints nothing, so
    HEAD is a liveness signal alongside droid's own output. An unreadable HEAD is
    not evidence of progress; it just leaves output as the only signal.
    """
    try:
        result = subprocess.run(
            ["git", "-C", cwd, "rev-parse", "HEAD"],
            stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, text=True,
            timeout=HEAD_CHECK_TIMEOUT,
        )
    except (OSError, subprocess.SubprocessError):
        return None
    return result.stdout.strip() if result.returncode == 0 else None


class _MissionCut(subprocess.TimeoutExpired):
    """A mission cut by a bound, carrying which bound so the caller can say so."""

    def __init__(self, cmd, timeout, bound):
        super().__init__(cmd, timeout)
        self.bound = bound


def _drain_output(p, cmd, output, cwd):
    started = time.monotonic()
    heartbeat = started + HEARTBEAT_SECONDS
    next_head_check = started + HEAD_CHECK_SECONDS
    last_progress = started
    exited_at = None
    head = _worktree_head(cwd)
    with selectors.DefaultSelector() as selector:
        selector.register(p.stdout, selectors.EVENT_READ, "stdout")
        selector.register(p.stderr, selectors.EVENT_READ, "stderr")
        while True:
            now = time.monotonic()
            # The mission process is what gets bounded, not the pipes: droid can
            # exit while a tool child still holds them, and a mission that has
            # already finished must not be reported as killed for a quiet tail.
            if p.poll() is not None:
                if exited_at is None:
                    exited_at = now
                if not selector.get_map() or now - exited_at >= MISSION_EXIT_GRACE_SECONDS:
                    _read_available(selector, output, 0)
                    break
                # Drain the tail for a bounded grace, so a child that inherited
                # the pipes can flush what it has.
                _read_available(selector, output, max(0, min(1, MISSION_EXIT_GRACE_SECONDS - (now - exited_at))))
                continue
            if now - started >= MISSION_TIMEOUT:
                raise _MissionCut(cmd, MISSION_TIMEOUT, "ceiling")
            # One liveness mechanism: the clock is reset by droid's output and by
            # a new commit. The poll runs on its schedule, and always once more
            # when the silence deadline arrives, because a commit made since the
            # last poll is progress this clock has not seen yet -- cutting
            # without asking would kill a mission that committed a moment ago.
            if now >= next_head_check or now - last_progress >= LIVENESS_TIMEOUT:
                current = _worktree_head(cwd)
                if current is not None and current != head:
                    head = current
                    last_progress = time.monotonic()
                now = time.monotonic()
                next_head_check = now + HEAD_CHECK_SECONDS
            if now - last_progress >= LIVENESS_TIMEOUT:
                raise _MissionCut(cmd, LIVENESS_TIMEOUT, "silence")
            if now >= heartbeat:
                print(f"[droid] Mission active ({int(now - started)}s elapsed)", flush=True)
                heartbeat = now + HEARTBEAT_SECONDS
            wait = max(0, min(
                1,
                MISSION_TIMEOUT - (now - started),
                LIVENESS_TIMEOUT - (now - last_progress),
                heartbeat - now,
            ))
            if selector.get_map():
                received = _read_available(selector, output, wait)
            else:
                # Both streams closed while the mission is still running. Keep
                # the watchdog alive on its own clock: looping on the pipes
                # alone would leave the ceiling as the only bound.
                time.sleep(wait)
                received = 0
            if received:
                last_progress = time.monotonic()
    p.wait(timeout=max(0.01, MISSION_TIMEOUT - (time.monotonic() - started)))


def _stop_mission(p, terminate):
    if p is None:
        return
    if terminate:
        try:
            os.killpg(p.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        p.wait()
    p.stdout.close()
    p.stderr.close()


def run_mission(prompt: str, cwd: str, is_continue: bool = False, model: str | None = None) -> tuple[int, str, str]:
    """Run a fresh exec, including continuation requests, and return code/stdout/stderr."""
    model_arg = model if (model and model != "default") else DEFAULT_MODEL
    cmd = ["droid", "exec", "--model", model_arg, "--reasoning-effort", REASONING_EFFORT, "--auto", "high", "--output-format", "stream-json", "--cwd", cwd, prompt]
    print(f"[droid] Running mission (continue={is_continue}) in {cwd}", flush=True)
    p = None
    terminate = True
    output = _MissionOutput()
    env = os.environ.copy()
    env["PWD"] = os.path.abspath(cwd)
    env.pop("OLDPWD", None)
    try:
        p = subprocess.Popen(cmd, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True, cwd=cwd, env=env, start_new_session=True)
        _drain_output(p, cmd, output, cwd)
        result = p.returncode, output.stdout_text(), output.stderr.decode(errors="replace")
        terminate = p.returncode != 0
        return result
    except _MissionCut as e:
        if e.bound == "silence":
            return -1, output.stdout_text(), (
                f"Error: Droid mission hung and was forcefully killed after {int(e.timeout)} seconds "
                f"with no output and no new commit."
            )
        return -1, output.stdout_text(), f"Error: Droid mission timed out after {int(e.timeout)} seconds."
    except subprocess.TimeoutExpired:
        # `p.wait` after the drain loop, on a mission that outlived the ceiling.
        return -1, output.stdout_text(), f"Error: Droid mission timed out after {MISSION_TIMEOUT} seconds."
    except Exception as e:
        return -1, output.stdout_text(), str(e)
    finally:
        _stop_mission(p, terminate)
