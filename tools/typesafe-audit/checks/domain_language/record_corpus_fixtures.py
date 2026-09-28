#!/usr/bin/env python3
"""Record offline fixture responses for the domain language corpus.

Rebuilds the exact request run_check.py sends for every case in
tests/typesafe-audit-fixtures/domain-language/cases, then writes the expected answer
payloads under the runner's request-hash fixture contract so corpus evaluation
runs offline and deterministically.
"""

from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[2]
CHECK_DIR = TOOL_DIR / "checks" / "domain_language"
CORPUS_DIR = TOOL_DIR.parents[1] / "tests" / "typesafe-audit-fixtures" / "domain-language"
sys.path.insert(0, str(TOOL_DIR))


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


runner = load_module("dl_recorder_runner", TOOL_DIR / "runner.py")
run_check = load_module("dl_recorder_run_check", CHECK_DIR / "run_check.py")

ANSWERS = {
    "canonical": {
        "choice": "canonical",
        "confidence": 0.95,
        "probabilities": {"canonical": 0.95, "non_canonical_alias": 0.02, "clean_no_domain_terms": 0.02, "ambiguous": 0.01},
    },
    "alias": {
        "choice": "non_canonical_alias",
        "confidence": 0.95,
        "probabilities": {"canonical": 0.02, "non_canonical_alias": 0.95, "clean_no_domain_terms": 0.02, "ambiguous": 0.01},
    },
    "clean": {
        "choice": "clean_no_domain_terms",
        "confidence": 0.95,
        "probabilities": {"canonical": 0.01, "non_canonical_alias": 0.01, "clean_no_domain_terms": 0.95, "ambiguous": 0.03},
    },
    "ambiguous": {
        "choice": "ambiguous",
        "confidence": 0.40,
        "probabilities": {"canonical": 0.25, "non_canonical_alias": 0.25, "clean_no_domain_terms": 0.10, "ambiguous": 0.40},
    },
}


def main() -> None:
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    fixture_dir = Path(sys.argv[1])
    fixture_dir.mkdir(parents=True, exist_ok=True)

    questions = run_check.checks_common.load_json(run_check.QUESTIONS_PATH, label="dl-recorder")["questions"]
    config = runner.load_config(runner.DEFAULT_CONFIG)

    for case_path in sorted((CORPUS_DIR / "cases").glob("*.json")):
        case = json.loads(case_path.read_text(encoding="utf-8"))
        case_name = case.get("case", case_path.stem)
        expected = ANSWERS.get(case_name)
        if not expected:
            continue

        request = {
            "state": {"text": case.get("text", "")},
            "model": config["model"],
            "questions": questions,
        }
        answers = {
            "canonical_terminology": {
                "type": "choice",
                "choice": expected["choice"],
                "confidence": expected["confidence"],
                "probabilities": expected["probabilities"],
            }
        }
        path = runner.record_fixture(request, fixture_dir, {"model": config["model"], "answers": answers})
        print(f"recorded {path.name} for domain-language case {case_name}")


if __name__ == "__main__":
    main()
