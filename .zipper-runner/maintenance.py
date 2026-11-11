#!/usr/bin/env python3
"""Pause runner timers for one command and restore their original active states."""
import argparse
import fcntl
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent.parent
TIMERS = ('zipper-intake.timer', 'zipper-babysit.timer', 'zipper-deploy.timer')


def active_timers():
    active = []
    for timer in TIMERS:
        state = subprocess.run(['systemctl', '--user', 'is-active', timer],
                               capture_output=True, text=True, timeout=30)
        if state.returncode == 0 and state.stdout.strip() == 'active':
            active.append(timer)
        elif state.returncode != 3 or state.stdout.strip() not in ('inactive', 'failed'):
            raise RuntimeError(f'Cannot determine state of {timer}: {state.stderr.strip()}')
    return active


def restore_timers(timers):
    if not timers:
        return 0
    result = subprocess.run(['systemctl', '--user', 'start', *timers], timeout=30)
    failed = [timer for timer in timers if subprocess.run(
        ['systemctl', '--user', 'is-active', '--quiet', timer],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=30).returncode]
    if result.returncode or failed:
        print('Failed to restore runner timers: ' + ', '.join(failed or timers), file=sys.stderr)
        raise SystemExit(result.returncode or 1)
    return 0


def stop_owned_group(child):
    try:
        os.killpg(child.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    child.wait(timeout=5)
    deadline = time.monotonic() + 5
    while True:
        # Zombies cannot execute; their parent owns reaping them.
        processes = subprocess.check_output(['ps', '-axo', 'pgid=,stat='], text=True, timeout=5)
        if not any(parts[0] == str(child.pid) and not parts[1].startswith('Z')
                   for line in processes.splitlines() if len(parts := line.split()) >= 2):
            return
        if time.monotonic() >= deadline:
            raise RuntimeError('Maintenance descendants did not terminate')
        time.sleep(0.02)


def finish_child(child):
    try:
        child.wait(timeout=5)
    except subprocess.TimeoutExpired:
        pass
    finally:
        stop_owned_group(child)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not command:
        parser.error('provide a maintenance command after --')
    child = None
    restore = []
    status = 1
    state = Path(os.environ.get('STATE_DIR', ROOT / '.zipper-runner/state'))
    state.mkdir(parents=True, exist_ok=True)
    lock = (state / 'maintenance.lock').open('a')

    def interrupted(number, _frame):
        if child is not None:
            try:
                os.killpg(child.pid, number)
            except ProcessLookupError:
                pass
        raise SystemExit(128 + number)

    def run(arguments):
        nonlocal child
        child = subprocess.Popen(arguments, cwd=ROOT, start_new_session=True)
        code = child.wait()
        stop_owned_group(child)
        child = None
        return code if code >= 0 else 128 - code

    for number in (signal.SIGINT, signal.SIGTERM):
        signal.signal(number, interrupted)
    try:
        status = run(['bash', str(ROOT / '.zipper-runner/cron-wrapper.sh'), '--preflight'])
        if status:
            return status
        # One owner spans the snapshot, command, and restoration interval.
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        restore = active_timers()
        if restore:
            subprocess.run(['systemctl', '--user', 'stop', *restore], check=True, timeout=30)
        status = run(command)
    finally:
        for number in (signal.SIGINT, signal.SIGTERM):
            signal.signal(number, signal.SIG_IGN)
        try:
            if child is not None:
                finish_child(child)
        finally:
            try:
                restore_timers(restore)
            finally:
                lock.close()
    return status


if __name__ == '__main__':
    raise SystemExit(main())
