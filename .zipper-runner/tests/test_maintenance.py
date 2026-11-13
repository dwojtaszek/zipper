"""Exercise timer restoration across real child-process and signal boundaries."""
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import unittest

BASE = Path(__file__).resolve().parents[1]


@unittest.skipIf(os.name == 'nt', 'systemd maintenance is POSIX-only')
class MaintenanceTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        runner = self.root / '.zipper-runner'
        runner.mkdir()
        shutil.copy(BASE / 'maintenance.py', runner)
        (runner / 'cron-wrapper.sh').write_text('#!/bin/sh\nexit "${PREFLIGHT_STATUS:-0}"\n')
        binary = self.root / 'bin'
        binary.mkdir()
        control = binary / 'systemctl'
        control.write_text(
            '#!' + sys.executable + '\n'
            'import json, os, pathlib, signal, sys\n'
            'root = pathlib.Path(os.environ["CONTROL_ROOT"])\n'
            'state = json.loads((root / "state.json").read_text())\n'
            'args = [arg for arg in sys.argv[2:] if arg != "--quiet"]\n'
            'with (root / "calls").open("a") as log: log.write(json.dumps(args) + "\\n")\n'
            'if args[0] == "is-active" and os.environ.get("INTERRUPT_SNAPSHOT"):\n'
            '    os.kill(os.getppid(), signal.SIGTERM)\n'
            'if args[0] == "is-active":\n'
            '    print(state[args[1]])\n'
            '    raise SystemExit(0 if state[args[1]] == "active" else 3)\n'
            'for timer in args[1:]:\n'
            '    if not (args[0] == "start" and os.environ.get("START_NOOP")):\n'
            '        state[timer] = "active" if args[0] == "start" else "inactive"\n'
            '    (root / "state.json").write_text(json.dumps(state))\n'
            '    if args[0] == "stop" and os.environ.get("STOP_FAIL"): raise SystemExit(5)\n')
        control.chmod(0o755)
        self.original = {'zipper-intake.timer': 'active', 'zipper-babysit.timer': 'inactive',
                         'zipper-deploy.timer': 'active'}
        (self.root / 'state.json').write_text(json.dumps(self.original))
        self.env = {**os.environ, 'CONTROL_ROOT': str(self.root), 'STATE_DIR': str(self.root / 'state'),
                    'PATH': str(binary) + os.pathsep + os.environ['PATH']}
        self.command = [sys.executable, str(runner / 'maintenance.py'), '--', sys.executable, '-c']

    def state(self):
        return json.loads((self.root / 'state.json').read_text())

    def run_command(self, code, **env):
        return subprocess.run([*self.command, code], env={**self.env, **env},
                              capture_output=True, text=True, timeout=15)

    def cleanup_process(self, process, ready):
        if ready.exists():
            try:
                pid = int(ready.read_text())
                group = os.getpgid(pid)
                if group == os.getpgrp():
                    os.kill(pid, signal.SIGKILL)
                else:
                    os.killpg(group, signal.SIGKILL)
            except ProcessLookupError:
                pass
        if process.poll() is None:
            process.kill()
        try:
            process.communicate(timeout=2)
        except subprocess.TimeoutExpired:
            process.stdout.close()
            process.stderr.close()
            process.wait(timeout=2)

    def test_success_and_failure_restore_only_originally_active_timers(self):
        for status in (0, 7):
            with self.subTest(status=status):
                result = self.run_command(
                    'import json, pathlib; '
                    f'state=json.loads(pathlib.Path({str(self.root / "state.json")!r}).read_text()); '
                    f'assert set(state.values()) == {{"inactive"}}; raise SystemExit({status})')
                self.assertEqual(result.returncode, status, result.stdout + result.stderr)
                self.assertEqual(self.state(), self.original)

    def test_preflight_failure_never_pauses_timers_or_runs_the_command(self):
        result = self.run_command('raise AssertionError("must not run")', PREFLIGHT_STATUS='9')
        self.assertEqual(result.returncode, 9, result.stdout + result.stderr)
        self.assertEqual(self.state(), self.original)
        self.assertFalse((self.root / 'calls').exists())

    def test_partial_stop_failure_still_restores_original_state(self):
        sentinel = self.root / 'unexpected-stop-command'
        result = self.run_command(f'__import__("pathlib").Path({str(sentinel)!r}).touch()', STOP_FAIL='1')
        self.assertFalse(sentinel.exists(), 'command ran after the stop failure')
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(self.state(), self.original)

    def test_successful_start_must_actually_restore_active_state(self):
        result = self.run_command('raise SystemExit(7)', START_NOOP='1')
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn('restore', result.stderr.lower())

    def test_normal_exit_does_not_leave_maintenance_grandchildren_running(self):
        ready = self.root / 'background-ready'
        code = ('import subprocess,sys,time; '
                f'child=subprocess.Popen([sys.executable,"-c","import time; time.sleep(60)"]); '
                f'__import__("pathlib").Path({str(ready)!r}).write_text(str(child.pid))')
        process = subprocess.Popen([*self.command, code], env=self.env,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            output, error = process.communicate(timeout=8)
            self.assertEqual(process.returncode, 0, output + error)
            self.assertTrue(ready.exists())
            self.assertEqual(self.state(), self.original)
        finally:
            self.cleanup_process(process, ready)

    def test_interrupt_kills_non_cooperative_grandchildren_before_timer_restore(self):
        ready = self.root / 'grandchild-ready'
        heartbeat = self.root / 'grandchild-heartbeat'
        grandchild = ('import os, pathlib, signal, time\n'
                      'signal.signal(signal.SIGTERM, signal.SIG_IGN)\n'
                      f'pathlib.Path({str(ready)!r}).write_text(str(os.getpid()))\n'
                      f'path = pathlib.Path({str(heartbeat)!r})\n'
                      'while True:\n    path.write_text(str(time.time()))\n    time.sleep(0.02)\n')
        leader = f'import subprocess,sys,time; subprocess.Popen([sys.executable,"-c",{grandchild!r}]); time.sleep(60)'
        process = subprocess.Popen([*self.command, leader], env=self.env,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            deadline = time.monotonic() + 5
            while not heartbeat.exists() and time.monotonic() < deadline:
                time.sleep(0.02)
            self.assertTrue(ready.exists())
            os.getpgid(int(ready.read_text()))
            process.terminate()
            output, error = process.communicate(timeout=8)
            self.assertEqual(process.returncode, 143, output + error)
            self.assertEqual(self.state(), self.original)
            last = heartbeat.read_text()
            time.sleep(0.1)
            self.assertEqual(heartbeat.read_text(), last, 'maintenance grandchild kept running')
        finally:
            self.cleanup_process(process, ready)

    def test_interrupt_terminates_owned_child_and_restores_timers(self):
        for number in (signal.SIGINT, signal.SIGTERM):
            with self.subTest(signal=number):
                ready = self.root / 'ready'
                ready.unlink(missing_ok=True)
                code = ('import os, pathlib, time; '
                        f'pathlib.Path({str(ready)!r}).write_text(str(os.getpid())); time.sleep(60)')
                process = subprocess.Popen([*self.command, code], env=self.env,
                                           stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
                try:
                    deadline = time.monotonic() + 5
                    while not ready.exists() and time.monotonic() < deadline:
                        time.sleep(0.02)
                    self.assertTrue(ready.exists(), 'maintenance child did not start')
                    child_pid = int(ready.read_text())
                    process.send_signal(number)
                    output, error = process.communicate(timeout=8)
                    self.assertEqual(process.returncode, 128 + number, output + error)
                    self.assertEqual(self.state(), self.original)
                    with self.assertRaises(ProcessLookupError):
                        os.kill(child_pid, 0)
                finally:
                    self.cleanup_process(process, ready)

    def test_restoration_failure_overrides_interrupt_status(self):
        ready = self.root / 'restore-failure-ready'
        process = subprocess.Popen([*self.command,
            f'import pathlib,os,time; pathlib.Path({str(ready)!r}).write_text(str(os.getpid())); time.sleep(60)'],
            env={**self.env, 'START_NOOP': '1'}, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            deadline = time.monotonic() + 5
            while not ready.exists() and time.monotonic() < deadline:
                time.sleep(0.02)
            self.assertTrue(ready.exists())
            process.terminate()
            output, error = process.communicate(timeout=8)
            self.assertEqual(process.returncode, 1, output + error)
            self.assertIn('Failed to restore', error)
        finally:
            self.cleanup_process(process, ready)

    def test_overlapping_maintenance_is_rejected_without_restoring_timers(self):
        ready = self.root / 'locked-ready'
        first = subprocess.Popen([*self.command,
            f'import pathlib,os,time; pathlib.Path({str(ready)!r}).write_text(str(os.getpid())); time.sleep(60)'],
            env=self.env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            deadline = time.monotonic() + 5
            while not ready.exists() and time.monotonic() < deadline:
                time.sleep(0.02)
            self.assertTrue(ready.exists())
            sentinel = self.root / 'unexpected-overlap-command'
            second = self.run_command(f'__import__("pathlib").Path({str(sentinel)!r}).touch()')
            self.assertFalse(sentinel.exists(), 'overlapping command ran')
            self.assertNotEqual(second.returncode, 0)
            self.assertTrue(all(value == 'inactive' for value in self.state().values()))
            first.terminate()
            first.communicate(timeout=8)
            self.assertEqual(self.state(), self.original)
        finally:
            self.cleanup_process(first, ready)

    def test_cancellation_during_snapshot_never_stops_timers(self):
        result = self.run_command('raise AssertionError("cancelled command ran")', INTERRUPT_SNAPSHOT='1')
        self.assertEqual(result.returncode, 143, result.stdout + result.stderr)
        self.assertEqual(self.state(), self.original)
        calls = [json.loads(line) for line in (self.root / 'calls').read_text().splitlines()]
        self.assertFalse(any(call[0] == 'stop' for call in calls))

    def test_signal_between_spawn_and_assignment_keeps_child_owned(self):
        ready = self.root / 'spawned-pid'
        bootstrap = self.root / 'spawn-boundary.py'
        bootstrap.write_text(
            'import os, pathlib, runpy, signal, subprocess, sys\n'
            'original = subprocess.Popen\n'
            'def spawn(args, *rest, **kwargs):\n'
            '    child = original(args, *rest, **kwargs)\n'
            '    if args[:2] == [sys.executable, "-c"]:\n'
            f'        pathlib.Path({str(ready)!r}).write_text(str(child.pid))\n'
            '        os.kill(os.getpid(), signal.SIGTERM)\n'
            '    return child\n'
            'subprocess.Popen = spawn\n'
            f'sys.argv = [{str(self.root / ".zipper-runner/maintenance.py")!r}, *sys.argv[1:]]\n'
            'runpy.run_path(sys.argv[0], run_name="__main__")\n')
        process = subprocess.Popen([sys.executable, str(bootstrap), *self.command[2:],
                                    'import time; time.sleep(60)'], env=self.env,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        try:
            output, error = process.communicate(timeout=9)
            self.assertEqual(process.returncode, 143, output + error)
            self.assertEqual(self.state(), self.original)
            with self.assertRaises(ProcessLookupError):
                os.kill(int(ready.read_text()), 0)
        finally:
            self.cleanup_process(process, ready)

    def test_signal_at_cleanup_entry_cannot_skip_restoration(self):
        bootstrap = self.root / 'cleanup-boundary.py'
        bootstrap.write_text(
            'import os, runpy, signal, sys\n'
            'original = signal.signal\n'
            'sent = False\n'
            'def install(number, handler):\n'
            '    global sent\n'
            '    if handler == signal.SIG_IGN and not sent:\n'
            '        sent = True\n'
            '        os.kill(os.getpid(), signal.SIGTERM)\n'
            '    return original(number, handler)\n'
            'signal.signal = install\n'
            f'sys.argv = [{str(self.root / ".zipper-runner/maintenance.py")!r}, *sys.argv[1:]]\n'
            'runpy.run_path(sys.argv[0], run_name="__main__")\n')
        result = subprocess.run([sys.executable, str(bootstrap), *self.command[2:], 'pass'],
                                env=self.env, capture_output=True, text=True, timeout=10)
        self.assertEqual(self.state(), self.original, result.stdout + result.stderr)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
