#!/usr/bin/env python3
"""Read-only dump of user turns from local OpenCode sessions for one repository."""

import argparse
import os
import re
import sqlite3
import subprocess
import sys
from datetime import datetime, timedelta, timezone

DB = os.environ.get(
    "OPENCODE_DB", os.path.expanduser("~/.local/share/opencode/opencode.db")
)

CUE = re.compile(
    r"\b(?:no|nope|don'?t|never|stop|instead|actually|wrong|again|still|why|"
    r"should|must|revert|undo|wait|remove|fix|not)\b",
    re.IGNORECASE,
)

SKIP = {
    "continue",
    "go on",
    "ok",
    "okay",
    "good job",
    "resume",
    "yes",
    "no",
}


def repo_root(path):
    try:
        common = subprocess.check_output(
            ["git", "-C", path, "rev-parse", "--path-format=absolute", "--git-common-dir"],
            text=True,
            stderr=subprocess.DEVNULL,
        ).strip()
    except (OSError, subprocess.CalledProcessError):
        return os.path.realpath(path)
    return os.path.dirname(common)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", default=os.getcwd(), help="repository path")
    parser.add_argument("--days", type=int, default=30, help="lookback window")
    parser.add_argument(
        "--corrections",
        action="store_true",
        help="only user turns that look like a correction",
    )
    parser.add_argument("--full", action="store_true", help="do not truncate text")
    parser.add_argument("--limit", type=int, default=500)
    args = parser.parse_args()

    if not os.path.exists(DB):
        sys.exit(f"opencode db not found: {DB}")

    repo = repo_root(os.path.realpath(args.repo))
    since = int(
        (datetime.now(timezone.utc) - timedelta(days=args.days)).timestamp() * 1000
    )

    con = sqlite3.connect(f"file:{DB}?mode=ro", uri=True)
    rows = con.execute(
        """
        SELECT s.title, s.directory, m.time_created, json_extract(m.data, '$.text')
        FROM session_message m
        JOIN session_v2 s ON s.id = m.session_id
        JOIN project p ON p.id = s.project_id
        WHERE m.type = 'user'
          AND s.parent_id IS NULL
          AND p.worktree = ?
          AND m.time_created >= ?
        ORDER BY m.time_created DESC
        LIMIT ?
        """,
        (repo, since, args.limit),
    ).fetchall()

    shown = 0
    for title, _directory, created, text in reversed(rows):
        text = " ".join((text or "").split())
        if not text or text.lower() in SKIP:
            continue
        if args.corrections and not CUE.search(text):
            continue
        if not args.full and len(text) > 400:
            text = text[:400] + "..."
        stamp = datetime.fromtimestamp(created / 1000).strftime("%Y-%m-%d %H:%M")
        print(f"{stamp} [{(title or 'untitled')[:50]}] {text}")
        shown += 1

    print(f"\n{shown} user turns from {repo} in the last {args.days} days.")


if __name__ == "__main__":
    main()
