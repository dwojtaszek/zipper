#!/usr/bin/env python3
"""Deterministic changed-section extraction for the requirements audit (#956).

Parses `git diff -U0 base...head` over audited documentation files and groups
changed hunks into named sections with the requirement IDs they touch. TypeSafe
only ever sees these candidate spans — it cannot invent a path or REQ ID.
"""

from __future__ import annotations

import re
import subprocess
from pathlib import Path

AUDITED_FILES = (
    "Requirements.md",
    "README.md",
    "UBIQUITOUS_LANGUAGE.md",
    "docs/architecture.md",
)

REQ_ID_PATTERN = re.compile(r"REQ-\d+", re.IGNORECASE)
CONTEXT_LINES_BEFORE = 2
CONTEXT_LINES_AFTER = 2


def _run_git(args: list[str], repo_root: Path) -> str:
    result = subprocess.run(
        ["git", "-C", str(repo_root), *args],
        capture_output=True,
        text=True,
        check=False,
    )
    if result.returncode != 0:
        raise RuntimeError(f"git {' '.join(args[:2])} failed: {result.stderr.strip()}")
    return result.stdout


def _head_sections(repo_root: Path, path: str) -> list[str]:
    source = Path(repo_root, path)
    return source.read_text(encoding="utf-8").splitlines() if source.is_file() else []


def extract_sections(repo_root: Path, base: str, head: str = "HEAD") -> list[dict]:
    """Return one section per changed hunk: path, line range, text, req_ids."""
    diff_output = _run_git(
        ["diff", "-U0", f"{base}...{head}", "--", *AUDITED_FILES], repo_root
    )
    sections: list[dict] = []
    current_file: str | None = None
    deleted_file = False
    head_lines: list[str] = []

    for line in diff_output.splitlines():
        if line.startswith("+++ b/"):
            current_file = line[6:]
            deleted_file = False
            head_lines = _head_sections(repo_root, current_file)
        elif line.startswith("+++ /dev/null"):
            # Deleted file: no head-side content exists to audit; hunks that
            # follow belong to /dev/null and must not be attributed to the
            # previously seen file.
            current_file = None
            deleted_file = True
        elif line.startswith("@@"):
            if current_file is None or deleted_file:
                continue
            match = re.search(r"\+(\d+)(?:,(\d+))?", line)
            if not match:
                continue
            start = int(match.group(1))
            count = int(match.group(2) or "1")
            if count == 0:
                continue
            end = start + count - 1
            ctx_start = max(0, start - 1 - CONTEXT_LINES_BEFORE)
            ctx_end = min(len(head_lines), end + CONTEXT_LINES_AFTER)
            text = "\n".join(head_lines[ctx_start:ctx_end])
            touched_ids = sorted(set(REQ_ID_PATTERN.findall(text)))
            # Neighbor requirement definitions directly referenced by the hunk
            # (its own text or an adjacent ID) join the section, per issue #956.
            if not touched_ids:
                nearby = "\n".join(head_lines[max(0, start - 6):min(len(head_lines), end + 6)])
                touched_ids = sorted(set(REQ_ID_PATTERN.findall(nearby)))
            sections.append({
                "path": current_file,
                "start_line": start,
                "end_line": end,
                "text": text,
                "req_ids": touched_ids,
            })
    return sections


def group_by_requirement(sections: list[dict]) -> dict[str, list[dict]]:
    """Group sections by requirement ID; unattributed sections group by path."""
    grouped: dict[str, list[dict]] = {}
    for section in sections:
        keys = section["req_ids"] or [f"path:{section['path']}"]
        for key in keys:
            grouped.setdefault(key, []).append(section)
    return grouped
