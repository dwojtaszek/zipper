#!/usr/bin/env python3
"""Compare perf-measure medians against baselines (RSS gates, wall_s informs).

Usage:
    perf-compare.py <results-dir> [--table-out PATH] [--measured-out PATH]

Reads run_1..run_5.json from <results-dir>, medians per scenario, compares to
tests/perf/baselines.json. Exits 1 when any rss_kb ratio exceeds 1.20.
Prints the markdown table to stdout and appends failed=/table outputs when
GITHUB_OUTPUT is set (GitHub Actions), so perf-guard.yml can retry once
before failing.
"""

import json
import os
import sys

WALL_THRESHOLD = 1.25  # informational only — never fails the job
RSS_THRESHOLD = 1.20  # hard gate
WALL_WARN_RATIO = 1.5


def median(values):
    s = sorted(values)
    n = len(s)
    return s[n // 2] if n % 2 else (s[n // 2 - 1] + s[n // 2]) / 2


def parse_args(argv):
    if len(argv) < 2:
        return None
    results_dir = argv[1]
    table_out = None
    measured_out = os.path.join(results_dir, "measured.json")
    args = argv[2:]
    i = 0
    while i < len(args):
        if args[i] == "--table-out" and i + 1 < len(args):
            table_out = args[i + 1]
            i += 2
        elif args[i] == "--measured-out" and i + 1 < len(args):
            measured_out = args[i + 1]
            i += 2
        else:
            return None
    return results_dir, table_out, measured_out


def load_medians(results_dir, scenarios):
    runs = []
    for n in range(1, 6):
        with open(os.path.join(results_dir, f"run_{n}.json")) as f:
            runs.append(json.load(f))
    medians = {}
    for sc in scenarios:
        medians[sc] = {
            "wall_s": median([r[sc]["wall_s"] for r in runs]),
            "rss_kb": median([r[sc]["rss_kb"] for r in runs]),
        }
    return medians


def warn_wall(sc, ratio, measured, baseline):
    if ratio > WALL_WARN_RATIO:
        print(
            f"::warning title=Perf Guard (wall_s)::{sc} wall_s is {ratio:.2f}× "
            f"baseline ({measured:.2f}s vs {baseline:.2f}s). Likely performance regression — review recommended."
        )


def scenario_rows(sc, baseline, measured):
    bw = baseline["wall_s"]
    br = baseline["rss_kb"]
    mw = measured["wall_s"]
    mr = measured["rss_kb"]
    rw = mw / bw if bw > 0 else 0
    rr = mr / br if br > 0 else 0
    warn_wall(sc, rw, mw, bw)
    sw = "ℹ️" if rw <= WALL_THRESHOLD else "⚠️"  # informational, never fails
    sr = "✅" if rr <= RSS_THRESHOLD else "❌"
    rows = [
        f"| {sc} | wall_s | {bw:.2f} | {mw:.2f} | {rw:.2f}× | {sw} |",
        f"| {sc} | rss_kb | {br} | {mr:.0f} | {rr:.2f}× | {sr} |",
    ]
    return rows, rr > RSS_THRESHOLD


def build_table(baselines, medians, scenarios):
    rows = []
    failed = False
    for sc in scenarios:
        sc_rows, sc_failed = scenario_rows(sc, baselines[sc], medians[sc])
        rows.extend(sc_rows)
        failed = failed or sc_failed
    table = "| Scenario | Metric | Baseline | Measured | Ratio | Status |\n"
    table += "|----------|--------|----------|----------|-------|--------|\n"
    table += "\n".join(rows)
    return table, failed


def emit_outputs(table, failed, medians, table_out, measured_out):
    print(table)
    with open(measured_out, "w") as f:
        json.dump(medians, f, indent=2)
    if table_out is not None:
        with open(table_out, "w") as f:
            f.write(table + "\n")
    gh_out = os.environ.get("GITHUB_OUTPUT")
    if gh_out:
        with open(gh_out, "a") as f:
            f.write(f"failed={'true' if failed else 'false'}\n")
            f.write(f"table<<EOF\n{table}\nEOF\n")


def main() -> int:
    parsed = parse_args(sys.argv)
    if parsed is None:
        print(
            "usage: perf-compare.py <results-dir> [--table-out PATH] [--measured-out PATH]",
            file=sys.stderr,
        )
        return 2
    results_dir, table_out, measured_out = parsed

    with open("tests/perf/baselines.json") as f:
        baselines = json.load(f)
    scenarios = [k for k in baselines if not k.startswith("_")]

    try:
        medians = load_medians(results_dir, scenarios)
    except FileNotFoundError as e:
        print(f"perf-compare: missing run file: {e.filename}", file=sys.stderr)
        return 2
    table, failed = build_table(baselines, medians, scenarios)
    emit_outputs(table, failed, medians, table_out, measured_out)

    if failed:
        print("❌ Performance regression detected!", file=sys.stderr)
        return 1
    print("✅ All scenarios within thresholds.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
