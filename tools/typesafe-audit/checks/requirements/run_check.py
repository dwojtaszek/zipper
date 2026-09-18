#!/usr/bin/env python3
"""Requirements quality + documentation consistency audit (#956).

Pipeline (deterministic first, semantic second, advisory always):
  1. candidate spans: changed sections from `git diff base...head`, an
     evaluation corpus dir, or every active requirement (--full)
  2. deterministic prechecks: immutable REQ IDs, orphan references, missing
     traceability rows — exact, no API
  3. TypeSafe judgments: for each batch of spans, the 5 questions are asked
     per span (question ids namespaced `<span>#<question>`) over shared state
     that includes cross-document excerpts (requirement definitions, glossary
     headings) — TypeSafe only picks among code-prepared candidates
  4. one JSON result per changed span + grouped Markdown summary; low
     confidence or mid-range signals are needs-human-review, never defects

Exit codes mirror the runner: 0 ok/advisory, 2 config/input error,
3 remote failure. This check never emits exit 1: every category is advisory.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[2]
CHECK_DIR = Path(__file__).resolve().parent
sys.path.insert(0, str(TOOL_DIR))
sys.path.insert(0, str(CHECK_DIR))

import runner  # noqa: E402
import prechecks  # noqa: E402
import checks_common  # noqa: E402
from extract_sections import extract_sections, group_by_requirement  # noqa: E402

EXIT_OK = runner.EXIT_OK
EXIT_INPUT_ERROR = runner.EXIT_INPUT_ERROR
EXIT_REMOTE_ERROR = runner.EXIT_REMOTE_ERROR

QUESTIONS_PATH = CHECK_DIR / "questions.json"
POLICY_PATH = CHECK_DIR / "policy.json"
SPAN_SEPARATOR = checks_common.SPAN_SEPARATOR


def load_json(path: Path) -> dict:
    return checks_common.load_json(path, label="requirements-audit")


def span_id(section: dict) -> str:
    return f"{section['path']}:{section['start_line']}-{section['end_line']}"


def sections_from_corpus(corpus_dir: Path) -> list[dict]:
    """Evaluation mode: each corpus file is one candidate span."""
    sections = []
    for path in sorted(corpus_dir.glob("*.md")):
        text = path.read_text(encoding="utf-8")
        sections.append({
            "path": str(path.relative_to(corpus_dir)),
            "start_line": 1,
            "end_line": len(text.splitlines()),
            "text": text,
            "req_ids": [],
        })
    return sections


def build_context(section: dict, all_sections: list[dict], corpus_mode: bool, repo_root: Path) -> list[dict]:
    """Cross-document excerpts the questions judge the section against.

    Directly linked material only (issue #956): requirement definitions for the
    section's REQ IDs, and in corpus mode the sibling corpus excerpts that play
    the other contracts. Never the whole repository.
    """
    context: list[dict] = []
    if corpus_mode:
        for other in all_sections:
            if other is not section:
                context.append({"source": span_id(other), "text": other["text"]})
        return context
    definitions = prechecks.load_requirements(repo_root)
    for req_id in section["req_ids"]:
        if req_id in definitions and section["path"] != "Requirements.md":
            line_no, text = definitions[req_id]
            context.append({"source": f"Requirements.md:{line_no}", "text": text})
    return context


def build_state(section: dict, context: list[dict], glossary_terms: list[str], max_state_bytes: int) -> dict:
    """Candidate spans are prepared by code; TypeSafe only picks among them."""
    state = {
        "sections": [{
            "source": span_id(section),
            "text": section["text"],
            "requirement_ids": section["req_ids"],
        }],
        "context_excerpts": context,
        "canonical_glossary_terms": glossary_terms,
    }
    encoded = json.dumps(state, sort_keys=True).encode("utf-8")
    if len(encoded) > max_state_bytes:
        print(
            f"requirements-audit: input error: state for {span_id(section)} is {len(encoded)} bytes (limit {max_state_bytes}).",
            file=sys.stderr,
        )
        sys.exit(EXIT_INPUT_ERROR)
    return state


def status_for(question_id: str, row: dict, policy: dict) -> str:
    return checks_common.status_for(question_id, row, policy)


def build_batch_request(batch: list[dict], all_sections: list[dict], corpus_mode: bool, questions: dict, policy: dict) -> tuple[dict, dict]:
    """Namespaced request for one batch + span lookup for the answer keys."""
    glossary_terms = [] if corpus_mode else sorted(prechecks.load_glossary_terms(Path.cwd()))[: policy["glossary_term_cap"]]
    config = runner.load_config(runner.DEFAULT_CONFIG)
    namespaced = {}
    span_by_key: dict[str, dict] = {}
    for section in batch:
        sid = span_id(section)
        context = build_context(section, all_sections, corpus_mode, Path.cwd())
        state = build_state(section, context, glossary_terms, policy["max_state_bytes"])
        for qid, question in questions.items():
            key = f"{sid}{SPAN_SEPARATOR}{qid}"
            namespaced[key] = question
            span_by_key[key] = section
    return {"state": state, "model": config["model"], "questions": namespaced}, span_by_key


def audit_batch(batch: list[dict], all_sections: list[dict], corpus_mode: bool, questions: dict, policy: dict, config: dict, args, api_key: str, secrets: list[str]) -> list[dict]:
    """One TypeSafe request per batch: 5 questions x N spans, namespaced ids.

    Batching independent questions over shared state in one request (runner
    rule, #955) keeps per-span granularity via the namespaced answer keys.
    """
    request, span_by_key = build_batch_request(batch, all_sections, corpus_mode, questions, policy)

    if args.mode == "fixture":
        raw = dict(runner.run_fixture(request, Path(args.fixture_dir)))
        raw["_mode"] = "fixture"
    else:
        if not api_key:
            print("requirements-audit: input error: TYPESAFE_API_KEY must be set for live mode.", file=sys.stderr)
            sys.exit(EXIT_INPUT_ERROR)
        raw = dict(runner.run_live(request, config, api_key, secrets))
        raw["_mode"] = "live"

    results: list[dict] = []
    for key, section in span_by_key.items():
        qid = key.rsplit(SPAN_SEPARATOR, 1)[1]
        question = questions[qid]
        answer = raw.get("answers", {}).get(key)
        if answer is None:
            print(f"requirements-audit: input error: response missing answer '{key}'.", file=sys.stderr)
            sys.exit(EXIT_INPUT_ERROR)
        row = {
            "question": qid,
            "type": question["type"],
            "answer": (
                answer.get("choice") if question["type"] == "choice"
                else answer.get("noul") if question["type"] == "noul"
                else answer.get("score")
            ),
            "probabilities": answer.get("probabilities"),
            "confidence": answer.get("confidence"),
        }
        row["status"] = status_for(qid, row, policy)
        results.append({
            "source": span_id(section),
            "requirement_ids": section["req_ids"],
            "evidence": [{"path": section["path"], "lines": f"{section['start_line']}-{section['end_line']}"}],
            **row,
        })
    return results


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Requirements semantic audit (advisory).")
    parser.add_argument("--base", help="Base SHA for changed-section extraction.")
    parser.add_argument("--head", default="HEAD")
    parser.add_argument("--corpus", type=Path, help="Evaluation corpus dir (skips git extraction).")
    parser.add_argument("--full", action="store_true", help="Audit every active requirement in bounded batches.")
    parser.add_argument("--mode", choices=("live", "fixture"), default="fixture")
    parser.add_argument("--fixture-dir", type=Path, default=CHECK_DIR / "fixtures")
    parser.add_argument("--json-out", type=Path, required=True)
    parser.add_argument("--md-out", type=Path, required=True)
    parser.add_argument("--summary-out", type=Path, help="Append the Markdown report to this file (CI job summary).")
    args = parser.parse_args(argv)

    policy = load_json(POLICY_PATH)
    questions = load_json(QUESTIONS_PATH)["questions"]
    config = runner.load_config(runner.DEFAULT_CONFIG)
    api_key = ""
    secrets: list[str] = []
    if args.mode == "live":
        api_key = os.environ.get("TYPESAFE_API_KEY", "")
        secrets = [api_key] if api_key else []

    omitted = 0
    if args.corpus:
        sections = sections_from_corpus(args.corpus)
        base = None
        corpus_mode = True
    else:
        corpus_mode = False
        if args.full:
            definitions = prechecks.load_requirements(Path.cwd())
            if not definitions:
                print("requirements-audit: input error: no active requirements found.", file=sys.stderr)
                return EXIT_INPUT_ERROR
            all_sections = [
                {
                    "path": "Requirements.md",
                    "start_line": line_no,
                    "end_line": line_no,
                    "text": text,
                    "req_ids": [req_id],
                }
                for req_id, (line_no, text) in sorted(definitions.items())
            ]
            batch_size = policy["full_mode_batch_size"]
            max_requests = policy["full_mode_max_requests"]
            sections = all_sections[: batch_size * max_requests]
            omitted = len(all_sections) - len(sections)
            if omitted:
                print(
                    f"requirements-audit: request budget bounds full mode to {len(sections)} of "
                    f"{len(all_sections)} requirements ({omitted} omitted); raise full_mode_max_requests to cover all.",
                    file=sys.stderr,
                )
            base = None
        else:
            if not args.base:
                print("requirements-audit: input error: --base is required without --corpus/--full.", file=sys.stderr)
                return EXIT_INPUT_ERROR
            try:
                sections = extract_sections(Path.cwd(), args.base, args.head)
            except RuntimeError as exc:
                # e.g. shallow clone without the base commit: clean message, no traceback.
                print(f"requirements-audit: input error: extraction failed: {exc}", file=sys.stderr)
                return EXIT_INPUT_ERROR
            base = args.base

    # Deterministic prechecks run first and never call the API.
    precheck_findings = (
        prechecks.run_all_prechecks(Path.cwd(), base or "HEAD", sections, args.head)
        if not corpus_mode else []
    )

    batch_size = len(sections) if not args.full else policy["full_mode_batch_size"]
    results: list[dict] = []
    for batch_start in range(0, len(sections), batch_size):
        batch = sections[batch_start:batch_start + batch_size]
        results.extend(audit_batch(batch, sections, corpus_mode, questions, policy, config, args, api_key, secrets))

    report = {
        "runner_version": config["runner_version"],
        "mode": args.mode,
        "full": args.full,
        "omitted_count": omitted,
        "results": results,
        "precheck_findings": precheck_findings,
        "needs_human_review": any(r["status"] == "needs-human-review" for r in results),
    }

    args.json_out.parent.mkdir(parents=True, exist_ok=True)
    args.json_out.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    markdown = render_markdown(report)
    args.md_out.write_text(markdown, encoding="utf-8")
    if args.summary_out:
        # GitHub Actions job summary (append; no PR comments in this slice).
        with open(args.summary_out, "a", encoding="utf-8") as fh:
            fh.write(markdown)

    defects = [r for r in results if r["status"] == "finding"] + precheck_findings
    print(
        f"requirements-audit: {len(results)} judgment(s), {len(defects)} finding(s) "
        f"(advisory only), {sum(1 for r in results if r['status'] == 'needs-human-review')} needs-human-review."
    )
    return EXIT_OK


def render_markdown(report: dict) -> str:
    lines = ["# Requirements Audit Report", ""]
    mode = "full audit" if report["full"] else "changed sections"
    lines.append(f"- Mode: {report['mode']} ({mode})")
    lines.append(f"- Runner: v{report['runner_version']}")
    if report.get("omitted_count"):
        lines.append(f"- **{report['omitted_count']} requirement(s) omitted by the full-mode request budget.**")
    lines.append("")
    for finding in report["precheck_findings"]:
        lines.append(f"- **{finding['check']}**: {finding['message']} (`{finding['source']}`)")
    if report["precheck_findings"]:
        lines.append("")
    lines.append("| Source span | Question | Answer | Confidence | Status |")
    lines.append("| --- | --- | --- | --- | --- |")
    for result in report["results"]:
        answer = result.get("answer")
        if isinstance(answer, float):
            answer = f"{answer:.4f}"
        confidence = result.get("confidence")
        confidence = "n/a" if confidence is None else f"{confidence:.4f}"
        lines.append(
            f"| `{result['source']}` | {result['question']} | {answer} | {confidence} | {result['status']} |"
        )
    lines.append("")
    grouped = group_by_requirement([
        {"path": r["source"].rsplit(":", 1)[0], "req_ids": r["requirement_ids"], "start_line": 0, "end_line": 0, "text": ""}
        for r in report["results"]
    ])
    lines.append(f"_Grouped by requirement: {', '.join(sorted(grouped)) or 'none'}_")
    lines.append("")
    return "\n".join(lines)


if __name__ == "__main__":
    sys.exit(main())
