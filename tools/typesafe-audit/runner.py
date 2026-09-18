#!/usr/bin/env python3
"""TypeSafe audit runner (developer-only, advisory). Issue #955 foundation slice.

Sends explicitly listed repository files as shared state plus a question
definition file to the TypeSafe System One evaluation endpoint and emits
normalized JSON + Markdown findings. Live mode reads TYPESAFE_API_KEY from the
process environment only. Fixture mode replays recorded responses offline.

Exit codes:
  0  success / no gated finding
  1  policy finding (only when --strict or config.blocking is true)
  2  configuration or input error
  3  remote service failure (never blocks: see --strict)

This plumbing ships no judgments; follow-up tickets define questions.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import NoReturn

EXIT_OK = 0
EXIT_FINDING = 1
EXIT_INPUT_ERROR = 2
EXIT_REMOTE_ERROR = 3

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_CONFIG = Path(__file__).resolve().parent / "config.json"

REDACTED = "***REDACTED***"


def redact(text: str, secrets: list[str]) -> str:
    """Scrub known secrets from any error text, header dump, or log line."""
    for secret in secrets:
        if secret:
            text = text.replace(secret, REDACTED)
    return text


def load_config(path: Path) -> dict:
    try:
        with open(path, "r", encoding="utf-8") as fh:
            config = json.load(fh)
    except (OSError, json.JSONDecodeError) as exc:
        fail_input(f"Cannot read config {path}: {exc}")
    required = ("model", "endpoint", "runner_version", "max_file_bytes", "max_total_bytes", "timeout_seconds")
    missing = [key for key in required if key not in config]
    if missing:
        fail_input(f"Config {path} is missing required keys: {', '.join(missing)}")
    return config


def fail_input(message: str) -> NoReturn:
    print(f"typesafe-audit: input error: {message}", file=sys.stderr)
    sys.exit(EXIT_INPUT_ERROR)


def resolve_inputs(file_list: Path, max_file_bytes: int, max_total_bytes: int) -> list[dict]:
    """Resolve an explicit newline-separated file list against the checkout.

    Rejects paths outside the repository checkout, missing files, and files
    over the byte budget. Only files named in the list are ever read.
    """
    try:
        lines = [ln.strip() for ln in file_list.read_text(encoding="utf-8").splitlines()]
    except OSError as exc:
        fail_input(f"Cannot read file list {file_list}: {exc}")

    names = [ln for ln in lines if ln and not ln.startswith("#")]
    if not names:
        fail_input(f"File list {file_list} names no files.")

    inputs: list[dict] = []
    total = 0
    for name in names:
        path = (REPO_ROOT / name).resolve()
        if REPO_ROOT.resolve() not in path.parents and path != REPO_ROOT.resolve():
            fail_input(f"Input path escapes the checkout: {name}")
        if not path.is_file():
            fail_input(f"Input file not found: {name}")
        size = path.stat().st_size
        if size > max_file_bytes:
            fail_input(f"Input file {name} is {size} bytes (limit {max_file_bytes}).")
        total += size
        if total > max_total_bytes:
            fail_input(f"Combined input size exceeds {max_total_bytes} bytes.")
        # Single read: the hash and the content must come from the same buffer
        # so the report cannot describe bytes different from those evaluated.
        data = path.read_bytes()
        inputs.append({
            "path": name,
            "sha256": hashlib.sha256(data).hexdigest(),
            "content": data.decode("utf-8", errors="replace"),
        })
    return inputs


def load_questions(questions_path: Path) -> dict:
    try:
        with open(questions_path, "r", encoding="utf-8") as fh:
            questions = json.load(fh)
    except (OSError, json.JSONDecodeError) as exc:
        fail_input(f"Cannot read questions {questions_path}: {exc}")
    if not isinstance(questions, dict) or not questions:
        fail_input(f"Questions file {questions_path} must be a non-empty JSON object.")
    for qid, question in questions.items():
        if not isinstance(question, dict) or "type" not in question or "instructions" not in question:
            fail_input(f"Question '{qid}' must be an object with 'type' and 'instructions'.")
        if question["type"] not in ("noul", "choice", "score"):
            fail_input(f"Question '{qid}' has unsupported type '{question['type']}'.")
    return questions


def build_request(inputs: list[dict], questions: dict, model: str) -> dict:
    """Batch every independent question over the shared state in one request."""
    return {
        "state": {"files": inputs},
        "model": model,
        "questions": questions,
    }


def canonical_payload(request: dict) -> bytes:
    """Stable serialization used for fixture keys and deterministic output."""
    return json.dumps(request, sort_keys=True, separators=(",", ":")).encode("utf-8")


def request_fixture_key(request: dict) -> str:
    return hashlib.sha256(canonical_payload(request)).hexdigest()


def run_live(request: dict, config: dict, api_key: str, secrets: list[str]) -> dict:
    body = canonical_payload(request)
    req = urllib.request.Request(
        config["endpoint"],
        data=body,
        headers={
            "Authorization": f"Bearer {api_key}",
            "Content-Type": "application/json",
        },
        method="POST",
    )
    for attempt in (1, 2):
        try:
            with urllib.request.urlopen(req, timeout=config["timeout_seconds"]) as resp:
                return json.loads(resp.read().decode("utf-8"))
        except urllib.error.HTTPError as exc:
            detail = redact(exc.read().decode("utf-8", errors="replace"), secrets)
            if exc.code in (429, 529) and attempt == 1:
                time.sleep(2)
                continue
            print(f"typesafe-audit: remote error: HTTP {exc.code}: {detail}", file=sys.stderr)
            sys.exit(EXIT_REMOTE_ERROR)
        except (urllib.error.URLError, TimeoutError, json.JSONDecodeError) as exc:
            print(f"typesafe-audit: remote error: {redact(str(exc), secrets)}", file=sys.stderr)
            sys.exit(EXIT_REMOTE_ERROR)
    raise AssertionError("unreachable")


def run_fixture(request: dict, fixture_dir: Path) -> dict:
    key = request_fixture_key(request)
    fixture_path = fixture_dir / f"{key}.json"
    if not fixture_path.is_file():
        fail_input(
            f"No recorded fixture for request key {key} in {fixture_dir}. "
            "Record one with --record-fixture."
        )
    try:
        return json.loads(fixture_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail_input(f"Cannot read fixture {fixture_path}: {exc}")


def normalize_answers(raw_response: dict, questions: dict, policy: dict) -> list[dict]:
    answers = raw_response.get("answers", {})
    rows = []
    for qid, question in questions.items():
        answer = answers.get(qid)
        if answer is None:
            fail_input(f"Remote response is missing an answer for question '{qid}'.")
        row = {
            "id": qid,
            "type": question["type"],
            "instructions": question["instructions"],
        }
        if question["type"] == "noul":
            row["answer"] = answer.get("noul")
        elif question["type"] == "choice":
            row["answer"] = answer.get("choice")
            row["probabilities"] = answer.get("probabilities")
        else:
            row["answer"] = answer.get("score")
            row["probabilities"] = answer.get("probabilities")
        row["confidence"] = answer.get("confidence")
        row["is_finding"] = is_finding(row, policy)
        rows.append(row)
    return rows


def is_finding(row: dict, policy: dict) -> bool:
    """A finding is a low-confidence or no-answer outcome worth human review.

    The confidence threshold comes from config.json. The foundation slice
    defines no gated categories (block_on stays empty); follow-up tickets
    extend this predicate per judgment.
    """
    threshold = policy.get("confidence_threshold", 0.7)
    confidence = row.get("confidence")
    if confidence is None:
        # Noul answers carry no separate confidence; treat extreme answers as
        # confident and mid-range answers as review-worthy.
        answer = row.get("answer")
        if isinstance(answer, (int, float)):
            return 0.25 < answer < 0.75
        return True
    return confidence < threshold


def build_report(request: dict, raw_response: dict, rows: list[dict], inputs: list[dict], config: dict) -> dict:
    return {
        "runner_version": config["runner_version"],
        "model": raw_response.get("model", config["model"]),
        "mode": raw_response.get("_mode", "unknown"),
        "source_files": [{"path": i["path"], "sha256": i["sha256"]} for i in inputs],
        "usage": raw_response.get("usage"),
        "answers": rows,
    }


def render_markdown(report: dict) -> str:
    lines = ["# TypeSafe Audit Report", ""]
    lines.append(f"- Runner: v{report['runner_version']}")
    lines.append(f"- Model: `{report['model']}`")
    lines.append(f"- Mode: {report['mode']}")
    usage = report.get("usage") or {}
    lines.append(f"- Token usage: {usage.get('input_tokens', '?')} in / {usage.get('output_tokens', '?')} out")
    lines.append("")
    lines.append("| Question | Type | Answer | Confidence | Finding |")
    lines.append("| --- | --- | --- | --- | --- |")
    for row in report["answers"]:
        answer = row.get("answer")
        if isinstance(answer, float):
            answer = f"{answer:.4f}"
        confidence = row.get("confidence")
        confidence = "n/a" if confidence is None else f"{confidence:.4f}"
        finding = "yes" if row["is_finding"] else "no"
        lines.append(f"| `{row['id']}` | {row['type']} | {answer} | {confidence} | {finding} |")
    lines.append("")
    lines.append("| Source file | SHA-256 |")
    lines.append("| --- | --- |")
    for source in report["source_files"]:
        lines.append(f"| `{source['path']}` | `{source['sha256'][:12]}…` |")
    lines.append("")
    return "\n".join(lines)


def write_outputs(report: dict, json_out: Path, md_out: Path) -> None:
    json_out.parent.mkdir(parents=True, exist_ok=True)
    json_out.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    md_out.write_text(render_markdown(report), encoding="utf-8")


def write_summary(report: dict, summary_path: Path) -> None:
    """GitHub Actions job summary (no PR comments in the foundation slice)."""
    with open(summary_path, "a", encoding="utf-8") as fh:
        fh.write(render_markdown(report))


def record_fixture(request: dict, fixture_dir: Path, response: dict) -> Path:
    fixture_dir.mkdir(parents=True, exist_ok=True)
    path = fixture_dir / f"{request_fixture_key(request)}.json"
    path.write_text(json.dumps(response, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    return path


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="TypeSafe audit runner (advisory).")
    parser.add_argument("--config", type=Path, default=DEFAULT_CONFIG)
    parser.add_argument("--files", type=Path, required=True, help="Newline-separated explicit input file list (repo-relative).")
    parser.add_argument("--questions", type=Path, required=True, help="JSON question definitions.")
    parser.add_argument("--mode", choices=("live", "fixture"), default="fixture")
    parser.add_argument("--fixture-dir", type=Path, default=Path(__file__).resolve().parent / "fixtures")
    parser.add_argument("--json-out", type=Path, required=True)
    parser.add_argument("--md-out", type=Path, required=True)
    parser.add_argument("--summary-out", type=Path, help="Append Markdown report to this file (CI job summary).")
    parser.add_argument("--record-fixture", action="store_true", help="Live mode: also record the response as a fixture.")
    parser.add_argument("--strict", action="store_true", help="Exit 1 on policy findings (advisory by default).")
    args = parser.parse_args(argv)

    config = load_config(args.config)
    api_key = os.environ.get("TYPESAFE_API_KEY", "")
    secrets = [api_key] if api_key else []

    inputs = resolve_inputs(args.files, config["max_file_bytes"], config["max_total_bytes"])
    questions = load_questions(args.questions)
    request = build_request(inputs, questions, config["model"])

    if args.mode == "fixture":
        raw_response = dict(run_fixture(request, args.fixture_dir))
        raw_response["_mode"] = "fixture"
    else:
        if not api_key:
            print(
                "typesafe-audit: input error: TYPESAFE_API_KEY must be set in the "
                "environment for live mode (never from .env or files).",
                file=sys.stderr,
            )
            return EXIT_INPUT_ERROR
        raw_response = dict(run_live(request, config, api_key, secrets))
        raw_response["_mode"] = "live"
        if args.record_fixture:
            record_fixture(request, args.fixture_dir, raw_response)

    report = build_report(
        request,
        raw_response,
        normalize_answers(raw_response, questions, {
            "confidence_threshold": config.get("confidence_threshold", 0.7),
        }),
        inputs,
        config,
    )
    write_outputs(report, args.json_out, args.md_out)
    if args.summary_out:
        write_summary(report, args.summary_out)

    has_findings = any(row["is_finding"] for row in report["answers"])
    blocking = config.get("blocking", False) or args.strict
    if has_findings and blocking:
        print("typesafe-audit: policy finding(s) present (blocking mode).")
        return EXIT_FINDING
    if has_findings:
        print("typesafe-audit: policy finding(s) present (advisory only; not blocking).")
    else:
        print("typesafe-audit: no gated findings.")
    return EXIT_OK


if __name__ == "__main__":
    try:
        sys.exit(main())
    except SystemExit:
        raise
    except Exception as exc:  # unexpected crash: never report as a policy finding
        print(f"typesafe-audit: unexpected runner error: {redact(str(exc), [os.environ.get('TYPESAFE_API_KEY', '')])}", file=sys.stderr)
        sys.exit(EXIT_INPUT_ERROR)
