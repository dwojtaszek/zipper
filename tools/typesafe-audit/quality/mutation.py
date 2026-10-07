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


def normalize_file_path(raw_filename: str, repo_root: Path) -> tuple[str, str]:
    """Returns (file_entry, rel_to_src).

    file_entry: repo-relative path starting with 'src/', e.g. 'src/LoadFiles/Foo.cs'
    rel_to_src: path relative to src/, e.g. 'LoadFiles/Foo.cs'
    """
    raw_filename = raw_filename.replace("\\", "/")
    p = Path(raw_filename)
    if p.is_absolute():
        try:
            rel = p.resolve().relative_to(repo_root.resolve())
            norm = str(rel).replace("\\", "/")
        except ValueError:
            try:
                rel = p.resolve().relative_to((repo_root / "src").resolve())
                norm = f"src/{str(rel).replace('\\', '/')}"
            except ValueError:
                norm = raw_filename
    else:
        norm = raw_filename

    if norm.startswith("src/"):
        file_entry = norm
        rel_to_src = norm[4:]
    else:
        file_entry = f"src/{norm}"
        rel_to_src = norm

    return file_entry, rel_to_src


def parse_report(path: Path, repo_root: Path) -> dict[str, list[dict]]:
    """Mutants grouped by deterministic category, generated code excluded."""
    source_root = repo_root / "src"
    rows = traceability_rows(repo_root)
    with open(path, "r", encoding="utf-8") as fh:
        report = json.load(fh)

    categories: dict[str, list[dict]] = {}
    for raw_filename, data in report.get("files", {}).items():
        raw_filename = raw_filename.replace("\\", "/")
        if any(marker in raw_filename for marker in _GENERATED_MARKERS):
            continue

        file_entry, rel_to_src = normalize_file_path(raw_filename, repo_root)
        source = source_root / rel_to_src

        for mutant in data.get("mutants", []):
            category = _CATEGORY.get(mutant.get("status", ""))
            if category is None:
                continue  # Killed and Ignored mutants are not candidates.
            line = mutant.get("location", {}).get("start", {}).get("line", 0)
            method = method_for_line(source_root, rel_to_src, line)
            reqs: set[str] = set()
            if method and source.exists():
                text = "\n".join(source.read_text(encoding="utf-8", errors="replace").splitlines()[max(0, line - 40): line])
                reqs.update(_REQ_ID.findall(text))
            for row in rows:
                if Path(rel_to_src).stem and row["reference"].split(".")[0].startswith(Path(rel_to_src).stem):
                    reqs.add(row["req_id"])
            entry = {
                "origin": "mutation",
                "category": category,
                "file": file_entry,
                "method": method,
                "line": line,
                "mutator": mutant.get("mutatorName", ""),
                "description": mutant.get("description", ""),
                "source_sha256": hashlib.sha256(source.read_bytes()).hexdigest() if source.exists() else "",
                "req_ids": sorted(reqs),
            }
            categories.setdefault(category, []).append(entry)
    return categories


def locate_report(path: Path) -> Path:
    """Locate mutation-report.json at path or in a subdirectory beneath path."""
    if path.is_file():
        return path
    if path.is_dir():
        candidate = path / "reports" / "mutation-report.json"
        if candidate.is_file():
            return candidate
        candidate2 = path / "mutation-report.json"
        if candidate2.is_file():
            return candidate2
        matches = sorted(path.glob("**/mutation-report.json"))
        if matches:
            return matches[0]
    raise FileNotFoundError(f"Could not locate mutation-report.json under '{path}'")


def count_selected_mutants(report: dict) -> int:
    """Count mutants that were selected/tested (status != 'Ignored')."""
    total = 0
    for file_data in report.get("files", {}).values():
        for mutant in file_data.get("mutants", []):
            if mutant.get("status") != "Ignored":
                total += 1
    return total


def normalize_report_dict(report: dict, repo_root: Path) -> dict:
    """Normalize file keys in report to relative paths."""
    normalized_report = dict(report)
    files = {}
    for raw_path, data in report.get("files", {}).items():
        _, rel_to_src = normalize_file_path(raw_path, repo_root)
        files[rel_to_src] = data
    normalized_report["files"] = files
    return normalized_report


def process_report(input_path: Path, output_path: Path, repo_root: Path, scope: str = "") -> dict:
    """Locates report, validates positive mutant count, normalizes paths, and writes output."""
    real_input = locate_report(input_path)
    if not real_input.is_file() or real_input.stat().st_size == 0:
        raise FileNotFoundError(f"Mutation report at '{real_input}' is missing or empty")

    with open(real_input, "r", encoding="utf-8") as fh:
        report = json.load(fh)

    selected_count = count_selected_mutants(report)
    if selected_count == 0:
        scope_msg = f" (scope: {scope})" if scope else ""
        raise ValueError(f"Mutation report contains 0 selected mutants{scope_msg}.")

    norm = normalize_report_dict(report, repo_root)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(norm, indent=2) + "\n", encoding="utf-8")
    return norm

