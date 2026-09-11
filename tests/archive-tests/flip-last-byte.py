#!/usr/bin/env python3
"""Flips the last byte of a file (E2E tamper helper for Windows .bat scripts).

The POSIX .sh scripts inline this as a heredoc; Windows cmd quoting makes an
inline `python -c` fragile, so the .bat calls this small script instead.
Exits nonzero when the file cannot be read or written.
"""

import sys


def main():
    if len(sys.argv) != 2:
        print("usage: flip-last-byte.py <file>", file=sys.stderr)
        return 2
    try:
        with open(sys.argv[1], "r+b") as handle:
            handle.seek(-1, 2)
            byte = handle.read(1)
            if not byte:
                print("empty file: %s" % sys.argv[1], file=sys.stderr)
                return 1
            handle.seek(-1, 2)
            handle.write(bytes([byte[0] ^ 0xFF]))
    except OSError as exc:
        print("cannot tamper %s: %s" % (sys.argv[1], exc), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
