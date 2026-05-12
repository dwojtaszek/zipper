#!/bin/bash
# Performance measurement script for Zipper CI perf guard.
# Runs three scenarios and emits JSON with wall time (seconds) and peak RSS (KB).
#
# Usage: ./tests/perf/measure.sh [path-to-zipper-binary]
# Output: JSON to stdout

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

ZIPPER="${1:-$REPO_ROOT/src/bin/Release/net8.0/Zipper}"

if [[ ! -x "$ZIPPER" ]]; then
    echo "Error: Zipper binary not found at $ZIPPER" >&2
    echo "Build with: dotnet publish src/Zipper.csproj -c Release" >&2
    exit 1
fi

TIME_CMD="/usr/bin/time"
if [[ ! -x "$TIME_CMD" ]]; then
    echo "Error: /usr/bin/time not found (required for RSS measurement)" >&2
    exit 1
fi

run_scenario() {
    shift  # skip scenario name (used by caller for clarity)
    local out_dir
    out_dir=$(mktemp -d)

    local time_output
    time_output=$("$TIME_CMD" -f '%e %M' "$ZIPPER" "$@" --output-path "$out_dir" 2>&1 >/dev/null | tail -1)

    local wall_s rss_kb
    wall_s=$(echo "$time_output" | awk '{print $1}')
    rss_kb=$(echo "$time_output" | awk '{print $2}')

    rm -rf "$out_dir"
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
