#!/usr/bin/env python3
"""Deterministic parsing/resolution for the semantic traceability audit (#957).

Resolves `unit` references (`Class.Method`) to exact test method bodies and
`e2e` references (`script.sh Scenario`) to exact scenario blocks. Unresolved,
malformed, or ambiguous references raise ParseError — the caller must fail
deterministically BEFORE any model call.
"""

from __future__ import annotations

import hashlib
import re
from pathlib import Path

TSV_HEADER = ("req_id", "coverage", "reference", "notes")
VALID_COVERAGES = {"unit", "e2e", "exemption"}


class ParseError(Exception):
    """Deterministic input problem: fail before any TypeSafe call."""


def sha256_text(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def parse_tsv(path: Path) -> list[dict]:
    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines:
        raise ParseError(f"{path} is empty.")
    header = tuple(col.strip().lower() for col in lines[0].split("\t"))
    if header != TSV_HEADER:
        raise ParseError(f"{path} header must be {TSV_HEADER}, got {header}.")
    rows = []
    for line_no, line in enumerate(lines[1:], start=2):
        if not line.strip():
            continue
        cols = line.split("\t")
        if len(cols) != 4:
            raise ParseError(f"{path}:{line_no}: expected 4 tab-separated columns, got {len(cols)}.")
        row = {
            "req_id": cols[0].strip(),
            "coverage": cols[1].strip().lower(),
            "reference": cols[2].strip(),
            "notes": cols[3].strip(),
            "line_no": line_no,
        }
        if not re.fullmatch(r"REQ-\d+", row["req_id"]):
            raise ParseError(f"{path}:{line_no}: malformed REQ id '{row['req_id']}'.")
        if row["coverage"] not in VALID_COVERAGES:
            raise ParseError(f"{path}:{line_no}: coverage must be one of {sorted(VALID_COVERAGES)}, got '{row['coverage']}'.")
        if row["coverage"] == "exemption":
            if row["reference"] not in ("-", ""):
                raise ParseError(f"{path}:{line_no}: exemption rows must use reference '-'.")
            if not row["notes"]:
                raise ParseError(f"{path}:{line_no}: exemption rows require a rationale in notes.")
        elif row["reference"] in ("-", ""):
            raise ParseError(f"{path}:{line_no}: {row['coverage']} rows require a reference.")
        rows.append(row)
    return rows


def _read(path: Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def _extract_brace_body(lines: list[str], start_idx: int) -> str:
    """Return the brace-balanced method body starting at `start_idx`."""
    depth = 0
    body: list[str] = []
    started = False
    for line in lines[start_idx:]:
        for ch in line:
            if ch == "{":
                depth += 1
                started = True
            elif ch == "}":
                depth -= 1
        body.append(line)
        if started and depth <= 0:
            break
    return "\n".join(body)


def _find_class_files(tests_root: Path, class_name: str) -> list[Path]:
    candidates = []
    for cs_file in tests_root.rglob("*.cs"):
        content = _read(cs_file)
        if re.search(rf"(partial\s+)?class\s+{re.escape(class_name)}\b", content):
            candidates.append(cs_file)
    return candidates


def _method_occurrences(cs_file: Path, method_name: str) -> list[tuple[int, str]]:
    content = _read(cs_file)
    lines = content.splitlines()
    occurrences = []
    pattern = re.compile(
        rf"(public|private|internal|protected)?\s*(?:static\s+)?(?:async\s+)?"
        rf"(?:Task\s*<[^>]+>|Task|void|ValueTask|int|string|bool)\s+{re.escape(method_name)}\s*\("
    )
    for idx, line in enumerate(lines):
        if pattern.search(line):
            occurrences.append((idx, _extract_brace_body(lines, idx)))
    return occurrences


def resolve_unit_reference(tests_root: Path, reference: str) -> dict:
    """Resolve `Class.Method` to its test body. Ambiguity/unresolved = ParseError."""
    parts = reference.split(".")
    if len(parts) != 2 or not all(parts):
        raise ParseError(f"Unit reference '{reference}' must be 'Class.Method'.")
    class_name, method_name = parts

    class_files = _find_class_files(tests_root, class_name)
    if not class_files:
        raise ParseError(f"Test class '{class_name}' not found under {tests_root}.")

    bodies: list[tuple[Path, int, str]] = []
    for cs_file in class_files:
        for idx, body in _method_occurrences(cs_file, method_name):
            bodies.append((cs_file, idx, body))

    if not bodies:
        raise ParseError(f"Test method '{reference}' not found (class file: {class_files[0].name}).")
    if len(bodies) > 1:
        raise ParseError(
            f"Test method '{reference}' is ambiguous: {len(bodies)} occurrences found "
            f"({', '.join(str(p) for p, _i, _b in bodies)}). Rename the tests."
        )

    cs_file, idx, body = bodies[0]
    return {
        "source": f"{cs_file.relative_to(tests_root.parents[0])}",
        "line": idx + 1,
        "body": body,
        "sha256": sha256_text(body),
    }


def resolve_e2e_reference(tests_root: Path, reference: str) -> dict:
    """Resolve `script.sh Scenario` to its scenario block."""
    parts = reference.split(None, 1)
    if len(parts) != 2:
        raise ParseError(f"E2E reference '{reference}' must be 'script.sh Scenario'.")
    script, scenario = parts
    script_path = tests_root / script
    if not script_path.is_file():
        script_path = tests_root.parent / script
    if not script_path.is_file():
        raise ParseError(f"E2E script '{script}' not found.")

    lines = _read(script_path).splitlines()
    start = None
    for idx, line in enumerate(lines):
        if scenario in line and ("scenario:" in line or "Test Case" in line or "print_info" in line or "INFO" in line):
            start = idx
            break
    if start is None:
        raise ParseError(f"Scenario '{scenario}' not found in {script_path.name}.")

    block = [lines[start]]
    for line in lines[start + 1:]:
        if "scenario:" in line or ("Test Case" in line and "print_info" in line):
            break
        block.append(line)
    text = "\n".join(block)
    return {
        "source": f"tests/{script}",
        "line": start + 1,
        "body": text,
        "sha256": sha256_text(text),
    }


def resolve_reference(tests_root: Path, row: dict) -> dict | None:
    if row["coverage"] == "exemption":
        return None
    if row["coverage"] == "unit":
        return resolve_unit_reference(tests_root, row["reference"])
    return resolve_e2e_reference(tests_root, row["reference"])
