#!/bin/bash
# Performance measurement script for Zipper CI perf guard.
# Runs three scenarios and emits JSON with wall time (seconds) and peak RSS (KB).
#
# Usage: ./tests/perf/measure.sh [path-to-zipper-binary]
# Output: JSON to stdout

set -euo pipefail
shopt -s inherit_errexit 2>/dev/null || true

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

ZIPPER="${1:-$REPO_ROOT/src/bin/Release/net10.0/Zipper}"

if [[ ! -x "$ZIPPER" ]]; then
    echo "Error: Zipper binary not found at $ZIPPER" >&2
    echo "Build with: dotnet publish src/Zipper.csproj -c Release" >&2
    exit 1
fi

TIME_CMD="${TIME_CMD:-/usr/bin/time}"
if [[ ! -x "$TIME_CMD" ]]; then
    if [[ "$TIME_CMD" == "/usr/bin/time" ]]; then
        echo "Error: /usr/bin/time not found (required for RSS measurement)" >&2
    else
        echo "Error: Time command not found at $TIME_CMD (required for RSS measurement)" >&2
    fi
    exit 1
fi

run_scenario() {
    local scenario_name="$1"
    shift
    local out_dir time_file err_file
    out_dir=$(mktemp -d)
    time_file=$(mktemp)
    err_file=$(mktemp)

    trap 'rm -rf "$out_dir" "$time_file" "$err_file"' EXIT INT TERM

    local exit_code=0
    "$TIME_CMD" -o "$time_file" -f '%e %M' "$ZIPPER" "$@" --output-path "$out_dir" 2>"$err_file" >/dev/null || exit_code=$?

    if [[ $exit_code -ne 0 ]]; then
        if [[ -s "$err_file" ]]; then
            cat "$err_file" >&2
        fi
        echo "Error: Scenario '$scenario_name' failed (CLI exited with code $exit_code)" >&2
        rm -rf "$out_dir" "$time_file" "$err_file"
        trap - EXIT INT TERM
        return $exit_code
    fi

    local time_output
    time_output=$(cat "$time_file" 2>/dev/null || true)

    local wall_s rss_kb
    wall_s=$(echo "$time_output" | awk '{print $1}')
    rss_kb=$(echo "$time_output" | awk '{print $2}')

    if [[ -z "$wall_s" || -z "$rss_kb" ]] || ! [[ "$wall_s" =~ ^[0-9]+(\.[0-9]+)?$ ]] || ! [[ "$rss_kb" =~ ^[0-9]+$ ]]; then
        if [[ -s "$err_file" ]]; then
            cat "$err_file" >&2
        fi
        echo "Error: Scenario '$scenario_name' failed: malformed or missing timing output (got: '$time_output')" >&2
        rm -rf "$out_dir" "$time_file" "$err_file"
        trap - EXIT INT TERM
        return 1
    fi

    rm -rf "$out_dir" "$time_file" "$err_file"
    trap - EXIT INT TERM
    echo "{\"wall_s\": $wall_s, \"rss_kb\": $rss_kb}"
}

# --- Scenarios ---

pdf_50k=$(run_scenario "pdf_50k" \
    --type pdf --count 50000 --folders 4 --with-metadata)

eml_20k=$(run_scenario "eml_20k" \
    --type eml --count 20000 --attachment-rate 30)

loadfile_200k=$(run_scenario "loadfile_200k" \
    --loadfile-only --count 200000 --column-profile standard)

# --- Output JSON ---

cat <<EOF
{
  "pdf_50k": $pdf_50k,
  "eml_20k": $eml_20k,
  "loadfile_200k": $loadfile_200k
}
EOF
