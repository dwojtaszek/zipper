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

function print_success() { local msg="$1"; echo -e "\e[42m[ SUCCESS ]\e[0m $msg"; }
function print_info()    { local msg="$1"; echo -e "\e[44m[ INFO ]\e[0m $msg"; }
function print_error()   {
    local msg="$1"
    echo -e "\e[41m[ ERROR ]\e[0m $msg" >&2
    exit 1
}

DAT_NAME="loadfile.dat"
OPT_NAME="loadfile.opt"
PROPERTIES_NAME="loadfile_properties.json"

PASSED=0
TOTAL=0

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# ---------------------------------------------------------------------------
# T1: Production-set mode with chaos produces a DAT and OPT load file different from baseline
# ---------------------------------------------------------------------------
TOTAL=$((TOTAL + 1))
print_info "T1: Production-set mode — chaos DAT/OPT and properties must differ from no-chaos baseline"

zipper --type pdf --count 100 --seed 42 \
    --production-set --bates-prefix CHAOS \
    --output-path "$TEST_OUTPUT_DIR/baseline"
BASELINE_DAT=$(find "$TEST_OUTPUT_DIR/baseline" -name "$DAT_NAME" -print -quit)
BASELINE_OPT=$(find "$TEST_OUTPUT_DIR/baseline" -name "$OPT_NAME" -print -quit)
[[ -z "$BASELINE_DAT" ]] && print_error "No baseline production-set DAT generated"
[[ -z "$BASELINE_OPT" ]] && print_error "No baseline production-set OPT generated"

zipper --type pdf --count 100 --seed 42 \
    --production-set --bates-prefix CHAOS \
    --chaos-mode --chaos-types quotes --chaos-amount "10%" \
    --output-path "$TEST_OUTPUT_DIR/chaos_run1"
CHAOS_DAT=$(find "$TEST_OUTPUT_DIR/chaos_run1" -name "$DAT_NAME" -print -quit)
CHAOS_OPT=$(find "$TEST_OUTPUT_DIR/chaos_run1" -name "$OPT_NAME" -print -quit)
CHAOS_PROPERTIES=$(find "$TEST_OUTPUT_DIR/chaos_run1" -name "$PROPERTIES_NAME" -print -quit)
[[ -z "$CHAOS_DAT" ]] && print_error "No chaos production-set DAT generated"
[[ -z "$CHAOS_OPT" ]] && print_error "No chaos production-set OPT generated"
[[ -z "$CHAOS_PROPERTIES" ]] && print_error "No chaos properties generated"

if cmp -s "$BASELINE_DAT" "$CHAOS_DAT"; then
    print_error "T1 FAILED: Production-set chaos DAT is identical to baseline — anomalies not injected"
fi
if cmp -s "$BASELINE_OPT" "$CHAOS_OPT"; then
    print_error "T1 FAILED: Production-set chaos OPT is identical to baseline — anomalies not injected"
fi

# Verify _properties.json contains the expected anomalies
ANOMALIES=$(grep -c "errorType" "$CHAOS_PROPERTIES" || true)
if [[ "$ANOMALIES" -lt 10 ]]; then
    print_error "T1 FAILED: _properties.json does not contain expected anomalies (found $ANOMALIES)"
fi

PASSED=$((PASSED + 1))
print_success "T1: Production-set chaos DAT/OPT differs from baseline — PASSED"

# ---------------------------------------------------------------------------
# T2: Determinism — two production-set runs with the same seed produce identical output
# ---------------------------------------------------------------------------
TOTAL=$((TOTAL + 1))
print_info "T2: Production-set mode — determinism"

zipper --type pdf --count 100 --seed 42 \
    --production-set --bates-prefix CHAOS \
    --chaos-mode --chaos-types quotes --chaos-amount "10%" \
    --output-path "$TEST_OUTPUT_DIR/chaos_run2"
CHAOS_DAT2=$(find "$TEST_OUTPUT_DIR/chaos_run2" -name "$DAT_NAME" -print -quit)
CHAOS_OPT2=$(find "$TEST_OUTPUT_DIR/chaos_run2" -name "$OPT_NAME" -print -quit)
CHAOS_PROPERTIES2=$(find "$TEST_OUTPUT_DIR/chaos_run2" -name "$PROPERTIES_NAME" -print -quit)
[[ -z "$CHAOS_DAT2" ]] && print_error "No chaos DAT2 for production-set"
[[ -z "$CHAOS_OPT2" ]] && print_error "No chaos OPT2 for production-set"
[[ -z "$CHAOS_PROPERTIES2" ]] && print_error "No chaos properties2 for production-set"

cmp -s "$CHAOS_DAT" "$CHAOS_DAT2" \
    || print_error "T2 FAILED: Two production-set chaos runs with seed 42 produced different DATs"
cmp -s "$CHAOS_OPT" "$CHAOS_OPT2" \
    || print_error "T2 FAILED: Two production-set chaos runs with seed 42 produced different OPTs"
cmp -s "$CHAOS_PROPERTIES" "$CHAOS_PROPERTIES2" \
    || print_error "T2 FAILED: Two production-set chaos runs with seed 42 produced different properties"

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
BASELINE2_DAT=$(find "$TEST_OUTPUT_DIR/baseline2" -name "$DAT_NAME" -print -quit)
BASELINE2_OPT=$(find "$TEST_OUTPUT_DIR/baseline2" -name "$OPT_NAME" -print -quit)
[[ -z "$BASELINE2_DAT" ]] && print_error "No second baseline DAT"
[[ -z "$BASELINE2_OPT" ]] && print_error "No second baseline OPT"

cmp -s "$BASELINE_DAT" "$BASELINE2_DAT" \
    || print_error "T3 FAILED: No-chaos production-set baseline DAT is not deterministic"
cmp -s "$BASELINE_OPT" "$BASELINE2_OPT" \
    || print_error "T3 FAILED: No-chaos production-set baseline OPT is not deterministic"

PASSED=$((PASSED + 1))
print_success "T3: No-chaos production-set baseline is stable — PASSED"

# ---------------------------------------------------------------------------
# Cleanup & summary
# ---------------------------------------------------------------------------
rm -rf "$TEST_OUTPUT_DIR"
echo ""
print_success "Production-set chaos: $PASSED/$TOTAL tests passed."
