#!/usr/bin/env python3
"""Normalizes and validates Stryker JSON mutation reports (#1110).

Locates mutation-report.json under output directory, validates that it contains
>0 selected mutants for the shard scope, normalizes absolute paths to relative,
and writes to the stable path expected by quality/run_check.py.
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from mutation import process_report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Normalize and validate Stryker mutation report.")
    parser.add_argument("--input", "-i", type=Path, required=True, help="Input directory or mutation-report.json file.")
    parser.add_argument("--output", "-o", type=Path, required=True, help="Output normalized mutation-report.json path.")
    parser.add_argument("--scope", "-s", type=str, default="", help="Mutation shard scope used for the run.")
    parser.add_argument("--repo-root", type=Path, default=Path.cwd(), help="Repository root.")
    args = parser.parse_args(argv)

    try:
        norm = process_report(args.input, args.output, args.repo_root, scope=args.scope)
        files_count = len(norm.get("files", {}))
        print(f"normalize-report: processed report with {files_count} file(s) into {args.output}")
        return 0
    except (FileNotFoundError, ValueError) as exc:
        print(f"normalize-report: error: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
