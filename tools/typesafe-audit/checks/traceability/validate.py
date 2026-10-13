#!/usr/bin/env python3
"""Deterministic requirement traceability validator (#1113).

Validates requirement-to-test mappings and reference integrity offline:
every active REQ-NNN and REQ_E-NNN contract must be mapped or reasoned-exempt,
every test reference must resolve uniquely to an existing executable test case,
and no orphaned, ambiguous, or malformed entries exist.
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

CHECK_DIR = Path(__file__).resolve().parent
if str(CHECK_DIR) not in sys.path:
    sys.path.insert(0, str(CHECK_DIR))

try:
    from .parse import (
        ParseError,
        SourceIndex,
        TSV_HEADER,
        VALID_COVERAGES,
        resolve_e2e_reference,
        resolve_unit_reference,
    )
except ImportError:
    from parse import (
        ParseError,
        SourceIndex,
        TSV_HEADER,
        VALID_COVERAGES,
        resolve_e2e_reference,
        resolve_unit_reference,
    )

UNIT_TESTS_REL_DIR = "src/Zipper.Tests"


def _load_active_and_orphan_requirements(requirements_path: Path) -> tuple[list[str], list[str]]:
    req_text = requirements_path.read_text(encoding="utf-8", errors="replace")
    defined_ids_list = re.findall(r"^\s*-\s+\*\*(REQ(?:_E)?-\d+)\*\*", req_text, re.MULTILINE)
    active_reqs = sorted(set(defined_ids_list))
    if not active_reqs:
        return [], []
    clean_lines = [line for line in req_text.splitlines() if not line.strip().startswith("<!--")]
    all_mentions = set(re.findall(r"\b(REQ(?:_E)?-\d+)\b", "\n".join(clean_lines)))
    defined_set = set(active_reqs)
    orphan_refs = sorted(all_mentions - defined_set)
    return active_reqs, orphan_refs


def _validate_manifest_row(
    line_no: int,
    cols: list[str],
) -> tuple[dict | None, dict | None, dict | None]:
    if len(cols) != 4:
        return None, {"line_no": line_no, "error": f"expected 4 tab-separated columns, got {len(cols)}"}, None
    req_id = cols[0].strip()
    coverage = cols[1].strip().lower()
    reference = cols[2].strip()
    notes = cols[3].strip()

    if not re.fullmatch(r"REQ(?:_E)?-\d+", req_id):
        return None, {"line_no": line_no, "error": f"manifest entry has invalid REQ format: '{req_id}'"}, None
    if coverage not in VALID_COVERAGES:
        return None, {"line_no": line_no, "error": f"{req_id} has invalid coverage type '{coverage}'"}, None
    if coverage == "exemption":
        if reference not in ("-", ""):
            return None, {"line_no": line_no, "error": f"{req_id} exemptions must use '-' as the reference"}, None
        if not notes:
            return None, None, {"line_no": line_no, "req_id": req_id, "error": f"{req_id} exemption requires a rationale in notes"}
    elif reference in ("-", ""):
        return None, {"line_no": line_no, "error": f"{req_id} requires a test reference (coverage={coverage})"}, None

    return {"req_id": req_id, "coverage": coverage, "reference": reference, "notes": notes, "line_no": line_no}, None, None


def _parse_manifest(
    tsv_path: Path,
    defined_set: set[str],
) -> tuple[list[dict], list[dict], list[dict], dict[str, list[dict]], list[dict], set[str]] | None:
    tsv_lines = tsv_path.read_text(encoding="utf-8", errors="replace").splitlines()
    if not tsv_lines:
        print(f"Error: {tsv_path} is empty.", file=sys.stderr)
        return None
    header = tuple(col.strip().lower() for col in tsv_lines[0].split("\t"))
    if header != TSV_HEADER:
        print(f"Error: {tsv_path} header must be {TSV_HEADER}, got {header}.", file=sys.stderr)
        return None

    rows: list[dict] = []
    malformed_rows: list[dict] = []
    unexplained_exemptions: list[dict] = []
    manifest_entries: dict[str, list[dict]] = {}
    unknown_refs: list[dict] = []
    exemptions_set: set[str] = set()

    for line_no, line in enumerate(tsv_lines[1:], start=2):
        if not line.strip():
            continue
        row_dict, malformed, unexplained = _validate_manifest_row(line_no, line.split("\t"))
        if malformed:
            malformed_rows.append(malformed)
            continue
        if unexplained:
            unexplained_exemptions.append(unexplained)
        if not row_dict:
            continue
        req_id = row_dict["req_id"]
        rows.append(row_dict)
        manifest_entries.setdefault(req_id, []).append(row_dict)
        if row_dict["coverage"] == "exemption":
            exemptions_set.add(req_id)
        if req_id not in defined_set:
            unknown_refs.append({"line_no": line_no, "req_id": req_id})

    return rows, malformed_rows, unexplained_exemptions, manifest_entries, unknown_refs, exemptions_set


def _check_resolutions(rows: list[dict], tests_root: Path, repo_root: Path) -> tuple[list[dict], list[dict]]:
    unit_tests_root = tests_root
    if (tests_root / UNIT_TESTS_REL_DIR).is_dir():
        unit_tests_root = tests_root / UNIT_TESTS_REL_DIR
    elif (repo_root / UNIT_TESTS_REL_DIR).is_dir():
        unit_tests_root = repo_root / UNIT_TESTS_REL_DIR

    source_index = SourceIndex(unit_tests_root)
    missing_refs: list[dict] = []
    ambiguous_refs: list[dict] = []

    for row in rows:
        cov = row["coverage"]
        try:
            if cov == "unit":
                resolve_unit_reference(unit_tests_root, row["reference"], source_index)
            elif cov == "e2e":
                resolve_e2e_reference(repo_root, row["reference"], source_index)
        except ParseError as exc:
            err_str = str(exc)
            entry = {"line_no": row["line_no"], "req_id": row["req_id"], "reference": row["reference"], "error": err_str}
            if "ambiguous" in err_str.lower():
                ambiguous_refs.append(entry)
            else:
                missing_refs.append(entry)

    return missing_refs, ambiguous_refs


def _print_section(header: str, items: list, formatter) -> None:
    if not items:
        return
    print(header)
    for item in items:
        print(f"  {formatter(item)}")
    print("")


def _print_report(report: dict) -> None:
    print("=== Requirement Traceability Report ===")
    print(f"Active requirements:  {report['active_total']}")
    print(f"Mapped to tests:      {report['mapped_count']}")
    print(f"Exempted:             {report['exempted_count']}")
    print(f"Unmapped:             {report['unmapped_count']}")
    print(f"Unknown references:   {report['unknown_count']}")
    print(f"Duplicate entries:    {report['duplicate_count']}")
    print(f"Missing references:   {report['missing_count']}")
    print(f"Ambiguous references: {report['ambiguous_count']}")
    print(f"Orphan references:    {report['orphan_count']}")
    print("")

    _print_section("--- Unknown references (in manifest but not in Requirements.md) ---", report["unknown_refs"], lambda u: f"Line {u['line_no']}: {u['req_id']}")
    _print_section("--- Duplicate entries (same REQ mapped multiple times — informational) ---", report["duplicate_entries"], str)
    _print_section("--- Missing references (mapped test or scenario not found) ---", report["missing_refs"], lambda m: f"Line {m['line_no']} ({m['req_id']}): {m['reference']} — {m['error']}")
    _print_section("--- Ambiguous references (multiple matches found) ---", report["ambiguous_refs"], lambda a: f"Line {a['line_no']} ({a['req_id']}): {a['reference']} — {a['error']}")
    _print_section("--- Unmapped active requirements ---", report["unmapped_reqs"], str)
    _print_section("--- Orphan references (mentioned but never defined) ---", report["orphan_refs"], str)
    _print_section("--- Malformed manifest entries ---", report["malformed_rows"], lambda mf: f"Line {mf['line_no']}: {mf['error']}")
    _print_section("--- Unexplained exemptions ---", report["unexplained_exemptions"], lambda ue: f"Line {ue['line_no']} ({ue['req_id']}): {ue['error']}")

    cov = report["coverage_pct"]
    mapped = report["mapped_count"]
    exempt = report["exempted_count"]
    total = report["active_total"]
    print(f"Coverage: {cov}% ({mapped} mapped + {exempt} exempted = {mapped + exempt}/{total})")


def _print_strict_failures(
    unmapped_reqs: list[str],
    orphan_refs: list[str],
    unknown_refs: list[dict],
    missing_refs: list[dict],
    ambiguous_refs: list[dict],
    malformed_rows: list[dict],
    unexplained_exemptions: list[dict],
) -> None:
    print("", file=sys.stderr)
    if unmapped_reqs:
        print(f"FAIL: {len(unmapped_reqs)} unmapped active requirements remain.", file=sys.stderr)
    if orphan_refs:
        print(f"FAIL: {len(orphan_refs)} orphaned requirement references found.", file=sys.stderr)
    if unknown_refs:
        print(f"FAIL: {len(unknown_refs)} unknown manifest requirement references found.", file=sys.stderr)
    if missing_refs:
        print(f"FAIL: {len(missing_refs)} missing test references found.", file=sys.stderr)
    if ambiguous_refs:
        print(f"FAIL: {len(ambiguous_refs)} ambiguous test references found.", file=sys.stderr)
    if malformed_rows:
        print(f"FAIL: {len(malformed_rows)} malformed manifest entries found.", file=sys.stderr)
    if unexplained_exemptions:
        print(f"FAIL: {len(unexplained_exemptions)} unexplained exemptions found.", file=sys.stderr)


def validate_traceability(
    tsv_path: Path,
    requirements_path: Path,
    tests_root: Path,
    repo_root: Path,
    strict: bool = False,
) -> tuple[int, dict]:
    """Validate requirement traceability deterministically offline."""
    if not requirements_path.is_file():
        print(f"Error: Requirements.md not found at {requirements_path}", file=sys.stderr)
        return 2, {"error": "requirements file not found"}
    if not tsv_path.is_file():
        print(f"Error: req-traceability.tsv not found at {tsv_path}", file=sys.stderr)
        return 2, {"error": "manifest file not found"}

    active_reqs, orphan_refs = _load_active_and_orphan_requirements(requirements_path)
    if not active_reqs:
        print(f"Error: no REQ-NNN or REQ_E-NNN IDs found in {requirements_path}", file=sys.stderr)
        return 2, {"error": "no active requirements"}

    parsed = _parse_manifest(tsv_path, set(active_reqs))
    if parsed is None:
        return 2, {"error": "invalid manifest"}
    rows, malformed_rows, unexplained_exemptions, manifest_entries, unknown_refs, exemptions_set = parsed

    duplicate_entries = sorted([req for req, entries in manifest_entries.items() if len(entries) > 1])
    missing_refs, ambiguous_refs = _check_resolutions(rows, tests_root, repo_root)

    unmapped_reqs = [req for req in active_reqs if req not in manifest_entries]
    exempted_count = sum(1 for req in active_reqs if req in manifest_entries and req in exemptions_set)
    mapped_count = sum(1 for req in active_reqs if req in manifest_entries and req not in exemptions_set)

    total_active = len(active_reqs)
    coverage_pct = ((mapped_count + exempted_count) * 100 // total_active) if total_active > 0 else 0

    report = {
        "active_total": total_active,
        "mapped_count": mapped_count,
        "exempted_count": exempted_count,
        "unmapped_count": len(unmapped_reqs),
        "unknown_count": len(unknown_refs),
        "duplicate_count": len(duplicate_entries),
        "missing_count": len(missing_refs),
        "ambiguous_count": len(ambiguous_refs),
        "orphan_count": len(orphan_refs),
        "coverage_pct": coverage_pct,
        "unmapped_reqs": unmapped_reqs,
        "orphan_refs": orphan_refs,
        "unknown_refs": unknown_refs,
        "missing_refs": missing_refs,
        "ambiguous_refs": ambiguous_refs,
        "duplicate_entries": duplicate_entries,
        "malformed_rows": malformed_rows,
        "unexplained_exemptions": unexplained_exemptions,
    }

    _print_report(report)

    has_strict_failures = (
        bool(unmapped_reqs)
        or bool(orphan_refs)
        or bool(unknown_refs)
        or bool(missing_refs)
        or bool(ambiguous_refs)
        or bool(malformed_rows)
        or bool(unexplained_exemptions)
    )

    if strict and has_strict_failures:
        _print_strict_failures(unmapped_reqs, orphan_refs, unknown_refs, missing_refs, ambiguous_refs, malformed_rows, unexplained_exemptions)
        return 1, report

    return 0, report


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Deterministic requirement traceability validator.")
    parser.add_argument("--strict", action="store_true", help="Fail on any traceability or reference integrity defect.")
    parser.add_argument("--tsv", type=Path, default=None, help="Path to req-traceability.tsv.")
    parser.add_argument("--requirements", type=Path, default=None, help="Path to Requirements.md.")
    parser.add_argument("--tests-root", type=Path, default=None, help="Root directory for test sources.")
    parser.add_argument("--repo-root", type=Path, default=None, help="Root directory of repository.")
    args = parser.parse_args(argv)

    default_repo = Path(__file__).resolve().parents[4]
    repo_root = (args.repo_root or default_repo).resolve()
    tsv_path = (args.tsv or (repo_root / "tests/req-traceability.tsv")).resolve()
    requirements_path = (args.requirements or (repo_root / "Requirements.md")).resolve()
    tests_root = (args.tests_root or repo_root).resolve()

    code, _ = validate_traceability(
        tsv_path=tsv_path,
        requirements_path=requirements_path,
        tests_root=tests_root,
        repo_root=repo_root,
        strict=args.strict,
    )
    return code


if __name__ == "__main__":
    sys.exit(main())
