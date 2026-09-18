#!/usr/bin/env python3
"""Semantic requirement-to-test traceability audit (#957).

Runs AFTER `tests/validate-req-traceability.sh --strict` — it complements row
presence with a judgment of whether the mapped test actually verifies the
requirement (full / partial / none / requirement-ambiguous), plus separate
signals for mocked-only and assertion-free execution tests.

Deterministic preparation fails before any model call: malformed TSV rows,
unresolved or ambiguous test references, oversized evidence. Every result
retains raw probabilities, confidence, model, and stable source hashes.

Exit codes: 0 ok/advisory (this check never gates), 2 config/input error,
3 remote service failure.
"""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[2]
CHECK_DIR = Path(__file__).resolve().parent
REQUIREMENTS_CHECK_DIR = CHECK_DIR.parent / "requirements"
REPO_ROOT = TOOL_DIR.parents[1]
sys.path.insert(0, str(TOOL_DIR))


def _load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


runner = _load_module("tsa_runner", TOOL_DIR / "runner.py")
requirement_prechecks = _load_module("tsa_req_prechecks", REQUIREMENTS_CHECK_DIR / "prechecks.py")
_extract_sections_mod = _load_module("tsa_extract_sections", REQUIREMENTS_CHECK_DIR / "extract_sections.py")

ParseError = type("ParseError", (Exception,), {})
extract_sections = _extract_sections_mod.extract_sections
group_by_requirement = _extract_sections_mod.group_by_requirement
_parse_mod = _load_module("tsa_trace_parse_internal", CHECK_DIR / "parse.py")
ParseError = _parse_mod.ParseError
parse_tsv = _parse_mod.parse_tsv
resolve_reference = _parse_mod.resolve_reference
sha256_text = _parse_mod.sha256_text

EXIT_OK = runner.EXIT_OK
EXIT_INPUT_ERROR = runner.EXIT_INPUT_ERROR
EXIT_REMOTE_ERROR = runner.EXIT_REMOTE_ERROR

QUESTIONS_PATH = CHECK_DIR / "questions.json"
POLICY_PATH = CHECK_DIR / "policy.json"
DEFAULT_TSV = REPO_ROOT / "tests/req-traceability.tsv"
DEFAULT_TESTS_ROOT = REPO_ROOT
SPAN_SEPARATOR = "#"


def load_json(path: Path) -> dict:
    try:
        with open(path, "r", encoding="utf-8") as fh:
            return json.load(fh)
    except (OSError, json.JSONDecodeError) as exc:
        print(f"traceability-audit: input error: cannot read {path}: {exc}", file=sys.stderr)
        sys.exit(EXIT_INPUT_ERROR)


def build_evidence(row: dict, resolved: dict | None, requirements: dict[str, tuple[int, str]], max_evidence_bytes: int) -> dict:
    """Requirement text + traceability notes + resolved test source + hashes."""
    req_line_no, req_text = requirements.get(row["req_id"], (0, ""))
    parts = {
        "requirement": {"req_id": row["req_id"], "source": f"Requirements.md:{req_line_no}", "text": req_text, "sha256": sha256_text(req_text)},
        "mapping": {"coverage": row["coverage"], "reference": row["reference"], "notes": row["notes"], "source": f"tests/req-traceability.tsv:{row['line_no']}", "sha256": sha256_text(f"{row['coverage']}:{row['reference']}:{row['notes']}")},
    }
    if resolved is not None:
        parts["test"] = {"source": resolved["source"], "line": resolved["line"], "body": resolved["body"], "sha256": resolved["sha256"]}
    else:
        parts["exemption"] = {"rationale": row["notes"], "sha256": sha256_text(row["notes"])}
    encoded = json.dumps(parts, sort_keys=True).encode("utf-8")
    if len(encoded) > max_evidence_bytes:
        raise ParseError(
            f"Evidence for {row['req_id']} is {len(encoded)} bytes (limit {max_evidence_bytes}); narrow the test body."
        )
    return parts


