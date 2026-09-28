#!/usr/bin/env python3
"""Record offline fixture responses for the architecture lint corpus.

Rebuilds the exact request run_check.py sends for every case in
tests/typesafe-audit-fixtures/architecture/cases, then writes the expected answer
payloads under the runner's request-hash fixture contract so corpus evaluation
runs offline and deterministically.
"""

from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[2]
CHECK_DIR = TOOL_DIR / "checks" / "architecture"
CORPUS_DIR = TOOL_DIR.parents[1] / "tests" / "typesafe-audit-fixtures" / "architecture"
sys.path.insert(0, str(TOOL_DIR))


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


runner = load_module("arch_recorder_runner", TOOL_DIR / "runner.py")
run_check = load_module("arch_recorder_run_check", CHECK_DIR / "run_check.py")

ANSWERS = {
    "clean_seam": {
        "seam_bypass": {"noul": 0.01, "confidence": 0.95, "probabilities": {"0.01": 1.0}},
        "diagram_stale": {"noul": 0.01, "confidence": 0.95, "probabilities": {"0.01": 1.0}},
    },
    "seam_bypass": {
        "seam_bypass": {"noul": 0.95, "confidence": 0.95, "probabilities": {"0.95": 1.0}},
        "diagram_stale": {"noul": 0.01, "confidence": 0.95, "probabilities": {"0.01": 1.0}},
    },
    "diagram_stale": {
        "seam_bypass": {"noul": 0.02, "confidence": 0.95, "probabilities": {"0.02": 1.0}},
        "diagram_stale": {"noul": 0.95, "confidence": 0.95, "probabilities": {"0.95": 1.0}},
    },
}


def main() -> None:
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    fixture_dir = Path(sys.argv[1])
    fixture_dir.mkdir(parents=True, exist_ok=True)

    questions = run_check.checks_common.load_json(run_check.QUESTIONS_PATH, label="arch-recorder")["questions"]
    config = runner.load_config(runner.DEFAULT_CONFIG)

    for case_path in sorted((CORPUS_DIR / "cases").glob("*.json")):
        case = json.loads(case_path.read_text(encoding="utf-8"))
        case_name = case.get("case", case_path.stem)
        changed = case.get("changed_files", [])
        watched = run_check.watched_files(changed)
        if not watched or run_check.carve_out_without_doc_update(changed):
            continue

        evidence = {
            "changed_files": watched,
            "diff_hunks": case.get("diff_hunks", []),
            "architecture_doc": case.get("architecture_doc", ""),
        }
        evidence_key = run_check.evidence_key_for(watched)
        request = {
            "state": evidence,
            "model": config["model"],
            "questions": run_check.build_questions(questions, evidence_key),
        }
        expected = ANSWERS.get(case_name)
        if not expected:
            continue
        answers = {
            f"{evidence_key}#{qid}": ans
            for qid, ans in expected.items()
        }
        path = runner.record_fixture(request, fixture_dir, {"model": config["model"], "answers": answers})
        print(f"recorded {path.name} for architecture case {case_name}")


if __name__ == "__main__":
    main()
