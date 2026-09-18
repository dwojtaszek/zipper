#!/usr/bin/env python3
"""Collect PR context for the spec compliance side check (#958) via the `gh` CLI.

Writes the pre-collected JSON consumed by run_check.py --pr-json:
explicit issue refs from the PR body (never branch names), changed files with
patches, and referenced issue bodies + comments in chronological order.
"""

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import collect  # noqa: E402


def gh(*args: str) -> dict | list:
    result = subprocess.run(
        ["gh", *args],
        capture_output=True,
        text=True,
        check=True,
    )
    return json.loads(result.stdout)


def main() -> None:
    if len(sys.argv) != 3:
        print(__doc__)
        sys.exit(2)
    pr_number = sys.argv[1]
    out_path = Path(sys.argv[2])

    pr = gh(
        "pr", "view", pr_number,
        "--json", "number,body,baseRefOid,headRefOid,files",
    )
    files = [
        {"path": f["path"], "additions": f.get("additions", 0), "patch": (f.get("patch") or "")}
        for f in pr.get("files", [])
    ]

    refs = collect.extract_issue_refs(pr.get("body") or "")
    issues: dict[str, dict] = {}
    for ref in refs:
        issue = gh("issue", "view", str(ref), "--json", "number,body,comments")
        # Human comments only: bot comments (robots, CI summaries) are noise
        # for the judgment and would bloat the state budget (review, #958).
        comments = [
            {"order": index, "author": c.get("author", {}).get("login", "unknown"), "body": c.get("body", "")}
            for index, c in enumerate(issue.get("comments", []))
            if not c.get("author", {}).get("login", "").endswith("[bot]")
        ]
        issues[str(ref)] = {"number": issue.get("number", ref), "body": issue.get("body", ""), "comments": comments}

    payload = {
        "pr": pr.get("number", int(pr_number)),
        "base": pr.get("baseRefOid", ""),
        "head": pr.get("headRefOid", ""),
        "body": pr.get("body") or "",
        "files": files,
        "issues": issues,
    }
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"collected PR {payload['pr']}: {len(files)} changed file(s), refs {refs}")


if __name__ == "__main__":
    main()
