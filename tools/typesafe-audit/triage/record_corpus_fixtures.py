#!/usr/bin/env python3
"""Record offline fixture responses for the triage corpus (#960)."""

from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
TRIAGE_DIR = TOOL_DIR / "triage"
CORPUS_DIR = TOOL_DIR.parents[1] / "tests" / "typesafe-audit-fixtures" / "triage"
sys.path.insert(0, str(TRIAGE_DIR))


def load_module(name: str, path: Path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


runner = load_module("triage_recorder_runner", TOOL_DIR / "runner.py")
run_check = load_module("triage_recorder_run_check", TRIAGE_DIR / "run_check.py")
collect = load_module("triage_recorder_collect", TRIAGE_DIR / "collect.py")
HIGH = 0.95

# Maintainer-labeled judgments per corpus issue.
LABELS = {
    800: {"type": "security", "subsystem": "archive", "priority": "P1", "missing_info": 0.98},
    801: {"type": "unclear", "subsystem": "unknown", "priority": "insufficient-evidence", "missing_info": 0.02},
    802: {"type": "bug", "subsystem": "archive", "priority": "P1", "missing_info": 0.02},
}
DUPLICATES = {802: {700: "duplicate"}, 800: {700: "related"}}


def main() -> None:
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    fixture_dir = Path(sys.argv[1])
    fixture_dir.mkdir(parents=True, exist_ok=True)

    questions = run_check.checks_common.load_json(run_check.QUESTIONS_PATH, label="triage-recorder")
    policy = run_check.checks_common.load_json(run_check.POLICY_PATH, label="triage-recorder")
    config = runner.load_config(runner.DEFAULT_CONFIG)
    req_ids = run_check.load_requirements_ids(run_check.TOOL_DIR.parents[1])

    corpus = collect.load_json(CORPUS_DIR / "open-issues.json")["issues"]
    for case_path in sorted((CORPUS_DIR / "issues").glob("*.json")):
        case = collect.load_json(case_path)
        prepared = run_check.prepare(case, corpus, policy, req_ids)
        request, _keys = run_check.build_request(prepared, questions, policy)
        labels = LABELS[case["number"]]
        answers = {
            run_check.question_key("type"): {"type": "choice", "choice": labels["type"], "probabilities": {labels["type"]: 1.0}, "confidence": HIGH},
            run_check.question_key("subsystem"): {"type": "choice", "choice": labels["subsystem"], "probabilities": {labels["subsystem"]: 1.0}, "confidence": HIGH},
            run_check.question_key("priority"): {"type": "choice", "choice": labels["priority"], "probabilities": {labels["priority"]: 1.0}, "confidence": HIGH},
            run_check.question_key("missing_info"): {"type": "noul", "noul": labels["missing_info"], "confidence": HIGH},
        }
        dup_labels = DUPLICATES.get(case["number"], {})
        for candidate in prepared["candidates"]:
            value = dup_labels.get(candidate["number"], "unrelated")
            answers[f"cand{run_check.SPAN_SEPARATOR}{candidate['number']}{run_check.SPAN_SEPARATOR}duplicate"] = {
                "type": "choice", "choice": value, "probabilities": {value: 1.0}, "confidence": HIGH,
            }
        for req_id in prepared["mentioned_req_ids"]:
            answers[f"req{run_check.SPAN_SEPARATOR}{req_id}{run_check.SPAN_SEPARATOR}relevance"] = {"type": "noul", "noul": 0.98, "confidence": HIGH}
        key = runner.request_fixture_key(request)
        (fixture_dir / f"{key}.json").write_text(
            json.dumps({"model": config["model"], "answers": answers}, indent=2, sort_keys=True) + "\n",
            encoding="utf-8",
        )
        print(f"recorded {key}.json for issue {case['number']} ({len(prepared['candidates'])} candidate(s))")


if __name__ == "__main__":
    main()
