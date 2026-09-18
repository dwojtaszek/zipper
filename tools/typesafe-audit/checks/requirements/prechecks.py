#!/usr/bin/env python3
"""Deterministic prechecks for the requirements audit (#956).

Runs before any TypeSafe request. These are exact, non-semantic defects:
immutable REQ IDs, orphan references, and missing traceability rows.
Glossary term lookup is deterministic too: non-canonical term *detection* is
TypeSafe's judgment (question 3), but glossary existence is checked here.
"""

from __future__ import annotations

import re
import subprocess
from pathlib import Path

REQ_ID_PATTERN = re.compile(r"REQ-\d+", re.IGNORECASE)
REQ_DEFINITION_PATTERN = re.compile(r"\*\*(REQ-\d+)\*\*\s*:", re.IGNORECASE)


def _run_git(args: list[str], repo_root: Path) -> str:
    result = subprocess.run(
        ["git", "-C", str(repo_root), *args], capture_output=True, text=True, check=False
    )
    return result.stdout if result.returncode == 0 else ""


def load_requirements(repo_root: Path) -> dict[str, tuple[int, str]]:
    """Map defined REQ-NNN (upper) -> (line number, definition line).

    Only bold `**REQ-N**:` headers count as definitions; body-text mentions
    (e.g. cross-references) do not. Line numbers are 1-based for evidence.
    """
    requirements: dict[str, tuple[int, str]] = {}
    source = Path(repo_root, "Requirements.md")
    if not source.is_file():
        return requirements
    for line_no, line in enumerate(source.read_text(encoding="utf-8").splitlines(), start=1):
        for match in REQ_DEFINITION_PATTERN.finditer(line):
            requirements.setdefault(match.group(1).upper(), (line_no, line))
    return requirements


def requirement_definitions(repo_root: Path) -> dict[str, str]:
    """Map defined REQ-NNN (upper) -> definition text (compatibility view)."""
    return {req_id: text for req_id, (_line_no, text) in load_requirements(repo_root).items()}


def load_glossary_terms(repo_root: Path) -> set[str]:
    """Canonical domain terms: Markdown headings in UBIQUITOUS_LANGUAGE.md."""
    source = Path(repo_root, "UBIQUITOUS_LANGUAGE.md")
    if not source.is_file():
        return set()
    return {
        m.group(1).strip()
        for line in source.read_text(encoding="utf-8").splitlines()
        if (m := re.match(r"#{1,4}\s+(.+?)\s*$", line))
    }


def load_traced_requirements(repo_root: Path) -> set[str]:
    traced: set[str] = set()
    source = Path(repo_root, "tests/req-traceability.tsv")
    if not source.is_file():
        return traced
    for line in source.read_text(encoding="utf-8").splitlines():
        first = line.split("\t", 1)[0].strip()
        if REQ_ID_PATTERN.fullmatch(first):
            traced.add(first.upper())
    return traced


def check_immutable_ids(repo_root: Path, base: str, head: str = "HEAD") -> list[dict]:
    """A REQ ID present in base must still exist with content in head (no renumbering)."""
    findings: list[dict] = []
    base_text = _run_git(["show", f"{base}:Requirements.md"], repo_root)
    base_ids = set(REQ_ID_PATTERN.findall(base_text))
    head_requirements = load_requirements(repo_root)
    for req_id in sorted(base_ids):
        if req_id.upper() not in head_requirements:
            findings.append({
                "check": "immutable-req-id",
                "severity": "defect",
                "source": "Requirements.md",
                "req_id": req_id.upper(),
                "message": f"Requirement {req_id.upper()} existed at {base} but is gone or renumbered at {head}. REQ IDs are immutable (AGENTS.md Critical Rule 2).",
            })
    return findings


def check_orphan_references(repo_root: Path, sections: list[dict]) -> list[dict]:
    """REQ IDs referenced in changed sections must exist in Requirements.md."""
    requirements = load_requirements(repo_root)
    findings: list[dict] = []
    for section in sections:
        for req_id in REQ_ID_PATTERN.findall(section["text"]):
            if req_id.upper() not in requirements:
                findings.append({
                    "check": "orphan-reference",
                    "severity": "defect",
                    "source": f"{section['path']}:{section['start_line']}-{section['end_line']}",
                    "req_id": req_id.upper(),
                    "message": f"Changed section references {req_id.upper()}, which is not defined in Requirements.md.",
                })
    return findings


def check_traceability(repo_root: Path, sections: list[dict]) -> list[dict]:
    """Changed requirement IDs must have traceability rows."""
    traced = load_traced_requirements(repo_root)
    findings: list[dict] = []
    seen: set[str] = set()
    for section in sections:
        if Path(section["path"]).name != "Requirements.md":
            continue
        for req_id in section["req_ids"]:
            if req_id in seen or req_id in traced:
                continue
            seen.add(req_id)
            findings.append({
                "check": "missing-traceability",
                "severity": "defect",
                "source": f"{section['path']}:{section['start_line']}-{section['end_line']}",
                "req_id": req_id,
                "message": f"Changed requirement {req_id} has no row in tests/req-traceability.tsv.",
            })
    return findings


def run_all_prechecks(repo_root: Path, base: str, sections: list[dict], head: str = "HEAD") -> list[dict]:
    findings = check_immutable_ids(repo_root, base, head)
    findings.extend(check_orphan_references(repo_root, sections))
    findings.extend(check_traceability(repo_root, sections))
    return findings
