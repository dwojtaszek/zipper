#!/usr/bin/env python3
"""Check performance baseline drift, compute medians, and update baselines.

Usage:
    perf-baseline-refresh.py <results-dir> <baselines-file> [--pr-body-out PATH] [--justification TEXT] [--seed N] [--force]
"""

import argparse
import json
import os
import sys
from datetime import date
from pathlib import Path

# Add current script directory to sys.path so perf_utils can be imported
sys.path.insert(0, str(Path(__file__).resolve().parent))

from perf_utils import (
    compute_variability,
    format_context_section,
    format_variability_table,
    get_runtime_context,
    load_runs,
    median,
)

DRIFT_THRESHOLD_PCT = 5.0


def parse_args(argv):
    parser = argparse.ArgumentParser(
        description="Check performance baseline drift, compute medians, and update baselines.",
        add_help=False,
    )
    parser.add_argument("results_dir")
    parser.add_argument("baselines_file")
    parser.add_argument("--pr-body-out", default=None)
    parser.add_argument("--justification", default=os.environ.get("JUSTIFICATION", "").strip())

    default_seed = 42
    if "PERF_SEED" in os.environ and os.environ["PERF_SEED"].strip().isdigit():
        default_seed = int(os.environ["PERF_SEED"].strip())

    parser.add_argument("--seed", type=int, default=default_seed)
    parser.add_argument("--force", action="store_true")

    try:
        args = parser.parse_args(argv[1:])
    except SystemExit:
        return None

    if args.seed < 0:
        return None

    pr_body_out = args.pr_body_out or os.path.join(args.results_dir, "pr_body.md")
    return args.results_dir, args.baselines_file, pr_body_out, args.justification.strip(), args.seed, args.force


def compute_new_baselines(runs, scenarios):
    new_baselines = {}
    for sc in scenarios:
        walls = [r[sc]["wall_s"] for r in runs]
        rsss = [r[sc]["rss_kb"] for r in runs]
        new_baselines[sc] = {
            "wall_s": round(median(walls), 2),
            "rss_kb": int(round(median(rsss))),
        }
    return new_baselines


def evaluate_drift(scenarios, baselines, new_baselines):
    drifted = False
    has_increases = False
    drift_rows = []
    for sc in scenarios:
        for metric in ("wall_s", "rss_kb"):
            old = baselines[sc][metric]
            new = new_baselines[sc][metric]
            ratio = new / old if old > 0 else 0
            drift_pct = abs(ratio - 1.0) * 100
            if drift_pct >= DRIFT_THRESHOLD_PCT:
                drifted = True
            if new > old:
                has_increases = True
            drift_rows.append(f"| {sc} | {metric} | {old} | {new} | {ratio:.2f}× | {drift_pct:.1f}% |")
    return drifted, has_increases, drift_rows


def format_raw_runs(results_dir):
    raw_runs_blocks = []
    for i in range(1, 6):
        run_file = os.path.join(results_dir, f"run_{i}.json")
        if not os.path.isfile(run_file):
            continue
        try:
            with open(run_file) as f:
                content = json.dumps(json.load(f), indent=2)
            raw_runs_blocks.append(f"#### run_{i}.json\n```json\n{content}\n```")
        except Exception:
            pass
    return (
        "<details>\n<summary>Raw Measurements (run_1.json – run_5.json)</summary>\n\n"
        + "\n\n".join(raw_runs_blocks)
        + "\n\n</details>"
    )


