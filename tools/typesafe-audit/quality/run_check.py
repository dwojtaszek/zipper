#!/usr/bin/env python3
"""TypeSafe quality prioritization check (#959) — advisory.

Deterministic tools stay the source of truth: Cobertura XML yields coverage
gaps and Stryker reports yield mutant categories. TypeSafe only scores each
candidate on four independent impact dimensions (correctness, data loss,
security, regression); priorities are explicit checked-in weights composed
over the raw scores, which are always retained in the report. Exit 0 always
except input errors (2) / remote failure (3): findings never gate.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
QUALITY_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(QUALITY_DIR))
sys.path.insert(0, str(TOOL_DIR))

import checks_common  # noqa: E402
import runner  # noqa: E402
import coverage_gaps  # noqa: E402
import mutation  # noqa: E402

EXIT_OK = runner.EXIT_OK
EXIT_INPUT_ERROR = runner.EXIT_INPUT_ERROR
EXIT_REMOTE_ERROR = runner.EXIT_REMOTE_ERROR

QUESTIONS_PATH = QUALITY_DIR / "questions.json"
WEIGHTS_PATH = QUALITY_DIR / "weights.json"
SPAN_SEPARATOR = checks_common.SPAN_SEPARATOR


def build_state(candidate: dict, max_state_bytes: int) -> dict:
    state = {"candidate": candidate}
    encoded = json.dumps(state, sort_keys=True).encode("utf-8")
    if len(encoded) > max_state_bytes:
        raise ValueError(f"state for {candidate['method']} is {len(encoded)} bytes (limit {max_state_bytes})")
    return state


def build_request(batch: list[dict], questions: dict, weights: dict) -> tuple[dict, dict]:
    config = runner.load_config(runner.DEFAULT_CONFIG)
    namespaced = {}
    candidate_by_key = {}
    states = {}
    for candidate in batch:
        state = build_state(candidate, weights["max_state_bytes"])
        states[candidate["key"]] = state
        for qid, question in questions.items():
            key = f"{candidate['key']}{SPAN_SEPARATOR}{qid}"
            namespaced[key] = question
            candidate_by_key[key] = candidate
    return {"state": states, "model": config["model"], "questions": namespaced}, candidate_by_key


def score_batch(batch: list[dict], questions: dict, weights: dict, config: dict, args, api_key: str, secrets: list[str]) -> list[dict]:
    request, candidate_by_key = build_request(batch, questions, weights)
    if args.mode == "fixture":
        raw = dict(runner.run_fixture(request, Path(args.fixture_dir)))
    else:
        if not api_key:
            print("quality-audit: input error: TYPESAFE_API_KEY must be set for live mode.", file=sys.stderr)
            sys.exit(EXIT_INPUT_ERROR)
        raw = dict(runner.run_live(request, config, api_key, secrets))

    grouped: dict[str, dict] = {}
    for key, candidate in candidate_by_key.items():
        qid = key.rsplit(SPAN_SEPARATOR, 1)[1]
        answer = raw.get("answers", {}).get(key)
        if answer is None:
            print(f"quality-audit: input error: response missing answer '{key}'.", file=sys.stderr)
            sys.exit(EXIT_INPUT_ERROR)
        score = answer.get("score")
        confidence = answer.get("confidence")
        if not isinstance(score, (int, float)) or not 0.0 <= float(score) <= 1.0:
            print(f"quality-audit: input error: answer '{key}' has invalid score {score!r}.", file=sys.stderr)
            sys.exit(EXIT_INPUT_ERROR)
        if confidence is not None and not isinstance(confidence, (int, float)):
            print(f"quality-audit: input error: answer '{key}' has invalid confidence {confidence!r}.", file=sys.stderr)
            sys.exit(EXIT_INPUT_ERROR)
        entry = grouped.setdefault(candidate["key"], {**candidate, "scores": {}})
        entry["scores"][qid] = {"score": score, "confidence": confidence}

    ranked = []
    for entry in grouped.values():
        components = {qid: value["score"] for qid, value in entry["scores"].items()}
        priority = sum(weights["weights"][qid] * score for qid, score in components.items())
        ranked.append({
            "origin": entry["origin"],
            "category": entry.get("category", "coverage_gap"),
            "file": entry["file"],
            "method": entry["method"],
            "lines": f"{entry.get('first_line', entry.get('line'))}-{entry.get('last_line', entry.get('line'))}",
            "req_ids": entry["req_ids"],
            "tests": entry.get("tests", []),
            "description": entry.get("description", ""),
            "components": components,
            "confidences": {qid: value["confidence"] for qid, value in entry["scores"].items()},
            "weights": weights["weights"],
            "priority": round(priority, 6),
            "source_sha256": entry["source_sha256"],
        })
    ranked.sort(key=lambda c: (-c["priority"], c["file"], c["method"]))
    return ranked


def render_markdown(report: dict) -> str:
    lines = ["# Quality Prioritization Report", ""]
    lines.append(f"- Mode: {report['mode']}")
    lines.append(f"- Runner: v{report['runner_version']}")
    lines.append(f"- Weights: {json.dumps(report['weights'], sort_keys=True)}")
    lines.append("")
    lines.append("| Priority | Origin | Category | Method | REQ IDs |")
    lines.append("| --- | --- | --- | --- | --- |")
    for candidate in report["ranked"]:
        reqs = ", ".join(candidate["req_ids"]) or "-"
        lines.append(
            f"| {candidate['priority']:.4f} | {candidate['origin']} | {candidate['category']} | "
            f"`{candidate['file']}:{candidate['lines']} {candidate['method']}` | {reqs} |"
        )
    lines.append("")
    lines.append("_Deterministic tools own coverage/survivor truth; this ranking is advisory._")
    lines.append("")
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Quality prioritization audit (advisory).")
    parser.add_argument("--coverage", type=Path, help="Cobertura XML from the test run.")
    parser.add_argument("--mutation", type=Path, help="Stryker JSON mutation report.")
    parser.add_argument("--repo-root", type=Path, default=Path.cwd())
    parser.add_argument("--mode", choices=("live", "fixture"), default="fixture")
    parser.add_argument("--fixture-dir", type=Path, default=QUALITY_DIR / "fixtures")
    parser.add_argument("--json-out", type=Path, required=True)
    parser.add_argument("--md-out", type=Path, required=True)
    parser.add_argument("--summary-out", type=Path, help="Append the Markdown report to this file (CI job summary).")
    args = parser.parse_args(argv)

    questions = checks_common.load_json(QUESTIONS_PATH, label="quality-audit")["questions"]
    weights = checks_common.load_json(WEIGHTS_PATH, label="quality-audit")
    config = runner.load_config(runner.DEFAULT_CONFIG)
    api_key = ""
    secrets: list[str] = []
    if args.mode == "live":
        api_key = os.environ.get("TYPESAFE_API_KEY", "")
        secrets = [api_key] if api_key else []

    if not args.coverage and not args.mutation:
        print("quality-audit: input error: --coverage and/or --mutation is required.", file=sys.stderr)
        return EXIT_INPUT_ERROR

    candidates: list[dict] = []
    mutation_categories: dict[str, list[dict]] = {}
    try:
        if args.coverage:
            candidates.extend(coverage_gaps.extract_gaps(args.coverage, args.repo_root, max_gaps=weights["max_gaps"]))
        if args.mutation:
            mutation_categories = mutation.parse_report(args.mutation, args.repo_root)
            candidates.extend(mutation_categories.get("survived", []))
    except (OSError, ValueError) as exc:
        print(f"quality-audit: input error: {exc}", file=sys.stderr)
        return EXIT_INPUT_ERROR

    for index, candidate in enumerate(candidates):
        candidate["key"] = f"{candidate['file']}:{candidate.get('first_line', candidate.get('line'))}#{index}"

    ranked: list[dict] = []
    batch_size = weights["batch_size"]
    try:
        for start in range(0, len(candidates), batch_size):
            ranked.extend(score_batch(candidates[start:start + batch_size], questions, weights, config, args, api_key, secrets))
    except ValueError as exc:
        # build_state budget violations are input errors, not findings.
        print(f"quality-audit: input error: {exc}", file=sys.stderr)
        return EXIT_INPUT_ERROR
    ranked.sort(key=lambda c: (-c["priority"], c["file"], c["method"]))

    report = {
        "runner_version": config["runner_version"],
        "mode": args.mode,
        "weights": weights["weights"],
        "coverage_candidates": sum(1 for c in candidates if c["origin"] == "coverage"),
        "mutation_categories": {name: len(entries) for name, entries in sorted(mutation_categories.items())},
        "ranked": ranked,
    }

    args.json_out.parent.mkdir(parents=True, exist_ok=True)
    args.json_out.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    markdown = render_markdown(report)
    args.md_out.write_text(markdown, encoding="utf-8")
    if args.summary_out:
        with open(args.summary_out, "a", encoding="utf-8") as fh:
            fh.write(markdown)

    print(f"quality-audit: {len(ranked)} ranked candidate(s) (advisory only).")
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
