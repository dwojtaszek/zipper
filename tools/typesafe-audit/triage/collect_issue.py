#!/usr/bin/env python3
"""Collect one issue's context for triage (#960) via the `gh` CLI.

Writes the pre-collected JSON consumed by run_check.py --issue-json. The
issue's text travels only as JSON data; nothing from the issue is interpolated
into shell commands. Also retrieves the deterministic duplicate-candidate
corpus: currently-open issues.
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import collect  # noqa: E402


def gh(*args: str) -> dict | list:
    result = subprocess.run(["gh", *args], capture_output=True, text=True, check=True)
    return json.loads(result.stdout)


def main() -> None:
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    if not sys.argv[1].isdigit() or int(sys.argv[1]) <= 0:
        print(f"triage-collect: input error: issue number must be a positive integer, got {sys.argv[1]!r}.", file=sys.stderr)
        sys.exit(2)
    issue_number = sys.argv[1]
    out_path = Path(sys.argv[2])

    issue = gh("issue", "view", issue_number, "--json", "number,title,body,labels,comments")
    comments = [
        {"order": index, "author": c.get("author", {}).get("login", "unknown"), "body": c.get("body", "")}
        for index, c in enumerate(issue.get("comments", []))
        if not c.get("author", {}).get("login", "").endswith("[bot]")
    ]

    open_issues = gh(
        "issue", "list", "--state", "open", "--limit", "100",
        "--json", "number,title,body,state,labels,url",
    )

    payload = {
        "number": issue.get("number", int(issue_number)),
        "title": issue.get("title", ""),
        "body": issue.get("body", ""),
        "labels": [l.get("name", "") for l in issue.get("labels", [])],
        "comments": comments,
        "open_issues": open_issues,
    }
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"collected issue {payload['number']} with {len(open_issues)} open candidate(s)")


if __name__ == "__main__":
    main()
