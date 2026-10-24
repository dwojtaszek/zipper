# Performance Guard

Automated performance regression detection for Zipper.

## How It Works

1. `measure.sh` runs three scenarios and emits wall time + peak RSS as JSON.
2. `perf-guard.yml` runs on PRs that touch product code under `src/**` (not test or analyzer projects), takes the **median** of 5 runs, and compares against `baselines.json`.
3. **RSS is advisory:** if peak RSS exceeds 1.20× baseline, the check posts a warning and comparison table. It does not fail for a threshold exceedance; measurement or comparison errors still fail.
4. **Wall time is informational only.** Shared GitHub runners vary 15–40% run-to-run with no code change, and baselines are captured on a different runner than each PR is measured on, so the wall ratio mixes hardware delta with code delta. It is reported (1.25× reference line) but never fails the job.

> **Statistic parity:** Both the PR check and the baseline refresh workflow use the
> **median of 5 runs** (per scenario, using `--seed 42`). This eliminates the mismatch
> where a median-of-5 was compared against a max-of-5 (which masked regressions up to the
> run-to-run spread and biased baselines toward worst-run outliers). Run-to-run variability
> and runtime context (runner image, .NET SDK version, seed) are recorded with the baselines
> and reported in the guard comment.

## Scenarios

| Scenario | Command | Purpose |
|----------|---------|---------|
| `pdf_50k` | `--type pdf --count 50000 --folders 4 --with-metadata --seed 42` | Standard mode throughput |
| `eml_20k` | `--type eml --count 20000 --attachment-rate 30 --seed 42` | Email + attachment pipeline |
| `loadfile_200k` | `--loadfile-only --count 200000 --column-profile standard --seed 42` | Loadfile-only throughput |

## Capturing New Baselines

When a legitimate performance change occurs (new feature, algorithm change):

```bash
# Build release binary
dotnet publish src/Zipper.csproj -c Release -o publish-bin

# Run 5 times with --seed 42
mkdir -p results
for i in $(seq 1 5); do
  ./tests/perf/measure.sh ./publish-bin/Zipper --seed 42 > "results/run_${i}.json"
done

# Run baseline refresh script (computes medians and updates baselines.json)
python3 .github/scripts/perf-baseline-refresh.py results tests/perf/baselines.json --seed 42
```

On CI, the `perf-baseline-refresh.yml` workflow runs weekly (Monday 04:00 UTC) and auto-proposes a PR if drift exceeds 5%. Baseline increases require recorded justification before merge, and raw measurement runs (`run_1..run_5.json`) are attached to the refresh PR.

## Reading the PR Comment

The perf-guard workflow posts a markdown table on the PR:

| Scenario | Metric | Baseline | Measured | Ratio | Status |
|----------|--------|----------|----------|-------|--------|
| pdf_50k | wall_s | 1.86 | 2.10 | 1.13× | ✅ |
| pdf_50k | rss_kb | 110116 | 115000 | 1.04× | ✅ |

- **✅** = RSS within threshold (≤ 1.20×)
- **⚠️** = RSS exceeds 1.20× — advisory, review recommended
- **ℹ️ / ⚠️** = wall_s reference marker (≤ / > 1.25×) — informational only, never fails

## When to Re-capture

- After a major architectural change that intentionally affects performance
- After upgrading .NET runtime version
- After changing file generation algorithms
- When the weekly refresh cron proposes a PR (review before merging)
