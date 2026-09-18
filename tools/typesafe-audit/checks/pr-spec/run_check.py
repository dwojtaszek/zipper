#!/usr/bin/env python3
"""PR specification compliance side check (#958) — advisory.

Deterministic collection first (explicit issue refs, affected REQ IDs, policy
excerpts, size budgets), then batched TypeSafe judgments per scoped obligation:
implementation Choice (implemented/partial/contradicted/not_addressed/ambiguous),
real-outcome-test and unrelated-behavior Nouls, and docs-sync Choice. Every
result traces to issue/requirement/diff evidence; the newest explicitly flagged
spec item supersedes older text deterministically. A PR with no explicit spec
yields a neutral `no-explicit-spec` result, never a failure. This check never
emits exit 1; service failures are exit 3, input errors exit 2.
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
import checks_common  # noqa: E402
import collect  # noqa: E402

EXIT_OK = runner.EXIT_OK
EXIT_INPUT_ERROR = runner.EXIT_INPUT_ERROR
EXIT_REMOTE_ERROR = runner.EXIT_REMOTE_ERROR

QUESTIONS_PATH = CHECK_DIR / "questions.json"
POLICY_PATH = CHECK_DIR / "policy.json"
SPAN_SEPARATOR = checks_common.SPAN_SEPARATOR


def load_json(path: Path) -> dict:
    return checks_common.load_json(path, label="pr-spec-audit")


def status_for(question_id: str, row: dict, policy: dict) -> str:
    return checks_common.status_for(question_id, row, policy)


def _excerpt(text: str, budget: int) -> str:
    return text if len(text) <= budget else text[:budget] + "\n... [truncated]"


def _find_req_definition(req_id: str, repo_root: Path | None) -> str | None:
    if not repo_root:
        return None
    requirements_path = repo_root / "Requirements.md"
    if not requirements_path.exists():
        return None
    prefix = f"**{req_id}**"
    for line in requirements_path.read_text(encoding="utf-8").splitlines():
        if line.lstrip().startswith(prefix):
            return line
    return None


def _collect_issue_texts(issues: dict | None) -> dict[int, str]:
    if not issues:
        return {}
    texts: dict[int, str] = {}
    for number, entry in issues.items():
        comments = entry.get("comments") or []
        body = entry.get("body", "")
        comments_body = "\n".join(c.get("body", "") for c in comments)
        texts[int(number)] = f"{body}\n{comments_body}"
    return texts


def build_obligations(case: dict, policy: dict, repo_root: Path | None = None) -> tuple[list[dict], str]:
    """Scoped obligations from the collected PR context + a spec_status.

    One obligation per explicit issue reference (spec compliance) and one per
    affected requirement (REQ conformance). Empty refs => no-explicit-spec.
    """
    body = case.get("body", "")
    files = collect.filter_files(case.get("files", []), max_files=policy["max_files"])
    issue_texts = _collect_issue_texts(case.get("issues"))
    refs = collect.extract_issue_refs(body)
    req_ids = case.get("req_ids") or collect.affected_req_ids(files, body, issue_texts, repo_root or Path.cwd())

    if not refs and not req_ids:
        return [], "no-explicit-spec"

    spec = collect.spec_evidence(case.get("issues") or {}, refs)
    # Repository policy excerpts only when a repo root is supplied (CI/live);
    # corpus cases stay self-contained so recorded fixtures never rot when
    # policy docs change.
    has_policy = bool(repo_root and (repo_root / "AGENTS.md").exists())
    policy_excerpts = collect.policy_evidence(repo_root, policy["per_file_policy_bytes"]) if has_policy else []

    obligations: list[dict] = []
    for ref in refs:
        obligations.append({"key": f"issue-{ref}", "kind": "issue", "ref": ref, "spec": spec, "files": files, "policy": policy_excerpts})
    inline_requirements = case.get("requirements") or {}
    for req_id in req_ids:
        definition = inline_requirements.get(req_id) or _find_req_definition(req_id, repo_root)
        obligations.append({
            "key": f"req-{req_id}", "kind": "requirement", "ref": req_id,
            "requirement_text": _excerpt(definition, policy["evidence_excerpt_bytes"]) if definition else None,
            "files": files, "policy": policy_excerpts,
        })
    return obligations, "ok"


def build_state(obligation: dict, policy: dict) -> dict:
    """Evidence spans are prepared by code; TypeSafe only judges them."""
    state: dict = {
        "obligation": {"key": obligation["key"], "kind": obligation["kind"], "ref": obligation["ref"]},
        "changed_files": [
            {"path": f["path"], "patch": _excerpt(f.get("patch", ""), policy["evidence_excerpt_bytes"])}
            for f in obligation["files"]
        ],
        "policy_excerpts": obligation["policy"],
    }
    if obligation["kind"] == "issue":
        state["spec_evidence"] = [
            {
                "order": e["order"],
                "source": e["source"],
                "supersedes_older": e["supersedes_older"],
                "text": _excerpt(e["text"], policy["evidence_excerpt_bytes"]),
            }
            for e in obligation["spec"]
        ]
    else:
        state["requirement_text"] = obligation["requirement_text"]
    encoded = json.dumps(state, sort_keys=True).encode("utf-8")
    if len(encoded) > policy["max_state_bytes"]:
        print(f"pr-spec-audit: input error: state for {obligation['key']} is {len(encoded)} bytes (limit {policy['max_state_bytes']}).", file=sys.stderr)
        sys.exit(EXIT_INPUT_ERROR)
    return state


def build_request(batch: list[dict], questions: dict, policy: dict, config: dict) -> dict:
    """One namespaced TypeSafe request for a batch of obligations.

    Shared by audit_batch and the corpus recorder so the hashed request can
    never drift between them (review finding, #958).
    """
    namespaced = {}
    for obligation in batch:
        for qid, question in questions.items():
            namespaced[f"{obligation['key']}{SPAN_SEPARATOR}{qid}"] = question
    return {
        "state": {obligation["key"]: build_state(obligation, policy) for obligation in batch},
        "model": config["model"],
        "questions": namespaced,
    }


def audit_batch(batch: list[dict], questions: dict, policy: dict, args, api_key: str, secrets: list[str]) -> list[dict]:
    config = runner.load_config(runner.DEFAULT_CONFIG)
    request = build_request(batch, questions, policy, config)
    obligation_by_key: dict[str, dict] = {}
    for obligation in batch:
        for qid in questions:
            obligation_by_key[f"{obligation['key']}{SPAN_SEPARATOR}{qid}"] = obligation

    if args.mode == "fixture":
        raw = dict(runner.run_fixture(request, Path(args.fixture_dir)))
        raw["_mode"] = "fixture"
    else:
        if not api_key:
            print("pr-spec-audit: input error: TYPESAFE_API_KEY must be set for live mode.", file=sys.stderr)
            sys.exit(EXIT_INPUT_ERROR)
        raw = dict(runner.run_live(request, config, api_key, secrets))
        raw["_mode"] = "live"

    grouped: dict[str, dict] = {}
    for key, obligation in obligation_by_key.items():
        qid = key.rsplit(SPAN_SEPARATOR, 1)[1]
        question = questions[qid]
        answer = raw.get("answers", {}).get(key)
        if answer is None:
            print(f"pr-spec-audit: input error: response missing answer '{key}'.", file=sys.stderr)
            sys.exit(EXIT_INPUT_ERROR)
        if question["type"] == "choice":
            value = answer.get("choice")
        elif question["type"] == "noul":
            value = answer.get("noul")
        else:
            value = answer.get("score")
        row = {
            "question": qid,
            "type": question["type"],
            "answer": value,
            "probabilities": answer.get("probabilities"),
            "confidence": answer.get("confidence"),
        }
        row["status"] = status_for(qid, row, policy)
        entry = grouped.setdefault(obligation["key"], {"key": obligation["key"], "kind": obligation["kind"], "ref": obligation["ref"], "answers": {}})
        entry["answers"][qid] = row
    return list(grouped.values())


def render_markdown(report: dict) -> str:
    lines = ["# PR Specification Compliance Report", ""]
    lines.append(f"- Mode: {report['mode']}")
    lines.append(f"- Runner: v{report['runner_version']}")
    lines.append(f"- Spec status: {report['spec_status']}")
    lines.append("")
    for result in report["results"]:
        if "key" not in result:
            lines.append(f"## PR #{result['pr']}: no explicit spec ({result['spec_status']})")
            lines.append("")
            continue
        lines.append(f"## Obligation `{result['key']}` ({result['kind']} {result['ref']})")
        lines.append("")
        lines.append("| Question | Answer | Confidence | Status |")
        lines.append("| --- | --- | --- | --- |")
        for qid, row in sorted(result["answers"].items()):
            answer = row.get("answer")
            if isinstance(answer, float):
                answer = f"{answer:.4f}"
            confidence = row.get("confidence")
            confidence = "n/a" if confidence is None else f"{confidence:.4f}"
            lines.append(f"| {qid} | {answer} | {confidence} | {row['status']} |")
        lines.append("")
    lines.append("_Advisory only: findings never gate merges._")
    lines.append("")
    return "\n".join(lines)


def _load_cases(args: argparse.Namespace) -> list[tuple[str, dict]] | None:
    if args.corpus:
        cases_dir = args.corpus / "cases"
        if not cases_dir.is_dir():
            print(f"pr-spec-audit: input error: no cases/ under {args.corpus}.", file=sys.stderr)
            return None
        return [(path.stem, collect.load_pr_json(path)) for path in sorted(cases_dir.glob("*.json"))]
    if args.pr_json:
        return [(args.pr_json.stem, collect.load_pr_json(args.pr_json))]
    print("pr-spec-audit: input error: one of --pr-json or --corpus is required.", file=sys.stderr)
    return None


def _audit_single_case(
    case_name: str,
    case: dict,
    policy: dict,
    args: argparse.Namespace,
    questions: dict,
    repo_root: Path | None,
    api_key: str,
    secrets: list[str],
) -> tuple[list[dict], bool]:
    obligations, status = build_obligations(case, policy, None if args.corpus else repo_root)
    if status == "no-explicit-spec":
        pr_id = case.get("pr", case_name)
        return [{"pr": pr_id, "spec_status": "no-explicit-spec", "answers": {}}], True
    case_results: list[dict] = []
    batch_size = policy["batch_size"]
    for start in range(0, len(obligations), batch_size):
        batch = obligations[start:start + batch_size]
        case_results.extend(audit_batch(batch, questions, policy, args, api_key, secrets))
    for entry in case_results:
        entry["pr"] = case.get("pr", case_name)
    return case_results, False


def _count_findings(results: list[dict]) -> tuple[int, bool]:
    findings = 0
    needs_review = False
    for r in results:
        for row in r.get("answers", {}).values():
            if row.get("status") == "finding":
                findings += 1
            elif row.get("status") == "needs-human-review":
                needs_review = True
    return findings, needs_review


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="PR specification compliance side check (advisory).")
    parser.add_argument("--pr-json", type=Path, help="Collected PR context JSON (live mode in CI).")
    parser.add_argument("--corpus", type=Path, help="Evaluation corpus dir with cases/*.json (skips --pr-json).")
    parser.add_argument("--mode", choices=("live", "fixture"), default="fixture")
    parser.add_argument("--fixture-dir", type=Path, default=CHECK_DIR / "fixtures")
    parser.add_argument("--json-out", type=Path, required=True)
    parser.add_argument("--md-out", type=Path, required=True)
    parser.add_argument("--summary-out", type=Path, help="Append the Markdown report to this file (CI job summary).")
    args = parser.parse_args(argv)

    policy = load_json(POLICY_PATH)
    questions = load_json(QUESTIONS_PATH)["questions"]
    config = runner.load_config(runner.DEFAULT_CONFIG)
    api_key = os.environ.get("TYPESAFE_API_KEY", "") if args.mode == "live" else ""
    secrets = [api_key] if api_key else []

    cases = _load_cases(args)
    if cases is None:
        return EXIT_INPUT_ERROR

    repo_root = Path.cwd()
    results: list[dict] = []
    no_spec_seen = False
    for case_name, case in cases:
        case_results, is_no_spec = _audit_single_case(
            case_name, case, policy, args, questions, repo_root, api_key, secrets
        )
        if is_no_spec:
            no_spec_seen = True
        results.extend(case_results)

    findings, needs_review = _count_findings(results)
    is_single_no_spec = no_spec_seen and len(cases) == 1
    report = {
        "runner_version": config["runner_version"],
        "mode": args.mode,
        "spec_status": "no-explicit-spec" if is_single_no_spec else "ok",
        "results": results,
        "needs_human_review": needs_review,
    }

    args.json_out.parent.mkdir(parents=True, exist_ok=True)
    args.json_out.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    markdown = render_markdown(report)
    args.md_out.write_text(markdown, encoding="utf-8")
    if args.summary_out:
        with open(args.summary_out, "a", encoding="utf-8") as fh:
            fh.write(markdown)

    print(f"pr-spec-audit: {len(results)} obligation result(s), {findings} finding(s) (advisory only).")
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
