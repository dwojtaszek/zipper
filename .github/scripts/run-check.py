#!/usr/bin/env python3
"""Keep full check logs on disk and return a bounded diagnostic with the real status."""
import argparse
import re
from collections import deque
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--log', required=True, type=Path)
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not command:
        parser.error('a command is required after --')
    args.log.parent.mkdir(parents=True, exist_ok=True)
    with args.log.open('wb') as log:
        try:
            status = subprocess.run(command, stdout=log, stderr=subprocess.STDOUT).returncode
        except OSError as error:
            log.write(str(error).encode('utf-8'))
            status = 127
    status = status if status >= 0 else 128 - status
    tail = deque(maxlen=20)
    failure = []
    capturing = False
    with args.log.open(encoding='utf-8', errors='replace') as log:
        for raw in log:
            line = raw.rstrip()[:500]
            tail.append(line)
            if status and re.match(r'^(FAIL:|ERROR:|Traceback \(most recent call last\):|\s+Failed )', line):
                capturing = True
            if capturing and len(failure) < 12:
                failure.append(line)
    for line in failure + list(tail)[-8:] if failure else tail:
        print(line)
    print(f'exit_status={status} log={args.log.resolve()}')
    return status


if __name__ == '__main__':
    raise SystemExit(main())
