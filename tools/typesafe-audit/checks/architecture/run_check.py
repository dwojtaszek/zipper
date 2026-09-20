#!/usr/bin/env python3
"""Semantic architecture lint: load-file seam invariants (advisory).

Critical Rule 5 makes docs/architecture.md a contract — notably the load-file
`composer -> serializer -> emitter` seam and the EDRM-XML carve-out. This check
adds bounded Jev judgments for the parts that are awkward to express
mechanically, on PRs that actually touch the seam:

- `seam_bypass` — does the diff make a format write output outside the seam?
- `diagram_stale` — does the diff make the architecture diagrams inaccurate
  without updating docs/architecture.md in the same diff?

Deterministic prechecks run before any model call: no seam file touched ->
neutral exit 0; `src/LoadFiles/XmlLoadFileWriter.cs` (the carve-out) changed
without `docs/architecture.md` in the same diff -> a needs-human-review row
without a model call. Advisory: this check never gates.

Exit codes: 0 ok/advisory, 2 configuration or input error, 3 remote failure.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import os
import subprocess
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[2]
CHECK_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOL_DIR.parents[1]
sys.path.insert(0, str(TOOL_DIR))

# Bootstrap: checks_common must load inline before its helpers are available.
_spec = importlib.util.spec_from_file_location("tsa_checks_common", TOOL_DIR / "checks_common.py")
checks_common = importlib.util.module_from_spec(_spec)
sys.modules["tsa_checks_common"] = checks_common
_spec.loader.exec_module(checks_common)

runner = checks_common.load_module("tsa_runner", TOOL_DIR / "runner.py")

EXIT_OK = runner.EXIT_OK
EXIT_INPUT_ERROR = runner.EXIT_INPUT_ERROR
EXIT_REMOTE_ERROR = runner.EXIT_REMOTE_ERROR

QUESTIONS_PATH = CHECK_DIR / "questions.json"
POLICY_PATH = CHECK_DIR / "policy.json"
ARCHITECTURE_DOC = REPO_ROOT / "docs" / "architecture.md"

SEAM_PREFIX = "src/LoadFiles/"
CARVE_OUT_FILE = "src/LoadFiles/XmlLoadFileWriter.cs"
ARCHITECTURE_DOC_REL = "docs/architecture.md"


def watched_files(changed: list[str]) -> list[str]:
    """Seam files this check cares about, in a stable order."""
    return sorted(
        f for f in changed
        if f.startswith(SEAM_PREFIX) or f == ARCHITECTURE_DOC_REL
    )


def carve_out_without_doc_update(changed: list[str]) -> bool:
    """EDRM carve-out touched without an architecture.md update in the same
    diff — deterministically needs-human-review (Critical Rule 5), no model
    call needed for this signal."""
    return CARVE_OUT_FILE in changed and ARCHITECTURE_DOC_REL not in changed


def evidence_key_for(watched: list[str]) -> str:
    """Stable request-key namespace: sha256 of the watched set, so distinct
    watched sets never share a key (recorded fixtures stay unambiguous)."""
    digest = hashlib.sha256("\n".join(watched).encode("utf-8")).hexdigest()[:12]
    return f"seam{checks_common.SPAN_SEPARATOR}{digest}"


def changed_files(repo_root: Path, base: str, head: str) -> list[str]:
    proc = subprocess.run(
        ["git", "-C", str(repo_root), "diff", "--name-only", f"{base}...{head}"],
        capture_output=True, text=True, check=False,
    )
    if proc.returncode != 0:
        print(f"architecture-lint: input error: git diff failed: {proc.stderr}", file=sys.stderr)
        sys.exit(EXIT_INPUT_ERROR)
    return [line for line in proc.stdout.splitlines() if line.strip()]


def file_diff(repo_root: Path, base: str, head: str, path: str, limit: int) -> str:
    proc = subprocess.run(
        ["git", "-C", str(repo_root), "diff", f"{base}...{head}", "--", path],
        capture_output=True, text=True, check=False,
    )
    if proc.returncode != 0:
        print(f"architecture-lint: input error: git diff failed for {path}: {proc.stderr}", file=sys.stderr)
        sys.exit(EXIT_INPUT_ERROR)
    data = proc.stdout.encode("utf-8")
    if len(data) <= limit:
        return proc.stdout
    return data[:limit].decode("utf-8", errors="ignore") + "\n... [truncated]"


def build_evidence(repo_root: Path, base: str, head: str, watched: list[str], policy: dict) -> dict:
    """Bounded evidence: watched diff hunks plus the architecture excerpt."""
    total = 0
    hunks: list[dict] = []
    for path in watched:
        diff = file_diff(repo_root, base, head, path, policy["max_file_diff_bytes"])
        total += len(diff.encode("utf-8"))
        if total > policy["max_total_diff_bytes"]:
            print(
                f"architecture-lint: input error: seam diff evidence exceeds "
                f"{policy['max_total_diff_bytes']} bytes; narrow the diff range.",
                file=sys.stderr,
            )
            sys.exit(EXIT_INPUT_ERROR)
        hunks.append({"path": path, "diff": diff})
    doc_text = ""
    if ARCHITECTURE_DOC.exists():
        doc_text = ARCHITECTURE_DOC.read_text(encoding="utf-8", errors="replace")[: policy["max_doc_bytes"]]
    else:
        print(f"architecture-lint: input error: {ARCHITECTURE_DOC} not found.", file=sys.stderr)
        sys.exit(EXIT_INPUT_ERROR)
    return {
        "changed_files": watched,
        "diff_hunks": hunks,
        "architecture_doc": doc_text,
    }


def build_questions(questions: dict, evidence_key: str) -> dict:
    namespaced = {}
    for qid, question in questions.items():
        namespaced[f"{evidence_key}#{qid}"] = question
    return namespaced


def _answer_value(answer: dict, qtype: str):
    """Extract the answer value for the question type (choice/noul/score)."""
    if qtype == "choice":
        return answer.get("choice")
    if qtype == "score":
        return answer.get("score")
    return answer.get("noul")


def _build_rows(questions: dict, raw: dict, evidence_key: str, policy: dict) -> list[dict] | None:
    """Answer rows with the shared status policy; None on a missing answer."""
    rows = []
    for qid in questions:
        answer = raw.get("answers", {}).get(f"{evidence_key}#{qid}")
        if answer is None:
            print(f"architecture-lint: input error: response missing answer '{evidence_key}#{qid}'.", file=sys.stderr)
            return None
        qtype = questions[qid]["type"]
        row = {
            "question": qid,
            "type": qtype,
            "answer": _answer_value(answer, qtype),
            "probabilities": answer.get("probabilities"),
            "confidence": answer.get("confidence"),
        }
        row["status"] = checks_common.status_for(qid, row, policy)
        rows.append(row)
    return rows


def render_markdown(rows: list[dict], watched: list[str], carve_out_review: bool) -> str:
    lines = ["# Architecture lint (advisory)", ""]
    lines.append(f"- Seam files in this diff: {len(watched)}")
    lines.append(f"- EDRM carve-out without architecture.md update: {'yes' if carve_out_review else 'no'}")
    lines.append("")
    lines.append("| Question | Answer | Confidence | Status |")
    lines.append("| --- | --- | --- | --- |")
    for row in rows:
        answer = row["answer"]
        if isinstance(answer, float):
            answer = f"{answer:.4f}"
        confidence = row["confidence"]
        confidence = "n/a" if confidence is None else f"{confidence:.4f}"
        lines.append(f"| `{row['question']}` | {answer} | {confidence} | {row['status']} |")
    lines.append("")
    for row in rows:
        if row["status"] == "finding":
            lines.append(f"- **{row['question']}**: finding — review the seam diff against docs/architecture.md (Critical Rule 5).")
        elif row["status"] == "needs-human-review":
            lines.append(f"- **{row['question']}**: needs-human-review.")
    if carve_out_review:
        lines.append(
            "- **carve_out_update**: needs-human-review — `src/LoadFiles/XmlLoadFileWriter.cs` "
            "changed without `docs/architecture.md`; Critical Rule 5 requires an explicit "
            "maintainer approval and a same-PR diagram update for carve-out changes."
        )
    lines.append("")
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Semantic architecture lint (advisory).")
    parser.add_argument("--base", required=True, help="Diff base ref (PR base SHA).")
    parser.add_argument("--head", default="HEAD", help="Diff head ref.")
    parser.add_argument("--mode", choices=("live", "fixture"), default="fixture")
    parser.add_argument("--fixture-dir", type=Path, default=CHECK_DIR / "fixtures")
    parser.add_argument("--json-out", type=Path, required=True)
    parser.add_argument("--md-out", type=Path, required=True)
    parser.add_argument("--summary-out", type=Path, help="Append the Markdown report to this file.")
    args = parser.parse_args(argv)

    policy = checks_common.load_json(POLICY_PATH, label="architecture-lint")
    questions = checks_common.load_json(QUESTIONS_PATH, label="architecture-lint")["questions"]
    config = runner.load_config(runner.DEFAULT_CONFIG)

    # --- Deterministic prechecks: fail or skip before any model call. ---
    changed = changed_files(REPO_ROOT, args.base, args.head)
    watched = watched_files(changed)
    if not watched:
        summary = (
            "# Architecture lint (advisory)\n\n"
            "No load-file seam files (`src/LoadFiles/**` or `docs/architecture.md`) "
            "touched by this diff — no judgment needed, no model call made.\n"
        )
        checks_common.write_outputs(
            args.json_out, args.md_out, args.summary_out,
            {"mode": "neutral", "watched_files": []}, summary,
        )
        print("architecture-lint: no seam files touched; neutral skip.")
        return EXIT_OK

    carve_out_review = carve_out_without_doc_update(changed)
    if carve_out_review:
        # Deterministic short-circuit (no model call): a carve-out change
        # without a same-diff docs/architecture.md update always needs
        # explicit human approval (Critical Rule 5) — judging the seam on
        # top would not change that outcome.
        report = {
            "mode": "carve-out-review",
            "watched_files": watched,
            "carve_out_needs_review": True,
            "answers": [],
        }
        markdown = render_markdown([], watched, True)
        checks_common.write_outputs(args.json_out, args.md_out, args.summary_out, report, markdown)
        print("architecture-lint: carve-out without same-diff architecture.md update — needs-human-review (advisory only; no model call).")
        return EXIT_OK

    evidence = build_evidence(REPO_ROOT, args.base, args.head, watched, policy)
    evidence_key = evidence_key_for(watched)

    # --- TypeSafe judgment (one bounded request over the seam evidence). ---
    api_key = os.environ.get("TYPESAFE_API_KEY", "") if args.mode == "live" else ""
    if args.mode == "live" and not api_key:
        print("architecture-lint: input error: TYPESAFE_API_KEY must be set for live mode.", file=sys.stderr)
        return EXIT_INPUT_ERROR
    request = {
        "state": evidence,
        "model": config["model"],
        "questions": build_questions(questions, evidence_key),
    }
    if args.mode == "fixture":
        raw = dict(runner.run_fixture(request, Path(args.fixture_dir)))
    else:
        raw = dict(runner.run_live(request, config, api_key, [api_key]))

    rows = _build_rows(questions, raw, evidence_key, policy)
    if rows is None:
        return EXIT_INPUT_ERROR

    report = {
        "mode": raw.get("_mode", args.mode),
        "watched_files": watched,
        "carve_out_needs_review": carve_out_review,
        "answers": rows,
    }
    markdown = render_markdown(rows, watched, carve_out_review)
    checks_common.write_outputs(args.json_out, args.md_out, args.summary_out, report, markdown)

    findings = [r for r in rows if r["status"] == "finding"]
    reviews = [r for r in rows if r["status"] == "needs-human-review"] + ([{"question": "carve_out_update"}] if carve_out_review else [])
    print(
        f"architecture-lint: {len(rows)} judgment(s), {len(findings)} finding(s), "
        f"{len(reviews)} needs-human-review (advisory only)."
    )
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