def build_pr_body(today, seed, drift_rows, variability, context, scenarios, results_dir, has_increases, justification):
    table = "| Scenario | Metric | Old | New | Ratio | Drift |\n"
    table += "|----------|--------|-----|-----|-------|-------|\n"
    table += "\n".join(drift_rows)

    var_section = format_variability_table(variability, scenarios)
    ctx_section = format_context_section(context)

    checklist_items = [
        "- [ ] Drift is expected (infra change, runtime update, etc.)",
        "- [ ] No unintentional regression introduced",
    ]
    if has_increases:
        just_entry = (
            f"  - Recorded justification: {justification}"
            if justification
            else "  <!-- REQUIRED: One or more baseline metrics increased. Record the reason for degradation here before merging. -->"
        )
        checklist_items.append(
            "- [ ] **Recorded justification for baseline increase(s)** (REQUIRED before merge):\n" + just_entry
        )

    checklist = "\n".join(checklist_items)
    raw_runs_section = format_raw_runs(results_dir)

    return (
        f"## Perf Baseline Refresh ({today})\n\n"
        f"Baselines computed as **median of 5 runs** using `--seed {seed}`.\n\n"
        f"{table}\n\n"
        f"{var_section}\n\n"
        f"{ctx_section}\n\n"
        f"### Review Checklist\n\n"
        f"{checklist}\n\n"
        f"### Raw Run Measurements\n\n"
        f"{raw_runs_section}\n"
    )


def write_github_output(drifted, force, has_increases):
    gh_out = os.environ.get("GITHUB_OUTPUT")
    if gh_out:
        with open(gh_out, "a") as f:
            f.write(f"drifted={'true' if (drifted or force) else 'false'}\n")
            f.write(f"has_increases={'true' if has_increases else 'false'}\n")


def save_baselines(baselines_path, today, seed, context, variability, justification, scenarios, new_baselines):
    output = {
        "_captured_on": f"ubuntu-latest (5-run median, {today})",
        "_methodology": f"Run measure.sh 5x with --seed {seed}, take median(wall_s) and median(rss_kb) per scenario.",
        "_context": context,
        "_variability": variability,
    }
    if justification:
        output["_justification"] = justification

    for sc in scenarios:
        output[sc] = new_baselines[sc]

    os.makedirs(os.path.dirname(os.path.abspath(baselines_path)), exist_ok=True)
    with open(baselines_path, "w") as f:
        json.dump(output, f, indent=2)
        f.write("\n")


def main() -> int:
    parsed = parse_args(sys.argv)
    if parsed is None:
        print(
            "usage: perf-baseline-refresh.py <results-dir> <baselines-file> [--pr-body-out PATH] [--justification TEXT] [--seed N] [--force]",
            file=sys.stderr,
        )
        return 2
    results_dir, baselines_path, pr_body_out, justification, seed, force = parsed

    try:
        with open(baselines_path) as f:
            baselines = json.load(f)
    except Exception as e:
        print(f"perf-baseline-refresh: error loading baselines file: {e}", file=sys.stderr)
        return 2

    scenarios = [k for k in baselines if not k.startswith("_")]

    try:
        runs = load_runs(results_dir, scenarios)
    except FileNotFoundError as e:
        print(f"perf-baseline-refresh: missing run file: {e.filename}", file=sys.stderr)
        return 2
    except ValueError as e:
        print(f"perf-baseline-refresh: {e}", file=sys.stderr)
        return 2

    new_baselines = compute_new_baselines(runs, scenarios)
    variability = compute_variability(runs, scenarios)
    context = get_runtime_context(seed=seed)

    drifted, has_increases, drift_rows = evaluate_drift(scenarios, baselines, new_baselines)
    write_github_output(drifted, force, has_increases)

    if not drifted and not force:
        print(f"Baselines in range (< {DRIFT_THRESHOLD_PCT}% drift). No refresh needed.")
        return 0

    today = date.today().isoformat()
    save_baselines(baselines_path, today, seed, context, variability, justification, scenarios, new_baselines)

    body = build_pr_body(
        today,
        seed,
        drift_rows,
        variability,
        context,
        scenarios,
        results_dir,
        has_increases,
        justification,
    )

    if pr_body_out:
        os.makedirs(os.path.dirname(os.path.abspath(pr_body_out)), exist_ok=True)
        with open(pr_body_out, "w") as f:
            f.write(body)

    if force and not drifted:
        print("Force refresh applied (< 5% drift). Updated baselines.")
    else:
        print("Drift detected. Updating baselines.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