def changed_req_ids(repo_root: Path, base: str, head: str, tsv_rows: list[dict]) -> set[str]:
    """PR scope: changed requirements, changed mappings, changed referenced tests."""
    changed: set[str] = set()
    try:
        sections = extract_sections(repo_root, base, head)
        for section in sections:
            changed.update(section["req_ids"])
        diff_files = os.popen(  # noqa: S605 — repo-local, pinned inputs
            f"git -C {repo_root} diff --name-only {base}...{head}"
        ).read().splitlines()
    except RuntimeError as exc:
        print(f"traceability-audit: input error: extraction failed: {exc}", file=sys.stderr)
        sys.exit(EXIT_INPUT_ERROR)

    tsv_path = "tests/req-traceability.tsv"
    if tsv_path in diff_files:
        changed.update(row["req_id"] for row in tsv_rows)
    test_file_changes = {f for f in diff_files if f.endswith(".cs") or f.startswith("tests/")}
    for row in tsv_rows:
        if row["coverage"] == "unit" and any(str(row["req_id"]) and f.endswith(f"{row['reference'].split('.')[0]}.cs") for f in test_file_changes):
            changed.add(row["req_id"])
        elif row["coverage"] == "e2e" and any(f.endswith(row["reference"].split(None, 1)[0]) for f in test_file_changes):
            changed.add(row["req_id"])
    return changed


