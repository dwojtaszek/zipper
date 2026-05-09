#!/bin/bash
# test-chaos-production-set.sh
#
# E2E: Chaos Engine is applied in production-set mode.
# Addresses issue #259 — ChaosEngine was silently dropped in --production-set mode.
#
# Must be called from the repository root:
#   bash ./tests/test-chaos-production-set.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TEST_OUTPUT_DIR="$SCRIPT_DIR/results/chaos-production-set"

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
# T1: Production-set mode with chaos produces a DAT load file different from baseline
# ---------------------------------------------------------------------------
TOTAL=$((TOTAL + 1))
print_info "T1: Production-set mode — chaos DAT must differ from no-chaos baseline"

zipper --type pdf --count 100 --seed 42 \
    --production-set --bates-prefix CHAOS \
    --output-path "$TEST_OUTPUT_DIR/baseline"
BASELINE_DAT=$(find "$TEST_OUTPUT_DIR/baseline" -name "loadfile.dat" | head -1)
[[ -z "$BASELINE_DAT" ]] && print_error "No baseline production-set DAT generated"

zipper --type pdf --count 100 --seed 42 \
    --production-set --bates-prefix CHAOS \
    --chaos-mode --chaos-types quotes --chaos-amount "10%" \
    --output-path "$TEST_OUTPUT_DIR/chaos_run1"
CHAOS_DAT=$(find "$TEST_OUTPUT_DIR/chaos_run1" -name "loadfile.dat" | head -1)
[[ -z "$CHAOS_DAT" ]] && print_error "No chaos production-set DAT generated"

if cmp -s "$BASELINE_DAT" "$CHAOS_DAT"; then
    print_error "T1 FAILED: Production-set chaos DAT is identical to baseline — anomalies not injected"
fi

PASSED=$((PASSED + 1))
print_success "T1: Production-set chaos DAT differs from baseline — PASSED"

# ---------------------------------------------------------------------------
# T2: Determinism — two production-set runs with the same seed produce identical DAT
# ---------------------------------------------------------------------------
TOTAL=$((TOTAL + 1))
print_info "T2: Production-set mode — determinism"

zipper --type pdf --count 100 --seed 42 \
    --production-set --bates-prefix CHAOS \
    --chaos-mode --chaos-types quotes --chaos-amount "10%" \
    --output-path "$TEST_OUTPUT_DIR/chaos_run2"
CHAOS_DAT2=$(find "$TEST_OUTPUT_DIR/chaos_run2" -name "loadfile.dat" | head -1)
[[ -z "$CHAOS_DAT2" ]] && print_error "No chaos DAT2 for production-set"

cmp -s "$CHAOS_DAT" "$CHAOS_DAT2" \
    || print_error "T2 FAILED: Two production-set chaos runs with seed 42 produced different DATs"

PASSED=$((PASSED + 1))
print_success "T2: Production-set deterministic chaos — PASSED"

# ---------------------------------------------------------------------------
# T3: Existing no-chaos production-set behaviour is unchanged (regression guard)
# ---------------------------------------------------------------------------
TOTAL=$((TOTAL + 1))
print_info "T3: No-chaos production-set baseline is stable across two runs"

zipper --type pdf --count 100 --seed 42 \
    --production-set --bates-prefix CHAOS \
    --output-path "$TEST_OUTPUT_DIR/baseline2"
BASELINE2_DAT=$(find "$TEST_OUTPUT_DIR/baseline2" -name "loadfile.dat" | head -1)
[[ -z "$BASELINE2_DAT" ]] && print_error "No second baseline DAT"

cmp -s "$BASELINE_DAT" "$BASELINE2_DAT" \
    || print_error "T3 FAILED: No-chaos production-set baseline is not deterministic"

PASSED=$((PASSED + 1))
print_success "T3: No-chaos production-set baseline is stable — PASSED"

# ---------------------------------------------------------------------------
# Cleanup & summary
# ---------------------------------------------------------------------------
rm -rf "$TEST_OUTPUT_DIR"
echo ""
print_success "Production-set chaos: $PASSED/$TOTAL tests passed."
