# Performance Guard

Automated performance regression detection for Zipper.

## How It Works

1. `measure.sh` runs three scenarios and emits wall time + peak RSS as JSON.
2. `perf-guard.yml` runs on PRs that touch `src/**`, takes the median of 5 runs, and compares against `baselines.json`.
3. If wall time exceeds 1.25× baseline or RSS exceeds 1.20× baseline, the check fails.

## Scenarios

| Scenario | Command | Purpose |
|----------|---------|---------|
| `pdf_50k` | `--type pdf --count 50000 --folders 4 --with-metadata` | Standard mode throughput |
| `eml_20k` | `--type eml --count 20000 --attachment-rate 30` | Email + attachment pipeline |
| `loadfile_200k` | `--loadfile-only --count 200000 --column-profile standard` | Loadfile-only throughput |

## Capturing New Baselines

When a legitimate performance change occurs (new feature, algorithm change):

```bash
# Build release binary
dotnet publish src/Zipper.csproj -c Release

# Run 5 times, take max per scenario
for i in $(seq 1 5); do
  ./tests/perf/measure.sh
done

# Update baselines.json with the max values observed
```

On CI, the `perf-baseline-refresh.yml` workflow runs weekly (Monday 04:00 UTC) and auto-proposes a PR if drift exceeds 5%.

## Reading the PR Comment

The perf-guard workflow posts a markdown table on the PR:

| Scenario | Metric | Baseline | Measured | Ratio | Status |
|----------|--------|----------|----------|-------|--------|
| pdf_50k | wall_s | 1.86 | 2.10 | 1.13× | ✅ |
| pdf_50k | rss_kb | 110116 | 115000 | 1.04× | ✅ |

- **✅** = within threshold
- **❌** = exceeds threshold (wall > 1.25× or RSS > 1.20×)

## When to Re-capture

- After a major architectural change that intentionally affects performance
- After upgrading .NET runtime version
- After changing file generation algorithms
- When the weekly refresh cron proposes a PR (review before merging)
