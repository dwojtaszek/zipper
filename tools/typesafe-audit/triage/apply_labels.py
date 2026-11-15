#!/usr/bin/env python3
"""Apply high-confidence TypeSafe type/subsystem labels safely (#960).

Rules:
- Only applies high-confidence (default >= 0.85) `type` and mapped `subsystem`
  labels that already exist on the repository.
- Never applies priority labels (P0-P3) or posts duplicate/comment noise.
- Never adds a `type` label if the issue already carries any type/tier label
  (prevents wave label bleed per docs/issue-selection.md).
- Skips bot-authored issues and tracker issues (Roadmap:, [Quality Backlog]).
"""

from __future__ import annotations

import argparse
import json
import math
import subprocess
import sys
from pathlib import Path

TRIAGE_DIR = Path(__file__).resolve().parent
DEFAULT_POLICY_PATH = TRIAGE_DIR / "policy.json"

EXISTING_TYPE_LABELS = frozenset({
    "bug",
    "testing",
    "requirements",
    "documentation",
    "docs",
    "performance",
    "perf",
    "security",
    "refactor",
    "enhancement",
    "quality",
    "investigation",
    "e2e",
    "critical",
    "high",
    "blocker",
})

TIER1_TYPE_LABELS = frozenset({"bug", "security"})
NON_TIER6_TYPE_LABELS = frozenset({"bug", "security", "testing", "refactor", "enhancement", "performance"})

APPLICABLE_TYPE_LABELS = frozenset({
    "bug",
    "testing",
    "requirements",
    "documentation",
    "performance",
    "security",
    "refactor",
    "enhancement",
})

SUBSYSTEM_LABEL_MAP = {
    "ci-infra": "ci-cd",
}

TRACKER_TITLE_PREFIXES = ("roadmap:", "[quality backlog]")


def _valid_confidence(conf: object) -> bool:
    return (
        isinstance(conf, (int, float))
        and not isinstance(conf, bool)
        and math.isfinite(float(conf))
        and 0.0 <= float(conf) <= 1.0
    )


def _select_type_label(judgments: dict, existing_lower: set[str], threshold: float) -> str | None:
    if existing_lower & EXISTING_TYPE_LABELS:
        return None
    type_j = judgments.get("type") or {}
    type_val = type_j.get("value")
    type_conf = type_j.get("confidence")
    if (
        type_j.get("status") != "judged"
        or not _valid_confidence(type_conf)
        or float(type_conf) < threshold
        or type_val not in APPLICABLE_TYPE_LABELS
    ):
        return None
    if type_val in TIER1_TYPE_LABELS and bool(existing_lower & {"p2", "p3"}):
        return None
    if type_val in NON_TIER6_TYPE_LABELS and "p3" in existing_lower:
        return None
    return str(type_val)


def _select_subsystem_label(judgments: dict, threshold: float) -> str | None:
    sub_j = judgments.get("subsystem") or {}
    sub_conf = sub_j.get("confidence")
    mapped_sub = SUBSYSTEM_LABEL_MAP.get(str(sub_j.get("value")))
    if (
        not mapped_sub
        or sub_j.get("status") != "judged"
        or not _valid_confidence(sub_conf)
        or float(sub_conf) < threshold
    ):
        return None
    return mapped_sub


def _filter_desired_labels(
    desired: list[str], existing_lower: set[str], available_labels: set[str] | None
) -> list[str]:
    filtered: list[str] = []
    for label in desired:
        allowed = (
            label.lower() not in existing_lower
            and (available_labels is None or label in available_labels)
            and label not in filtered
        )
        if allowed:
            filtered.append(label)
    return filtered


def select_labels_to_add(
    issue_input: dict,
    triage_report: dict,
    policy: dict,
    available_labels: set[str] | None = None,
) -> tuple[list[str], str]:
    author = str(issue_input.get("author") or "").strip().lower()
    if author.endswith("[bot]") or author.startswith("app/"):
        return [], "skipped:bot-author"

    title = str(issue_input.get("title") or "").strip().lower()
    if title.startswith(TRACKER_TITLE_PREFIXES):
        return [], "skipped:tracker-issue"

    issue_number = issue_input.get("number")
    match = next(
        (r for r in triage_report.get("results", []) if r.get("issue") == issue_number),
        None,
    )
    if not match or not match.get("judgments"):
        return [], "skipped:no-judgments"

    judgments = match["judgments"]
    threshold = float(policy.get("label_confidence_threshold", 0.85))
    existing_lower = {str(label).lower() for label in issue_input.get("labels", [])}

    desired = [
        label
        for label in (
            _select_type_label(judgments, existing_lower, threshold),
            _select_subsystem_label(judgments, threshold),
        )
        if label is not None
    ]
    filtered = _filter_desired_labels(desired, existing_lower, available_labels)
    if not filtered:
        return [], "skipped:no-qualifying-labels"
    return filtered, "ok"


def fetch_repo_labels() -> set[str]:
    proc = subprocess.run(
        ["gh", "label", "list", "--limit", "200", "--json", "name"],
        capture_output=True,
        text=True,
        check=True,
    )
    data = json.loads(proc.stdout)
    return {str(entry.get("name", "")) for entry in data if entry.get("name")}


def fetch_issue_labels(issue_number: int) -> list[str]:
    proc = subprocess.run(
        ["gh", "issue", "view", str(issue_number), "--json", "labels"],
        capture_output=True,
        text=True,
        check=True,
    )
    data = json.loads(proc.stdout)
    return [str(entry.get("name", "")) for entry in data.get("labels", []) if entry.get("name")]


def apply_labels_gh(issue_number: int, labels: list[str]) -> None:
    cmd = ["gh", "issue", "edit", str(issue_number)]
    for label in labels:
        cmd.extend(["--add-label", label])
    subprocess.run(cmd, capture_output=True, text=True, check=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Apply high-confidence TypeSafe triage labels.")
    parser.add_argument("--triage-json", type=Path, required=True)
    parser.add_argument("--issue-json", type=Path, required=True)
    parser.add_argument("--policy", type=Path, default=DEFAULT_POLICY_PATH)
    parser.add_argument("--summary-out", type=Path, default=None)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)

    if not args.triage_json.exists() or not args.issue_json.exists():
        print("triage-apply-labels: missing triage.json or issue-input.json.", file=sys.stderr)
        return 1

    triage_report = json.loads(args.triage_json.read_text(encoding="utf-8"))
    issue_input = json.loads(args.issue_json.read_text(encoding="utf-8"))
    policy = json.loads(args.policy.read_text(encoding="utf-8"))

    issue_number = int(issue_input["number"])
    if args.dry_run:
        available = None
    else:
        available = fetch_repo_labels()
        issue_input = {**issue_input, "labels": fetch_issue_labels(issue_number)}

    labels_to_add, status = select_labels_to_add(issue_input, triage_report, policy, available)

    if labels_to_add and not args.dry_run:
        apply_labels_gh(issue_number, labels_to_add)

    outcome = f"{status}: {', '.join(labels_to_add)}" if labels_to_add else status
    print(f"triage-apply-labels: issue #{issue_number} -> {outcome}")
    if args.summary_out:
        with open(args.summary_out, "a", encoding="utf-8") as fh:
            fh.write(f"\n### Triage Label Application\n\n- Issue #{issue_number}: `{outcome}`\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
