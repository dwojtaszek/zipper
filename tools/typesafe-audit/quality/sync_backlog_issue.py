#!/usr/bin/env python3
"""Merge TypeSafe quality shard rankings and maintain one pinned backlog issue (#959)."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys
import tempfile
from pathlib import Path

BACKLOG_MARKER = "<!-- typesafe-quality-backlog -->"
BACKLOG_TITLE = "[Quality Backlog] Top mutation & coverage gaps"
DEFAULT_LABELS = ("quality", "P2")


def load_shard_reports(reports_dir: Path | None, explicit_reports: list[Path]) -> list[dict]:
    paths: list[Path] = []
    if reports_dir is not None and reports_dir.exists():
        paths.extend(sorted(reports_dir.rglob("ranking.json")))
    for path in explicit_reports:
        if path.exists() and path not in paths:
            paths.append(path)

    reports: list[dict] = []
    for path in paths:
        with open(path, "r", encoding="utf-8") as fh:
            reports.append(json.load(fh))
    return reports


def merge_ranked_candidates(reports: list[dict], top_n: int = 15) -> list[dict]:
    """Merge and deduplicate ranked candidates across shards."""
    deduped: dict[tuple[str, str, str, str, str], dict] = {}
    for report in reports:
        for item in report.get("ranked", []):
            key = (
                str(item.get("origin", "")),
                str(item.get("category", "")),
                str(item.get("file", "")),
                str(item.get("method", "")),
                str(item.get("lines", "")),
            )
            existing = deduped.get(key)
            if existing is None or float(item.get("priority", 0.0)) > float(existing.get("priority", 0.0)):
                deduped[key] = item

    merged = list(deduped.values())
    merged.sort(key=lambda c: (-float(c.get("priority", 0.0)), str(c.get("file", "")), str(c.get("method", ""))))
    return merged[:top_n]


def _format_scores(components: dict) -> str:
    order = ("correctness", "data_loss", "security", "regression")
    parts = [f"{name[0].upper()}:{float(components[name]):.2f}" for name in order if name in components]
    return " ".join(parts) if parts else "-"


def render_issue_body(
    top_items: list[dict],
    total_merged: int,
    sha: str,
    run_url: str,
) -> str:
    lines = [
        BACKLOG_MARKER,
        "## Quality Prioritization Backlog",
        "",
        "Automatically updated from `typesafe-quality-audit` (deterministic Coverlet coverage gaps + Stryker surviving mutants ranked by TypeSafe risk weights).",
        "",
    ]
    if sha:
        lines.append(f"- **Commit:** `{sha[:12]}`")
    if run_url:
        lines.append(f"- **Workflow run:** {run_url}")
    lines.append(f"- **Showing:** top {len(top_items)} of {total_merged} unique candidates")
    lines.append("")
    lines.append("| Priority | Origin | Category | Target | REQ IDs | Scores (`C/D/S/R`) |")
    lines.append("| --- | --- | --- | --- | --- | --- |")
    for item in top_items:
        reqs = ", ".join(item.get("req_ids") or []) or "-"
        target = f"`{item.get('file', '')}:{item.get('lines', '')} {item.get('method', '')}`"
        scores = _format_scores(item.get("components") or {})
        lines.append(
            f"| {float(item.get('priority', 0.0)):.4f} | {item.get('origin', '')} | {item.get('category', '')} | "
            f"{target} | {reqs} | {scores} |"
        )
    lines.append("")
    lines.append("_Deterministic tools (Coverlet & Stryker) own coverage and mutant status; TypeSafe only prioritizes risk._")
    lines.append("")
    return "\n".join(lines)


def _gh_json(*args: str) -> list[dict]:
    proc = subprocess.run(["gh", *args], capture_output=True, text=True, check=True)
    data = json.loads(proc.stdout)
    return data if isinstance(data, list) else []


def find_existing_backlog_issue() -> int | None:
    issues = _gh_json(
        "issue",
        "list",
        "--state",
        "open",
        "--search",
        f'in:title "{BACKLOG_TITLE}"',
        "--limit",
        "50",
        "--json",
        "number,title,body",
    )
    matches = [
        int(issue["number"])
        for issue in issues
        if issue.get("title") == BACKLOG_TITLE
        and (issue.get("body") or "").lstrip().startswith(BACKLOG_MARKER)
    ]
    return min(matches) if matches else None


def resolve_existing_labels(desired: tuple[str, ...]) -> list[str]:
    labels = _gh_json("label", "list", "--limit", "200", "--json", "name")
    available = {str(entry.get("name", "")) for entry in labels}
    return [label for label in desired if label in available]


def sync_issue(body: str, dry_run: bool = False) -> str:
    if dry_run:
        return "dry-run"

    existing_number = find_existing_backlog_issue()
    with tempfile.NamedTemporaryFile("w", encoding="utf-8", suffix=".md", delete=False) as tf:
        tf.write(body)
        body_path = Path(tf.name)
    try:
        if existing_number is not None:
            subprocess.run(
                ["gh", "issue", "edit", str(existing_number), "--body-file", str(body_path)],
                capture_output=True,
                text=True,
                check=True,
            )
            return f"updated #{existing_number}"

        cmd = ["gh", "issue", "create", "--title", BACKLOG_TITLE, "--body-file", str(body_path)]
        for label in resolve_existing_labels(DEFAULT_LABELS):
            cmd.extend(["--label", label])
        proc = subprocess.run(cmd, capture_output=True, text=True, check=True)
        return f"created ({proc.stdout.strip()})"
    finally:
        body_path.unlink(missing_ok=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Merge quality shard rankings and sync backlog issue.")
    parser.add_argument("--reports-dir", type=Path, default=None)
    parser.add_argument("--report", type=Path, action="append", default=[])
    parser.add_argument("--top", type=int, default=15)
    parser.add_argument("--expected-shards", type=int, default=1)
    parser.add_argument("--sha", default="")
    parser.add_argument("--run-url", default="")
    parser.add_argument("--summary-out", type=Path, default=None)
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)

    reports = load_shard_reports(args.reports_dir, args.report)
    if len(reports) < args.expected_shards:
        msg = (
            f"quality-backlog-sync: found {len(reports)} ranking.json report(s), "
            f"expected {args.expected_shards}; skipping issue update."
        )
        print(msg)
        if args.summary_out:
            with open(args.summary_out, "a", encoding="utf-8") as fh:
                fh.write(f"\n### Quality Backlog Sync\n\n{msg}\n")
        return 0

    all_merged = merge_ranked_candidates(reports, top_n=10_000)
    if not all_merged:
        msg = "quality-backlog-sync: ranking reports had zero candidates; skipping issue update."
        print(msg)
        if args.summary_out:
            with open(args.summary_out, "a", encoding="utf-8") as fh:
                fh.write(f"\n### Quality Backlog Sync\n\n{msg}\n")
        return 0

    top_items = all_merged[: args.top]
    body = render_issue_body(top_items, len(all_merged), args.sha, args.run_url)
    outcome = sync_issue(body, dry_run=args.dry_run)
    print(f"quality-backlog-sync: {outcome} ({len(top_items)} item(s))")
    if args.summary_out:
        with open(args.summary_out, "a", encoding="utf-8") as fh:
            fh.write(f"\n### Quality Backlog Sync\n\n- Outcome: `{outcome}` ({len(top_items)} of {len(all_merged)} items)\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
