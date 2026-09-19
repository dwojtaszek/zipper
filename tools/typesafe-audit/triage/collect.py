#!/usr/bin/env python3
"""Deterministic preparation for TypeSafe issue triage (#960, dry-run).

Pure functions: markup stripping with size caps, keyword extraction (REQ IDs,
file paths, domain terms), closed-set loading, and a scored duplicate-candidate
search over a supplied issue corpus. Issue text is untrusted DATA throughout —
never executed, never interpolated into shell.
"""

from __future__ import annotations

import json
import re
from pathlib import Path

_REQ_ID = re.compile(r"\bREQ-\d{3,4}\b")
_PATH = re.compile(r"\b[\w.-]+(?:/[\w.-]+)+\.(?:cs|py|md|yml|sh|bat|json)\b")
_MARKUP_PATTERNS = (
    (re.compile(r"<!--.*?-->", re.DOTALL), ""),
    (re.compile(r"!?\[([^\]]*)\]\([^)]*\)"), r"\1"),
    (re.compile(r"[*_`]{1,3}"), ""),
    (re.compile(r"^#{1,6}\s+", re.MULTILINE), ""),
    (re.compile(r"^>\s?", re.MULTILINE), ""),
)


def strip_markup(text: str, max_chars: int) -> str:
    """Markdown/HTML-comment stripped, size-capped untrusted text."""
    stripped = text or ""
    for pattern, replacement in _MARKUP_PATTERNS:
        stripped = pattern.sub(replacement, stripped)
    return stripped[:max_chars]


def extract_keywords(text: str) -> list[str]:
    """REQ IDs, file paths, and lowercase word tokens (>=4 chars), in order."""
    keywords: list[str] = []
    for match in _REQ_ID.findall(text or ""):
        keywords.append(match)
    for match in _PATH.findall(text or ""):
        keywords.append(match)
    for word in re.findall(r"[a-z_]{4,}", (text or "").lower()):
        if word not in keywords:
            keywords.append(word)
    return keywords


def search_candidates(issue: dict, corpus: list[dict], max_candidates: int) -> list[dict]:
    """Deterministic keyword-overlap ranking over open issues."""
    query = set(extract_keywords(f"{issue.get('title', '')} {issue.get('body', '')}"))
    scored = []
    for other in corpus:
        if other.get("number") == issue.get("number"):
            continue
        overlap = len(query & set(extract_keywords(f"{other.get('title', '')} {other.get('body', '')}")))
        scored.append((overlap, other.get("number", 0), other))
    scored.sort(key=lambda item: (-item[0], item[1]))
    return [dict(item[2], score=item[0]) for item in scored[:max_candidates] if item[0] > 0]


def allowed_sets(policy: dict, req_ids: list[str]) -> dict:
    return {
        "subsystems": list(policy["subsystems"]),
        "labels": list(policy["labels"]),
        "req_ids": list(req_ids),
    }


def load_json(path: Path) -> dict:
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)
