#!/usr/bin/env python3
"""Classify conservative docs-only and runner-only changes for local and CI gates."""
import argparse
import os
import re
import subprocess
import sys

DOCS = re.compile(r'^(docs/|CHANGELOG(\.md)?$|.*\.(md|markdown)$)')


def is_doc(path):
    return path != '.zipper-runner/AGENT_PREFERENCES.md' and bool(DOCS.search(path))


def classify(paths):
    if any(path.startswith('/') or '..' in path.split('/') for path in paths):
        return False, False
    docs_only = bool(paths) and all(is_doc(path) for path in paths)
    runner_only = (bool(paths) and not docs_only
                   and all(path.startswith('.zipper-runner/') or is_doc(path) for path in paths))
    return docs_only, runner_only


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--staged', action='store_true')
    parser.add_argument('--base')
    parser.add_argument('--head', default='HEAD')
    parser.add_argument('--lines', action='store_true', help='Read legacy newline-separated path lists instead of NUL paths.')
    args = parser.parse_args()
    if args.staged and args.base:
        parser.error('--staged and --base are mutually exclusive')
    if args.staged or args.base:
        command = ['git', 'diff', '--name-only', '--no-renames', '-z']
        command += ['--cached'] if args.staged else [args.base, args.head]
        try:
            data = subprocess.check_output(command)
        except subprocess.CalledProcessError as error:
            return error.returncode
        paths = [os.fsdecode(path) for path in data.split(b'\0') if path]
    else:
        data = sys.stdin.buffer.read()
        paths = [os.fsdecode(path) for path in (data.splitlines() if args.lines else data.split(b'\0')) if path]
    docs, runner = classify(paths)
    print('only_docs=' + str(docs).lower())
    print('only_runner=' + str(runner).lower())
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
