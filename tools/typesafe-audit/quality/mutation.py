#!/usr/bin/env python3
"""Deterministic Stryker mutation-report parsing (#959 Phase 2).

Keeps compile errors, timeouts, no-coverage mutants, and true survivors as
separate deterministic categories (issue #959). TypeSafe only prioritizes the
surviving-mutant list; it never decides survivor status.
"""

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from coverage_gaps import _REQ_ID, _GENERATED_MARKERS, method_for_line, traceability_rows

_CATEGORY = {
    "Survived": "survived",
    "CompileError": "compile_error",
    "Timeout": "timeout",
    "NoCoverage": "no_coverage",
}


def parse_report(path: Path, repo_root: Path) -> dict[str, list[dict]]:
    """Mutants grouped by deterministic category, generated code excluded."""
    source_root = repo_root / "src"
    rows = traceability_rows(repo_root)
    with open(path, "r", encoding="utf-8") as fh:
        report = json.load(fh)

    categories: dict[str, list[dict]] = {}
    for filename, data in report.get("files", {}).items():
        filename = filename.replace("\\", "/")
        if any(marker in filename for marker in _GENERATED_MARKERS):
            continue
        for mutant in data.get("mutants", []):
            category = _CATEGORY.get(mutant.get("status", ""))
            if category is None:
                continue  # Killed and Ignored mutants are not candidates.
            line = mutant.get("location", {}).get("start", {}).get("line", 0)
            method = method_for_line(source_root, filename, line)
            reqs: set[str] = set()
            source = source_root / filename
            if method and source.exists():
                text = "\n".join(source.read_text(encoding="utf-8", errors="replace").splitlines()[max(0, line - 40): line])
                reqs.update(_REQ_ID.findall(text))
            for row in rows:
                if Path(filename).stem and row["reference"].split(".")[0].startswith(Path(filename).stem):
                    reqs.add(row["req_id"])
            entry = {
                "origin": "mutation",
                "category": category,
                "file": f"src/{filename}" if not filename.startswith("src/") else filename,
                "method": method,
                "line": line,
                "mutator": mutant.get("mutatorName", ""),
                "description": mutant.get("description", ""),
                "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest() if source.exists() else "",
                "req_ids": sorted(reqs),
            }
            categories.setdefault(category, []).append(entry)
    return categories
