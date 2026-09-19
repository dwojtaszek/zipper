#!/usr/bin/env python3
"""Record offline fixture responses for the PR spec compliance corpus (#958).

Rebuilds the exact request run_check.py sends for every case in
tests/typesafe-audit-fixtures/pr-spec/cases (same pure builders), then writes
the expected answer payloads under the runner's request-hash fixture contract
so corpus evaluation runs offline and deterministically.
"""

from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[2]
CHECK_DIR = TOOL_DIR / "checks" / "pr-spec"
CORPUS_DIR = TOOL_DIR.parents[1] / "tests" / "typesafe-audit-fixtures" / "pr-spec"
sys.path.insert(0, str(TOOL_DIR))


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


runner = load_module("pr_spec_recorder_runner", TOOL_DIR / "runner.py")
run_check = load_module("pr_spec_recorder_run_check", CHECK_DIR / "run_check.py")

ANSWERS = {
    100: {
        "implementation": {"type": "choice", "choice": "implemented", "probabilities": {"implemented": 1.0}, "confidence": 0.9},
        "real_outcome_test": {"type": "noul", "noul": 0.02, "confidence": 0.95},
        "unrelated_behavior": {"type": "noul", "noul": 0.03, "confidence": 0.95},
        "docs_sync": {"type": "choice", "choice": "docs_synchronized", "probabilities": {"docs_synchronized": 1.0}, "confidence": 0.9},
    },
    101: {
        "implementation": {"type": "choice", "choice": "partial", "probabilities": {"partial": 1.0}, "confidence": 0.85},
        "real_outcome_test": {"type": "noul", "noul": 0.05, "confidence": 0.95},
        "unrelated_behavior": {"type": "noul", "noul": 0.1, "confidence": 0.95},
        "docs_sync": {"type": "choice", "choice": "readme_missing", "probabilities": {"readme_missing": 1.0}, "confidence": 0.8},
    },
    102: {
        "implementation": {"type": "choice", "choice": "contradicted", "probabilities": {"contradicted": 1.0}, "confidence": 0.9},
        "real_outcome_test": {"type": "noul", "noul": 0.03, "confidence": 0.95},
        "unrelated_behavior": {"type": "noul", "noul": 0.9, "confidence": 0.95},
        "docs_sync": {"type": "choice", "choice": "requirements_missing", "probabilities": {"requirements_missing": 1.0}, "confidence": 0.85},
    },
    103: {
        "implementation": {"type": "choice", "choice": "ambiguous", "probabilities": {"ambiguous": 1.0}, "confidence": 0.4},
        "real_outcome_test": {"type": "noul", "noul": 0.5, "confidence": 0.8},
        "unrelated_behavior": {"type": "noul", "noul": 0.6, "confidence": 0.8},
        "docs_sync": {"type": "choice", "choice": "readme_missing", "probabilities": {"readme_missing": 1.0}, "confidence": 0.3},
    },
}


def main() -> None:
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    fixture_dir = Path(sys.argv[1])
    fixture_dir.mkdir(parents=True, exist_ok=True)

    questions = run_check.load_json(run_check.QUESTIONS_PATH)["questions"]
    policy = run_check.load_json(run_check.POLICY_PATH)
    config = runner.load_config(runner.DEFAULT_CONFIG)

    for case_path in sorted((CORPUS_DIR / "cases").glob("*.json")):
        case = run_check.load_json(case_path)
        obligations, status = run_check.build_obligations(case, policy, None)
        if status == "no-explicit-spec":
            continue  # neutral result: no request is ever sent
        if len(obligations) > policy["batch_size"]:
            # Fail loudly: run_check would send a second batch we have no
            # recorded fixture for (review finding, #958).
            print(f"{case_path.stem}: {len(obligations)} obligations exceed batch_size {policy['batch_size']}.", file=sys.stderr)
            sys.exit(2)
        batch = obligations
        request = run_check.build_request(batch, questions, policy, config)
        expected = ANSWERS[case["pr"]]
        answers = {
            f"{obligation['key']}{run_check.SPAN_SEPARATOR}{qid}": expected[qid]
            for obligation in batch
            for qid in questions
        }
        key = runner.request_fixture_key(request)
        (fixture_dir / f"{key}.json").write_text(
            json.dumps({"model": config["model"], "answers": answers}, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        print(f"recorded {key}.json for case {case_path.stem} (pr {case['pr']})")


if __name__ == "__main__":
    main()
