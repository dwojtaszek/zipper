#!/usr/bin/env python3
"""TypeSafe issue triage (#960) — DRY-RUN ONLY, advisory.

Deterministic preparation (markup stripping, size caps, keyword extraction,
duplicate-candidate search over retrieved issues, closed sets) precedes any
model call. TypeSafe picks among supplied values only; the check discards any
selection that is not in the supplied candidate sets. Output is JSON/Markdown/
job summary ONLY: this tool never labels, comments, closes, assigns, or creates
issues. API failure, missing candidates, or low confidence yield neutral
results. Exit 0 always except input errors (2) / remote failure (3).
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[1]
TRIAGE_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(TRIAGE_DIR))
sys.path.insert(0, str(TOOL_DIR))

import checks_common  # noqa: E402
import runner  # noqa: E402

collect = checks_common.load_module("tsa_triage_collect", TRIAGE_DIR / "collect.py")

EXIT_OK = runner.EXIT_OK
EXIT_INPUT_ERROR = runner.EXIT_INPUT_ERROR
EXIT_REMOTE_ERROR = runner.EXIT_REMOTE_ERROR

QUESTIONS_PATH = TRIAGE_DIR / "questions.json"
POLICY_PATH = TRIAGE_DIR / "policy.json"
SPAN_SEPARATOR = checks_common.SPAN_SEPARATOR


def load_requirements_ids(repo_root: Path) -> list[str]:
    path = repo_root / "Requirements.md"
    if not path.exists():
        return []
    import re

    return sorted(set(re.findall(r"\bREQ-\d{3,4}\b", path.read_text(encoding="utf-8"))))


def prepare(issue: dict, corpus: list[dict], policy: dict, req_ids: list[str]) -> dict:
    """Deterministic state: stripped text, keywords, candidates, allowed sets."""
    # Cumulative cap across body + comments keeps untrusted text bounded.
    budget = policy["max_issue_chars"]
    text = collect.strip_markup(f"{issue.get('title', '')}\n{issue.get('body', '')}", budget)
    budget -= len(text)
    for comment in issue.get("comments", []):
        if budget <= 0:
            break
        excerpt = collect.strip_markup(comment.get("body", ""), budget)
        text += "\n" + excerpt
        budget -= len(excerpt)
    candidates = collect.search_candidates(issue, corpus, policy["max_candidates"])
    reqs = sorted(set(collect.extract_keywords(text)) & set(req_ids))[: policy["max_req_ids"]]
    return {
        "issue_number": issue.get("number"),
        "labels": issue.get("labels", []),
        "text": text,
        "keywords": collect.extract_keywords(text),
        "candidates": [
            {
                "number": c.get("number"),
                "title": c.get("title", ""),
                "state": c.get("state", ""),
                "labels": c.get("labels", []),
                "url": c.get("url") or f"issues/{c.get('number')}",
                "body": collect.strip_markup(c.get("body", ""), 1024),
            }
            for c in candidates
        ],
        "allowed": collect.allowed_sets(policy, req_ids),
        "mentioned_req_ids": reqs,
    }


def build_request(prepared: dict, questions: dict, policy: dict) -> tuple[dict, list[str]]:
    """Namespaced request: base questions + per-candidate + per-REQ questions."""
    config = runner.load_config(runner.DEFAULT_CONFIG)
    state_size = len(json.dumps({"prepared": prepared}, sort_keys=True).encode("utf-8"))
    if state_size > policy["max_state_bytes"]:
        raise ValueError(f"triage state is {state_size} bytes (limit {policy['max_state_bytes']})")

    namespaced: dict[str, dict] = {}
    for qid, question in questions["questions"].items():
        namespaced[question_key(qid)] = question
    for candidate in prepared["candidates"]:
        namespaced[f"cand{SPAN_SEPARATOR}{candidate['number']}{SPAN_SEPARATOR}duplicate"] = questions["duplicate_question"]
    for req_id in prepared["mentioned_req_ids"]:
        namespaced[f"req{SPAN_SEPARATOR}{req_id}{SPAN_SEPARATOR}relevance"] = questions["req_question"]
    request = {"state": {"prepared": prepared}, "model": config["model"], "questions": namespaced}
    answer_keys = list(namespaced)
    return request, answer_keys


def question_key(qid: str) -> str:
    return f"main{SPAN_SEPARATOR}{qid}"


def status_for(value: str, confidence: float | None, policy: dict) -> str:
    if confidence is None or confidence < policy["confidence_threshold"]:
        return "needs-human-review"
    return "judged"


def judge(prepared: dict, questions: dict, policy: dict, config: dict, args, api_key: str, secrets: list[str]) -> dict:
    request, answer_keys = build_request(prepared, questions, policy)
    if args.mode == "fixture":
        raw = dict(runner.run_fixture(request, Path(args.fixture_dir)))
    else:
        if not api_key:
            print("triage-audit: input error: TYPESAFE_API_KEY must be set for live mode.", file=sys.stderr)
            sys.exit(EXIT_INPUT_ERROR)
        raw = dict(runner.run_live(request, config, api_key, secrets))

    answers = raw.get("answers", {})
    judgments: dict[str, dict] = {}
    for qid in questions["questions"]:
        key = question_key(qid)
        answer = answers.get(key)
        if answer is None:
            # Missing answers are neutral (untrusted issue text can steer the
            # model); they never fail the advisory dry run (review, #960).
            judgments[qid] = {"value": None, "confidence": None, "status": "needs-human-review"}
            continue
        confidence = answer.get("confidence")
        value = answer.get("choice") if questions["questions"][qid]["type"] == "choice" else answer.get("noul")
        judgments[qid] = {
            "value": value,
            "confidence": confidence,
            "status": status_for(value, confidence, policy),
        }
    # missing_info is a Noul probability; surface the derived verdict too
    # (noul = P(information is NOT missing)).
    raw_missing = judgments["missing_info"]["value"]
    if isinstance(raw_missing, (int, float)):
        judgments["missing_info"]["noul"] = raw_missing
        judgments["missing_info"]["value"] = "yes" if raw_missing <= 0.5 else "no"
    # Closed-set enforcement: a selection outside the supplied candidates is
    # demoted to a neutral needs-human-review, never applied.
    allowed = prepared["allowed"]
    if judgments["type"]["value"] not in questions["questions"]["type"]["criteria"]:
        judgments["type"] = {"value": None, "confidence": judgments["type"]["confidence"], "status": "needs-human-review"}
    if judgments["subsystem"]["value"] not in allowed["subsystems"]:
        judgments["subsystem"] = {"value": None, "confidence": judgments["subsystem"]["confidence"], "status": "needs-human-review"}
    if judgments["priority"]["value"] not in policy["priorities"] + ["insufficient-evidence"]:
        judgments["priority"] = {"value": None, "confidence": judgments["priority"]["confidence"], "status": "needs-human-review"}

    duplicates = []
    for candidate in prepared["candidates"]:
        answer = answers.get(f"cand{SPAN_SEPARATOR}{candidate['number']}{SPAN_SEPARATOR}duplicate")
        if answer is None:
            # Neutral on a missing per-candidate answer (review, #960).
            judgments.setdefault("duplicates", []).append({
                "candidate": candidate["number"], "candidate_url": candidate["url"],
                "value": None, "confidence": None, "status": "needs-human-review",
            })
            continue
        confidence = answer.get("confidence")
        value = answer.get("choice")
        if value not in questions["duplicate_question"]["criteria"]:
            value = "insufficient-evidence"
        entry = {"candidate": candidate["number"], "candidate_url": candidate["url"], "value": value, "confidence": confidence}
        # Duplicate suggestions require retrieval + high-confidence judgment.
        entry["status"] = "duplicate-suggestion" if value == "duplicate" and confidence is not None and confidence >= policy["confidence_threshold"] else status_for(value, confidence, policy)
        judgments.setdefault("duplicates", []).append(entry)

    req_relevance = {}
    for req_id in prepared["mentioned_req_ids"]:
        answer = answers.get(f"req{SPAN_SEPARATOR}{req_id}{SPAN_SEPARATOR}relevance")
        if answer is None:
            req_relevance[req_id] = {"noul": None, "confidence": None, "status": "needs-human-review"}
            continue
        req_relevance[req_id] = {"noul": answer.get("noul"), "confidence": answer.get("confidence")}
    judgments["req_relevance"] = req_relevance
    return judgments


def render_markdown(report: dict) -> str:
    lines = ["# Issue Triage (dry-run)", ""]
    lines.append(f"- Mode: {report['mode']}")
    lines.append(f"- Runner: v{report['runner_version']}")
    lines.append("")
    for result in report["results"]:
        lines.append(f"## Issue #{result['issue']}")
        lines.append("")
        if result.get("neutral"):
            # Nothing to judge (no candidates, no REQ ties): no judgment keys exist.
            lines.append(f"- Neutral: {result['neutral']} (nothing to judge).")
            lines.append("")
            continue
        j = result["judgments"]
        lines.append(f"- Type: {j['type']['value']} ({j['type']['status']})")
        lines.append(f"- Subsystem: {j['subsystem']['value']} ({j['subsystem']['status']})")
        lines.append(f"- Priority: {j['priority']['value']} ({j['priority']['status']})")
        lines.append(f"- Missing info: {j['missing_info']['value']} ({j['missing_info']['status']})")
        for dup in j.get("duplicates", []):
            lines.append(f"- Duplicate candidate #{dup['candidate']} ({dup['candidate_url']}): {dup['value']} ({dup['status']})")
        for req_id, relevance in sorted(j.get("req_relevance", {}).items()):
            lines.append(f"- REQ relevance {req_id}: noul={relevance['noul']}")
        lines.append("")
    lines.append("_Dry run: no labels, comments, closures, assignments, or new issues were written._")
    lines.append("")
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="TypeSafe issue triage (dry-run, advisory).")
    parser.add_argument("--issue-json", type=Path, help="Pre-collected issue context JSON.")
    parser.add_argument("--corpus", type=Path, help="Evaluation corpus dir with issues/*.json and open-issues.json.")
    parser.add_argument("--mode", choices=("live", "fixture"), default="fixture")
    parser.add_argument("--fixture-dir", type=Path, default=TRIAGE_DIR / "fixtures")
    parser.add_argument("--json-out", type=Path, required=True)
    parser.add_argument("--md-out", type=Path, required=True)
    parser.add_argument("--summary-out", type=Path, help="Append the Markdown report to this file (CI job summary).")
    args = parser.parse_args(argv)

    questions = checks_common.load_json(QUESTIONS_PATH, label="triage-audit")
    policy = checks_common.load_json(POLICY_PATH, label="triage-audit")
    config = runner.load_config(runner.DEFAULT_CONFIG)
    api_key = ""
    secrets: list[str] = []
    if args.mode == "live":
        api_key = os.environ.get("TYPESAFE_API_KEY", "")
        secrets = [api_key] if api_key else []

    repo_root = Path.cwd()
    req_ids = load_requirements_ids(repo_root)
    corpus: list[dict] = []
    cases: list[dict] = []
    if args.corpus:
        open_issues = checks_common.load_json(args.corpus / "open-issues.json", label="triage-audit")
        corpus = open_issues["issues"]
        for path in sorted((args.corpus / "issues").glob("*.json")):
            cases.append(collect.load_json(path))
    elif args.issue_json:
        case = collect.load_json(args.issue_json)
        corpus = case.get("open_issues", [])
        cases = [case]
    else:
        print("triage-audit: input error: one of --issue-json or --corpus is required.", file=sys.stderr)
        return EXIT_INPUT_ERROR

    results: list[dict] = []
    try:
        for case in cases:
            prepared = prepare(case, corpus, policy, req_ids)
            if not prepared["candidates"] and not prepared["mentioned_req_ids"]:
                # Neutral result: nothing to judge (no candidates, no REQ ties).
                results.append({"issue": case.get("number"), "judgments": {}, "neutral": "no-candidates"})
                continue
            judgments = judge(prepared, questions, policy, config, args, api_key, secrets)
            results.append({"issue": case.get("number"), "judgments": judgments})
    except (ValueError, KeyError) as exc:
        # Malformed case JSON or oversized state: clean input error, no traceback.
        print(f"triage-audit: input error: {exc}", file=sys.stderr)
        return EXIT_INPUT_ERROR

    report = {
        "runner_version": config["runner_version"],
        "mode": args.mode,
        "dry_run": True,
        "results": results,
    }
    checks_common.write_outputs(args.json_out, args.md_out, args.summary_out, report, render_markdown(report))

    print(f"triage-audit: {len(results)} issue(s) triaged (dry-run, advisory only).")
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
