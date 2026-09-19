#!/usr/bin/env python3
"""Record offline fixture responses for the quality prioritization corpus (#959)."""

from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
QUALITY_DIR = TOOL_DIR / "quality"
CORPUS_DIR = TOOL_DIR.parents[1] / "tests" / "typesafe-audit-fixtures" / "quality"
sys.path.insert(0, str(QUALITY_DIR))


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


runner = load_module("quality_recorder_runner", TOOL_DIR / "runner.py")
run_check = load_module("quality_recorder_run_check", QUALITY_DIR / "run_check.py")
coverage_gaps = load_module("quality_recorder_coverage", QUALITY_DIR / "coverage_gaps.py")
mutation = load_module("quality_recorder_mutation", QUALITY_DIR / "mutation.py")

# Maintainer-labeled impact scores: the output-validation method is the
# consequential candidate; the archive-name helper is the trivial one.
HIGH = {"correctness": 0.9, "data_loss": 0.9, "security": 0.7, "regression": 0.8}
MEDIUM = {"correctness": 0.5, "data_loss": 0.4, "security": 0.2, "regression": 0.5}
CONFIDENCE = 0.9


def main() -> None:
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    fixture_dir = Path(sys.argv[1])
    fixture_dir.mkdir(parents=True, exist_ok=True)

    questions = run_check.checks_common.load_json(run_check.QUESTIONS_PATH, label="quality-recorder")["questions"]
    weights = run_check.checks_common.load_json(run_check.WEIGHTS_PATH, label="quality-recorder")
    config = runner.load_config(runner.DEFAULT_CONFIG)

    candidates = coverage_gaps.extract_gaps(CORPUS_DIR / "cobertura.xml", CORPUS_DIR, max_gaps=weights["max_gaps"])
    candidates.extend(mutation.parse_report(CORPUS_DIR / "stryker.json", CORPUS_DIR).get("survived", []))
    for index, candidate in enumerate(candidates):
        candidate["key"] = f"{candidate['file']}:{candidate.get('first_line', candidate.get('line'))}#{index}"

    batch = candidates[: weights["batch_size"]]
    if len(candidates) > weights["batch_size"]:
        print(f"{len(candidates)} candidates exceed batch_size {weights['batch_size']}.", file=sys.stderr)
        sys.exit(2)

    request, candidate_by_key = run_check.build_request(batch, questions, weights)
    answers = {}
    for key, candidate in candidate_by_key.items():
        qid = key.rsplit(run_check.SPAN_SEPARATOR, 1)[1]
        label = HIGH if candidate["method"] == "ValidateDestination" else MEDIUM
        answers[key] = {"type": "score", "score": label[qid], "confidence": CONFIDENCE}

    key = runner.request_fixture_key(request)
    (fixture_dir / f"{key}.json").write_text(
        json.dumps({"model": config["model"], "answers": answers}, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    print(f"recorded {key}.json for {len(batch)} candidate(s)")


if __name__ == "__main__":
    main()
