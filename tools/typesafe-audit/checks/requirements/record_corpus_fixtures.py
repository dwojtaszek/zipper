#!/usr/bin/env python3
"""Records deterministic fixture responses for the evaluation corpus (#956).

Builds the exact batched request the audit pipeline sends in corpus mode and
records a synthetic response whose answers match the expected labels. Checked-in
corpus + recorded responses let CI verify the extraction -> policy -> status
pipeline without any network.
"""

from __future__ import annotations

import sys
from pathlib import Path

CHECK_DIR = Path(__file__).resolve().parent
TOOL_DIR = CHECK_DIR.parents[1]
REPO_ROOT = TOOL_DIR.parents[1]
POLICY_PATH = CHECK_DIR / "policy.json"
QUESTIONS_PATH = CHECK_DIR / "questions.json"
sys.path.insert(0, str(TOOL_DIR))
sys.path.insert(0, str(CHECK_DIR))

import runner  # noqa: E402
from run_check import (  # noqa: E402
    SPAN_SEPARATOR,
    build_batch_request,
    load_json,
    sections_from_corpus,
)

CORPUS_DIR = REPO_ROOT / "tests/typesafe-audit-fixtures"


def main() -> int:
    if len(sys.argv) != 2:
        print(f"usage: {Path(__file__).name} <fixture-dir>", file=sys.stderr)
        return 2

    policy = load_json(POLICY_PATH)
    questions = load_json(QUESTIONS_PATH)["questions"]
    config = runner.load_config(runner.DEFAULT_CONFIG)
    fixture_dir = Path(sys.argv[1])
    expected = load_json(CORPUS_DIR / "expected_labels.json")["examples"]
    sections = sections_from_corpus(CORPUS_DIR)

    # Non-full corpus mode batches every section into one request.
    request, span_by_key = build_batch_request(sections, sections, True, questions, policy)

    answers = {}
    for key, section in span_by_key.items():
        qid = key.rsplit(SPAN_SEPARATOR, 1)[1]
        file_name = section["path"]
        expected_answers = next(e["expected"] for e in expected if e["file"] == file_name)
        value = expected_answers[qid]
        if questions[qid]["type"] == "noul":
            answers[key] = {"type": "noul", "noul": 0.99 if value else 0.01}
        else:
            answers[key] = {
                "type": "choice",
                "choice": value,
                "probabilities": {value: 1.0},
                "confidence": 0.95,
            }
    response = {
        "model": config["model"],
        "answers": answers,
        "usage": {"input_tokens": 200, "output_tokens": 20},
    }
    path = runner.record_fixture(request, fixture_dir, response)
    print(f"recorded corpus fixture {path.name[:16]}…")
    return 0


if __name__ == "__main__":
    sys.exit(main())
