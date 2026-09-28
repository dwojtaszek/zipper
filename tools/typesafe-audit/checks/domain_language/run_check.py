#!/usr/bin/env python3
"""Semantic domain language lint check (advisory).

Critical Rule 1 mandates canonical terminology from UBIQUITOUS_LANGUAGE.md for
all code, comments, documentation, and reviews. This check adds bounded Jev
judgments to classify text/diffs as canonical, non-canonical alias, or clean.

Exit codes: 0 ok/advisory, 2 configuration or input error, 3 remote failure.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import re
import subprocess
import sys
from pathlib import Path

TOOL_DIR = Path(__file__).resolve().parents[2]
CHECK_DIR = Path(__file__).resolve().parent
REPO_ROOT = TOOL_DIR.parents[1]
sys.path.insert(0, str(TOOL_DIR))

# Bootstrap checks_common
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

KNOWN_ALIASES = re.compile(r"\b(rolling sets?|target zip size|redacted mode|audit logs?)\b", re.IGNORECASE)


def detect_known_aliases(text: str) -> list[str]:
    """Deterministic regex precheck for common prohibited non-canonical terms."""
    matches = KNOWN_ALIASES.findall(text)
    return sorted(list(set(m.lower() for m in matches)))


def extract_diff_text(repo_root: Path, base: str, head: str, max_bytes: int) -> str:
    proc = subprocess.run(
        ["git", "-C", str(repo_root), "diff", f"{base}...{head}", "--", "*.cs", "*.md", "*.txt"],
        capture_output=True, text=True, check=False,
    )
    if proc.returncode != 0:
        print(f"domain-language-lint: input error: git diff failed: {proc.stderr}", file=sys.stderr)
        sys.exit(EXIT_INPUT_ERROR)
    data = proc.stdout.encode("utf-8")
    if len(data) <= max_bytes:
        return proc.stdout
    return data[:max_bytes].decode("utf-8", errors="ignore") + "\n... [truncated]"


def status_for_choice(choice: str, confidence: float | None, policy: dict) -> str:
    threshold = policy.get("confidence_threshold", 0.7)
    if choice in policy.get("finding_choices", {}).get("canonical_terminology", []):
        return "finding"
    if choice == "ambiguous" or (confidence is not None and confidence < threshold):
        return "needs-human-review"
    return "ok"


def render_markdown(results: list[dict]) -> str:
    lines = ["# Domain Language Lint Report (Advisory)", ""]
    lines.append("| Case / Scope | Choice | Confidence | Status | Aliases Detected |")
    lines.append("| --- | --- | --- | --- | --- |")
    for r in results:
        conf = r.get("confidence")
        conf_str = "n/a" if conf is None else f"{conf:.4f}"
        aliases = ", ".join(r.get("detected_aliases", [])) or "none"
        lines.append(f"| `{r['name']}` | `{r['choice']}` | {conf_str} | {r['status']} | {aliases} |")
    lines.append("")
    findings = [r for r in results if r["status"] == "finding"]
    if findings:
        lines.append("### Findings")
        for f in findings:
            lines.append(f"- **{f['name']}**: uses non-canonical domain terminology. Review against UBIQUITOUS_LANGUAGE.md.")
        lines.append("")
    return "\n".join(lines)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Semantic domain language lint (advisory).")
    parser.add_argument("--base", default=None, help="Diff base ref (PR base SHA).")
    parser.add_argument("--head", default="HEAD", help="Diff head ref.")
    parser.add_argument("--corpus", type=Path, default=None, help="Evaluate every case in this corpus directory.")
    parser.add_argument("--text-file", type=Path, default=None, help="Evaluate a single text file directly.")
    checks_common.add_report_arguments(parser, CHECK_DIR / "fixtures", "Append Markdown report to this file.")
    args = parser.parse_args(argv)

    if not args.base and not args.corpus and not args.text_file:
        print("domain-language-lint: input error: one of --base, --corpus, or --text-file is required.", file=sys.stderr)
        return EXIT_INPUT_ERROR

    policy = checks_common.load_json(POLICY_PATH, label="domain-language-lint")
    questions = checks_common.load_json(QUESTIONS_PATH, label="domain-language-lint")["questions"]
    config = runner.load_config(runner.DEFAULT_CONFIG)

    api_key = os.environ.get("TYPESAFE_API_KEY", "") if args.mode == "live" else ""
    if args.mode == "live" and not api_key:
        print("domain-language-lint: input error: TYPESAFE_API_KEY must be set for live mode.", file=sys.stderr)
        return EXIT_INPUT_ERROR
    secrets = [api_key] if api_key else []

    cases_to_evaluate: list[tuple[str, str]] = []

    if args.corpus:
        case_files = sorted((args.corpus / "cases").glob("*.json"))
        if not case_files:
            print(f"domain-language-lint: input error: no cases found in {args.corpus / 'cases'}", file=sys.stderr)
            return EXIT_INPUT_ERROR
        for cpath in case_files:
            cdata = json.loads(cpath.read_text(encoding="utf-8"))
            cname = cdata.get("case", cpath.stem)
            cases_to_evaluate.append((cname, cdata.get("text", "")))
    elif args.text_file:
        if not args.text_file.is_file():
            print(f"domain-language-lint: input error: {args.text_file} not found.", file=sys.stderr)
            return EXIT_INPUT_ERROR
        cases_to_evaluate.append((args.text_file.name, args.text_file.read_text(encoding="utf-8", errors="replace")))
    else:
        diff_text = extract_diff_text(REPO_ROOT, args.base, args.head, policy.get("max_total_diff_bytes", 200000))
        if not diff_text.strip():
            summary = "# Domain Language Lint (advisory)\n\nNo text or code diff to inspect — neutral skip.\n"
            checks_common.write_outputs(args.json_out, args.md_out, args.summary_out, {"mode": "neutral", "results": []}, summary)
            print("domain-language-lint: no diff; neutral skip.")
            return EXIT_OK
        cases_to_evaluate.append(("diff", diff_text))

    results: list[dict] = []
    for name, text in cases_to_evaluate:
        detected = detect_known_aliases(text)
        state = {"text": text}
        request = {
            "state": state,
            "model": config["model"],
            "questions": questions,
        }

        if args.mode == "fixture":
            raw = dict(runner.run_fixture(request, Path(args.fixture_dir)))
        else:
            raw = dict(runner.run_live(request, config, api_key, secrets))

        raw_answers = raw.get("answers")
        if not isinstance(raw_answers, dict) or "canonical_terminology" not in raw_answers:
            print("domain-language-lint: input error: response missing 'canonical_terminology' answer.", file=sys.stderr)
            return EXIT_INPUT_ERROR
        ans = raw_answers["canonical_terminology"]
        if not isinstance(ans, dict) or "choice" not in ans:
            print("domain-language-lint: input error: answer missing 'choice' field.", file=sys.stderr)
            return EXIT_INPUT_ERROR
        choice = ans["choice"]
        conf = ans.get("confidence")
        status = status_for_choice(choice, conf, policy)

        results.append({
            "name": name,
            "choice": choice,
            "confidence": conf,
            "probabilities": ans.get("probabilities", {}),
            "status": status,
            "detected_aliases": detected,
        })

    findings_count = sum(1 for r in results if r["status"] == "finding")
    reviews_count = sum(1 for r in results if r["status"] == "needs-human-review")

    report = {
        "mode": args.mode,
        "results": results,
        "total_findings": findings_count,
        "total_needs_review": reviews_count,
    }
    markdown = render_markdown(results)
    checks_common.write_outputs(args.json_out, args.md_out, args.summary_out, report, markdown)

    print(
        f"domain-language-lint: {len(results)} evaluated, {findings_count} finding(s), "
        f"{reviews_count} needs-human-review (advisory only)."
    )
    return EXIT_OK


if __name__ == "__main__":
    sys.exit(main())
