#!/usr/bin/env python3
"""Deterministic coverage-gap extraction from Cobertura XML (#959 Phase 1).

Parses coverlet's Cobertura output, maps uncovered lines/branches to enclosing
methods, drops generated code and trivial accessors, and attaches REQ IDs +
owning test names as evidence. TypeSafe never computes any of this.
"""

from __future__ import annotations

import hashlib
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

_REQ_ID = re.compile(r"\bREQ-\d{3,4}\b")
_TRIVIAL_METHODS = re.compile(r"^(get_|set_)[A-Z]")
_GENERATED_MARKERS = ("obj/", "bin/", "generated/", ".Designer.cs", ".AssemblyInfo.cs")


def parse_cobertura(path: Path) -> list[dict]:
    """Class-level method/line data from a Cobertura XML document."""
    root = ET.parse(path).getroot()
    classes = []
    for cls in root.iter("class"):
        filename = cls.get("filename")
        if not filename:
            continue
        methods = []
        for method in cls.iter("method"):
            lines = []
            for line in method.iter("line"):
                lines.append({
                    "number": int(line.get("number")),
                    "hits": int(line.get("hits", 0)),
                    "branch": line.get("branch") == "True",
                    "condition": line.get("condition-coverage", ""),
                })
            methods.append({"name": method.get("name", ""), "lines": lines})
        classes.append({"filename": filename, "methods": methods})
    return classes


def method_for_line(source_root: Path, filename: str, line_number: int) -> str:
    """Best-effort enclosing method name by scanning the C# source text."""
    source = source_root / filename
    if not source.exists():
        return ""
    depth = 0
    name = ""
    try:
        text = source.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""
    for index, line in enumerate(text.splitlines(), start=1):
        if index > line_number:
            break
        match = re.search(r"(?:public|private|internal|protected)[^({;=]*\b([A-Z]\w*)\s*\(", line)
        # Method declarations sit inside a type body (depth >= 1); the same
        # regex cannot match at depth 0 (namespace/file scope).
        if match and depth >= 1:
            name = match.group(1)
        depth += line.count("{") - line.count("}")
    return name


def traceability_rows(repo_root: Path) -> list[dict]:
    path = repo_root / "tests" / "req-traceability.tsv"
    rows = []
    if not path.exists():
        return rows
    for line in path.read_text(encoding="utf-8").splitlines():
        if not line or line.startswith("req_id") or "\t" not in line:
            continue
        cells = line.split("\t")
        if len(cells) >= 4:
            rows.append({"req_id": cells[0].strip(), "reference": cells[2].strip(), "coverage": cells[1].strip()})
    return rows


def attach_req_evidence(gap: dict, source_root: Path, rows: list[dict]) -> None:
    """REQ IDs from the method source text + owning test names from the TSV."""
    source = source_root / gap["file"]
    reqs: set[str] = set()
    if source.exists():
        text = "\n".join(source.read_text(encoding="utf-8", errors="replace").splitlines()[gap["first_line"] - 1: gap["last_line"]])
        reqs.update(_REQ_ID.findall(text))
    class_name = Path(gap["file"]).stem
    tests = []
    for row in rows:
        if row["reference"].split(".")[0].startswith(class_name):
            reqs.add(row["req_id"])
            tests.append(row["reference"])
    gap["req_ids"] = sorted(reqs)
    gap["tests"] = sorted(set(tests))


def extract_gaps(cobertura_path: Path, repo_root: Path, max_gaps: int | None = None) -> list[dict]:
    """Uncovered line/branch gaps per non-excluded method."""
    source_root = repo_root / "src"
    rows = traceability_rows(repo_root)
    gaps: list[dict] = []
    for cls in parse_cobertura(cobertura_path):
        filename = cls["filename"].replace("\\", "/")
        if any(marker in filename for marker in _GENERATED_MARKERS):
            continue
        for method in cls["methods"]:
            name = method["name"]
            if _TRIVIAL_METHODS.match(name):
                continue
            uncovered = [line for line in method["lines"] if line["hits"] == 0]
            uncovered_branches = sum(1 for line in uncovered if line["branch"] and "50%" in line["condition"])
            if not uncovered:
                continue
            resolved = method_for_line(source_root, filename, uncovered[0]["number"])
            gap = {
                "origin": "coverage",
                "file": f"src/{filename}" if not filename.startswith("src/") else filename,
                "method": resolved or name,
                "first_line": uncovered[0]["number"],
                "last_line": uncovered[-1]["number"],
                "uncovered_lines": len(uncovered),
                "uncovered_branches": uncovered_branches,
            }
            attach_req_evidence(gap, source_root, rows)
            gap["source_sha256"] = hashlib.sha256((source_root / filename).read_bytes()).hexdigest() if (source_root / filename).exists() else ""
            gaps.append(gap)
    ordered = sorted(gaps, key=lambda g: (g["file"], g["first_line"]))
    return ordered[:max_gaps] if max_gaps is not None else ordered


def load_json(path: Path) -> dict:
    with open(path, "r", encoding="utf-8") as fh:
        return json.load(fh)
