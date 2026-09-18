#!/usr/bin/env python3
"""Records deterministic fixture responses for the traceability corpus (#957)."""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path

CHECK_DIR = Path(__file__).resolve().parent
TOOL_DIR = CHECK_DIR.parents[1]
REPO_ROOT = TOOL_DIR.parents[1]
REQUIREMENTS_CHECK_DIR = CHECK_DIR.parent / "requirements"
POLICY_PATH = CHECK_DIR / "policy.json"
QUESTIONS_PATH = CHECK_DIR / "questions.json"
sys.path.insert(0, str(TOOL_DIR))


def _load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


runner = _load_module("tsa_runner", TOOL_DIR / "runner.py")
requirement_prechecks = _load_module("tsa_req_prechecks", REQUIREMENTS_CHECK_DIR / "prechecks.py")
own = _load_module("tsa_trace_parse", CHECK_DIR / "parse.py")
own_run_check = _load_module("tsa_trace_run_check", CHECK_DIR / "run_check.py")
req_run_check = _load_module("tsa_req_run_check", REQUIREMENTS_CHECK_DIR / "run_check.py")

parse_tsv = own.parse_tsv
resolve_reference = own.resolve_reference
build_evidence = own_run_check.build_evidence
load_json = req_run_check.load_json
SPAN_SEPARATOR = own_run_check.SPAN_SEPARATOR

CORPUS_DIR = REPO_ROOT / "tests/typesafe-audit-fixtures/traceability"


def main() -> int:
    if len(sys.argv) != 6:
        print(f"usage: {Path(__file__).name} <fixture-dir> <tsv> <tests-root> <requirements-root> <questions.json>", file=sys.stderr)
        return 2

    fixture_dir = Path(sys.argv[1])
    tsv = Path(sys.argv[2])
    tests_root = Path(sys.argv[3])
    requirements_root = Path(sys.argv[4])
    questions = load_json(Path(sys.argv[5]))["questions"]

    policy = load_json(POLICY_PATH)
    config = runner.load_config(runner.DEFAULT_CONFIG)
    expected = {e["file"]: e["expected"] for e in load_json(CORPUS_DIR / "expected_labels.json")["examples"]}
    requirements = requirement_prechecks.load_requirements(requirements_root)

    namespaced_questions = {}
    namespaced_answers = {}
    key_meta = {}
    for row in parse_tsv(tsv):
        resolved = resolve_reference(tests_root, row)
        evidence = build_evidence(row, resolved, requirements, policy["max_evidence_bytes"])
        if resolved is None:
            continue  # exemption rows are outside the audited scope
        key = f"{row['req_id']}{SPAN_SEPARATOR}{__import__('hashlib').sha256(evidence['mapping']['source'].encode()).hexdigest()[:12]}"
        expected_answers = expected[row["req_id"]]
        for qid, question in questions.items():
            value = expected_answers[qid]
            namespaced_questions[f"{key}{SPAN_SEPARATOR}{qid}"] = question
            namespaced_answers[f"{key}{SPAN_SEPARATOR}{qid}"] = (
                {"type": "noul", "noul": 0.99 if value else 0.01}
                if question["type"] == "noul"
                else {"type": "choice", "choice": value, "probabilities": {value: 1.0}, "confidence": 0.95}
            )
        key_meta[key] = (row, evidence)

    request = {
        "state": {"evidence": [e for _r, e in key_meta.values()]},
        "model": config["model"],
        "questions": namespaced_questions,
    }
    response = {
        "model": config["model"],
        "answers": namespaced_answers,
        "usage": {"input_tokens": 300, "output_tokens": 30},
    }
    path = runner.record_fixture(request, fixture_dir, response)
    print(f"recorded traceability corpus fixture {path.name[:16]}…")
    return 0


if __name__ == "__main__":
    sys.exit(main())