def status_for(row: dict, policy: dict) -> str:
    confidence = row.get("confidence")
    low, high = policy["noul_finding_range"]
    if row["question"] == "coverage":
        if confidence is None or confidence < policy["confidence_threshold"]:
            return "needs-human-review"
        return "finding" if row["answer"] in ("none", "partial", "ambiguous") else "ok"
    # noul signals: mocked-only / execution-only true = finding.
    if row["answer"] is None or not isinstance(row["answer"], (int, float)):
        return "needs-human-review"
    if low < row["answer"] < high:
        return "needs-human-review"
    return "finding" if row["answer"] >= high else "ok"


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Semantic traceability audit (advisory).")
    parser.add_argument("--base", help="Base SHA (PR mode: changed requirements/mappings/tests).")
    parser.add_argument("--head", default="HEAD")
    parser.add_argument("--full", action="store_true", help="Audit every active non-exempt requirement in bounded batches.")
    parser.add_argument("--tsv", type=Path, default=DEFAULT_TSV)
    parser.add_argument("--tests-root", type=Path, default=DEFAULT_TESTS_ROOT, help="Root for resolving test sources (corpus tests point this at fixture trees).")
    parser.add_argument("--requirements-root", type=Path, default=Path.cwd(), help="Root containing Requirements.md (corpus tests point this at fixture trees).")
    parser.add_argument("--mode", choices=("live", "fixture"), default="fixture")
    parser.add_argument("--fixture-dir", type=Path, default=CHECK_DIR / "fixtures")
    parser.add_argument("--json-out", type=Path, required=True)
    parser.add_argument("--md-out", type=Path, required=True)
    parser.add_argument("--summary-out", type=Path, help="Append the Markdown report (CI job summary).")
    args = parser.parse_args(argv)

    policy = load_json(POLICY_PATH)
    questions = load_json(QUESTIONS_PATH)["questions"]
    config = runner.load_config(runner.DEFAULT_CONFIG)
    api_key = ""
    secrets: list[str] = []
    if args.mode == "live":
        api_key = os.environ.get("TYPESAFE_API_KEY", "")
        secrets = [api_key] if api_key else []

    # --- Deterministic preparation: fail before any model call. ---
    try:
        tsv_rows = parse_tsv(args.tsv)
    except ParseError as exc:
        print(f"traceability-audit: input error: {exc}", file=sys.stderr)
        return EXIT_INPUT_ERROR

    requirements = requirement_prechecks.load_requirements(args.requirements_root)
    evidence_by_req: dict[str, list[dict]] = {}
    resolved_by_req: dict[str, list[dict | None]] = {}
    try:
        for row in tsv_rows:
            resolved = resolve_reference(args.tests_root, row)
            evidence = build_evidence(row, resolved, requirements, policy["max_evidence_bytes"])
            evidence_by_req.setdefault(row["req_id"], []).append(evidence)
            resolved_by_req.setdefault(row["req_id"], []).append(resolved)
    except ParseError as exc:
        print(f"traceability-audit: input error: {exc}", file=sys.stderr)
        return EXIT_INPUT_ERROR

    if args.full:
        scope = [req_id for req_id in sorted(evidence_by_req) if any(
            ev.get("exemption") is None and "test" in ev for ev in evidence_by_req[req_id]
        )]
        batch_size = policy["batch_size"]
    else:
        if not args.base:
            print("traceability-audit: input error: --base is required without --full.", file=sys.stderr)
            return EXIT_INPUT_ERROR
        scope = sorted(changed_req_ids(Path.cwd(), args.base, args.head, tsv_rows) & set(evidence_by_req))
        batch_size = len(scope) or 1

    max_requests = policy["max_requests"]
    if len(scope) > batch_size * max_requests:
        print(
            f"traceability-audit: input error: scope of {len(scope)} requirements exceeds the "
            f"{max_requests}-request budget at batch size {batch_size}; raise max_requests or narrow scope.",
            file=sys.stderr,
        )
        return EXIT_INPUT_ERROR

    # --- TypeSafe judgments (batched, namespaced answer keys). ---
    results: list[dict] = []
    for batch_start in range(0, len(scope), batch_size):
        batch = scope[batch_start:batch_start + batch_size]
        namespaced = {}
        req_by_key: dict[str, tuple[str, dict]] = {}
        for req_id in batch:
            for evidence in evidence_by_req[req_id]:
                key = f"{req_id}{SPAN_SEPARATOR}{sha256_text(evidence['mapping']['source'])[:12]}"
                for qid, question in questions.items():
                    namespaced[f"{key}{SPAN_SEPARATOR}{qid}"] = question
                req_by_key[key] = (req_id, evidence)

        if not namespaced:
            continue
        request = {"state": {"evidence": list(e for _k, (_r, e) in req_by_key.items())}, "model": config["model"], "questions": namespaced}

        if args.mode == "fixture":
            raw = dict(runner.run_fixture(request, Path(args.fixture_dir)))
        else:
            if not api_key:
                print("traceability-audit: input error: TYPESAFE_API_KEY must be set for live mode.", file=sys.stderr)
                return EXIT_INPUT_ERROR
            raw = dict(runner.run_live(request, config, api_key, secrets))

        for key, (req_id, evidence) in req_by_key.items():
            for qid in questions:
                answer = raw.get("answers", {}).get(f"{key}{SPAN_SEPARATOR}{qid}")
                if answer is None:
                    print(f"traceability-audit: input error: response missing answer '{key}{SPAN_SEPARATOR}{qid}'.", file=sys.stderr)
                    return EXIT_INPUT_ERROR
                row = {
                    "question": qid,
                    "type": questions[qid]["type"],
                    "answer": (
                        answer.get("choice") if questions[qid]["type"] == "choice"
                        else answer.get("noul") if questions[qid]["type"] == "noul"
                        else answer.get("score")
                    ),
                    "probabilities": answer.get("probabilities"),
                    "confidence": answer.get("confidence"),
                }
                test_source = evidence.get("test", {}).get("source") or evidence.get("exemption", {}).get("rationale", "exempt")
                results.append({
                    "req_id": req_id,
                    "test_source": test_source,
                    "evidence_hashes": {
                        "requirement": evidence["requirement"]["sha256"],
                        "mapping": evidence["mapping"]["sha256"],
                        "test": evidence.get("test", {}).get("sha256") or evidence.get("exemption", {}).get("sha256"),
                    },
                    "model": raw.get("model", config["model"]),
                    "usage": raw.get("usage"),
                    **row,
                    "status": status_for(row, policy) if qid == "coverage" or row["type"] == "noul" else "ok",
                })

    report = {
        "runner_version": config["runner_version"],
        "mode": args.mode,
        "full": args.full,
        "results": results,
        "needs_human_review": any(r["status"] == "needs-human-review" for r in results),
    }

    args.json_out.parent.mkdir(parents=True, exist_ok=True)
    args.json_out.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    markdown = render_markdown(report)
    args.md_out.write_text(markdown, encoding="utf-8")
    if args.summary_out:
        with open(args.summary_out, "a", encoding="utf-8") as fh:
            fh.write(markdown)

    print(
        f"traceability-audit: {len(results)} judgment(s), "
        f"{sum(1 for r in results if r['status'] == 'finding')} finding(s) (advisory only), "
        f"{sum(1 for r in results if r['status'] == 'needs-human-review')} needs-human-review."
    )
    return EXIT_OK


def render_markdown(report: dict) -> str:
    lines = ["# Semantic Traceability Audit Report", ""]
    mode = "full audit" if report["full"] else "changed scope"
    lines.append(f"- Mode: {report['mode']} ({mode})")
    lines.append(f"- Runner: v{report['runner_version']}")
    lines.append("")
    lines.append("| REQ | Test source | Question | Answer | Confidence | Status |")
    lines.append("| --- | --- | --- | --- | --- | --- |")
    for result in report["results"]:
        answer = result.get("answer")
        if isinstance(answer, float):
            answer = f"{answer:.4f}"
        confidence = result.get("confidence")
        confidence = "n/a" if confidence is None else f"{confidence:.4f}"
        lines.append(
            f"| {result['req_id']} | `{result['test_source']}` | {result['question']} | {answer} | {confidence} | {result['status']} |"
        )
    lines.append("")
    return "\n".join(lines)


if __name__ == "__main__":
    sys.exit(main())
