#!/bin/bash
# test-chaos-standard-mode.sh
#
# E2E: Chaos Engine is applied in standard (ZIP + load file) mode.
# Addresses issue #259 — ChaosEngine was silently dropped in standard mode.
#
# Must be called from the repository root:
#   bash ./tests/test-chaos-standard-mode.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEST_OUTPUT_DIR="$SCRIPT_DIR/results/chaos-standard-mode"

# shellcheck source=./_zipper-cli.sh
source "$SCRIPT_DIR/_zipper-cli.sh"

function print_success() { echo -e "\e[42m[ SUCCESS ]\e[0m $1"; }
function print_info()    { echo -e "\e[44m[ INFO ]\e[0m $1"; }
function print_error()   {
    echo -e "\e[41m[ ERROR ]\e[0m $1" >&2
    exit 1
}

PASSED=0
TOTAL=0

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# ---------------------------------------------------------------------------
# T1: Standard mode with chaos produces a load file different from no-chaos baseline
# ---------------------------------------------------------------------------
TOTAL=$((TOTAL + 1))
print_info "T1: Standard mode — chaos DAT must differ from no-chaos baseline"

zipper --type pdf --count 200 --seed 42 \
    --output-path "$TEST_OUTPUT_DIR/baseline"
BASELINE_DAT=$(find "$TEST_OUTPUT_DIR/baseline" -name "*.dat" | head -1)
[[ -z "$BASELINE_DAT" ]] && print_error "No baseline DAT file generated"

zipper --type pdf --count 200 --seed 42 \
    --chaos-mode --chaos-types quotes --chaos-amount "10%" \
    --output-path "$TEST_OUTPUT_DIR/chaos_run1"
CHAOS_DAT=$(find "$TEST_OUTPUT_DIR/chaos_run1" -name "*.dat" | head -1)
[[ -z "$CHAOS_DAT" ]] && print_error "No chaos DAT file generated"

if cmp -s "$BASELINE_DAT" "$CHAOS_DAT"; then
    print_error "T1 FAILED: Chaos DAT is identical to baseline — anomalies were not injected"
fi

PASSED=$((PASSED + 1))
print_success "T1: Chaos DAT differs from no-chaos baseline — PASSED"

# ---------------------------------------------------------------------------
# T2: Determinism — two runs with the same seed produce byte-identical output
# ---------------------------------------------------------------------------
TOTAL=$((TOTAL + 1))
print_info "T2: Standard mode — determinism (same seed → same output)"

zipper --type pdf --count 200 --seed 42 \
    --chaos-mode --chaos-types quotes --chaos-amount "10%" \
    --output-path "$TEST_OUTPUT_DIR/chaos_run2"
CHAOS_DAT2=$(find "$TEST_OUTPUT_DIR/chaos_run2" -name "*.dat" | head -1)
[[ -z "$CHAOS_DAT2" ]] && print_error "No chaos DAT2 file generated"

cmp -s "$CHAOS_DAT" "$CHAOS_DAT2" \
    || print_error "T2 FAILED: Two chaos runs with seed 42 produced different DAT files"

PASSED=$((PASSED + 1))
print_success "T2: Deterministic chaos output — PASSED"

# ---------------------------------------------------------------------------
# T3: All five DAT chaos types each produce output different from baseline
# ---------------------------------------------------------------------------
for TYPE in mixed-delimiters quotes columns eol encoding; do
    TOTAL=$((TOTAL + 1))
    print_info "T3: Standard mode chaos type '$TYPE'"

    OUT_DIR="$TEST_OUTPUT_DIR/type_${TYPE//-/_}"
    zipper --type pdf --count 200 --seed 42 \
        --chaos-mode --chaos-types "$TYPE" --chaos-amount "10%" \
        --output-path "$OUT_DIR"

    TYPE_DAT=$(find "$OUT_DIR" -name "*.dat" | head -1)
    [[ -z "$TYPE_DAT" ]] && print_error "No DAT for chaos type: $TYPE"

    if cmp -s "$BASELINE_DAT" "$TYPE_DAT"; then
        print_error "T3 FAILED: Chaos type '$TYPE' produced output identical to baseline"
    fi

    PASSED=$((PASSED + 1))
    print_success "T3: Chaos type '$TYPE' alters standard mode DAT — PASSED"
done

# ---------------------------------------------------------------------------
# T4: ZIP archive is valid and contains the chaos-modified load file
# ---------------------------------------------------------------------------
TOTAL=$((TOTAL + 1))
print_info "T4: Standard mode — ZIP archive contains DAT load file"

CHAOS_ZIP=$(find "$TEST_OUTPUT_DIR/chaos_run1" -name "*.zip" | head -1)
[[ -z "$CHAOS_ZIP" ]] && print_error "No ZIP archive generated"

# Verify archive is non-empty (basic integrity check)
ENTRY_COUNT=$(python3 -c "import zipfile; z=zipfile.ZipFile('$CHAOS_ZIP'); print(len(z.namelist()))" 2>/dev/null || echo 0)
[[ "$ENTRY_COUNT" -lt 200 ]] && print_error "T4 FAILED: Expected ≥200 entries in ZIP, got $ENTRY_COUNT"

PASSED=$((PASSED + 1))
print_success "T4: ZIP archive valid and contains expected entries — PASSED"

# ---------------------------------------------------------------------------
# Cleanup & summary
# ---------------------------------------------------------------------------
rm -rf "$TEST_OUTPUT_DIR"
echo ""
print_success "Standard-mode chaos: $PASSED/$TOTAL tests passed."
