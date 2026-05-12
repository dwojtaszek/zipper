#!/bin/bash
# E2E test: CLI flags with no prior end-to-end coverage
# Covers flags from issue #287 that have zero E2E tests.

set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

PASSED=0
FAILED=0
TEST_OUTPUT_DIR="./results/e2e-coverage-gaps"

function print_info() { local msg="$1"; echo -e "\033[44m[ INFO ]\033[0m $msg"; }
function print_success() { local msg="$1"; echo -e "\033[42m[ SUCCESS ]\033[0m $msg"; }
function print_error() { local msg="$1"; echo -e "\033[41m[ ERROR ]\033[0m $msg" >&2; }

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

pass() { local msg="$1"; print_info "PASS: $msg"; PASSED=$((PASSED + 1)); }
fail() { local msg="$1"; print_error "FAIL: $msg"; FAILED=$((FAILED + 1)); }

print_info "=== E2E Coverage Gap Tests ==="

# --- Utility flags ---

print_info "Test: --benchmark exits 0"
if zipper --benchmark > /dev/null 2>&1; then
    pass "--benchmark exits 0"
else
    fail "--benchmark exits 0"
fi

print_info "Test: --chaos-list exits 0 and lists scenarios"
if chaos_output=$(zipper --chaos-list 2>&1); then
    if echo "$chaos_output" | grep -q "full-chaos"; then
        pass "--chaos-list contains scenario names"
    else
        fail "--chaos-list missing expected scenario names"
    fi
else
    fail "--chaos-list exited non-zero"
fi

# --- Multi-format generation ---

print_info "Test: --load-file-formats dat,opt,csv"
if zipper --type pdf --count 5 --output-path "$TEST_OUTPUT_DIR/multi_format" --load-file-formats dat,opt,csv > /dev/null 2>&1; then
    [[ -n "$(find "$TEST_OUTPUT_DIR/multi_format" -name "*.dat" -print -quit)" ]] && pass "produces .dat" || fail "produces .dat"
    [[ -n "$(find "$TEST_OUTPUT_DIR/multi_format" -name "*.opt" -print -quit)" ]] && pass "produces .opt" || fail "produces .opt"
    [[ -n "$(find "$TEST_OUTPUT_DIR/multi_format" -name "*.csv" -print -quit)" ]] && pass "produces .csv" || fail "produces .csv"
else
    fail "--load-file-formats command failed"
fi

# --- Loadfile-only delimiter flags ---

print_info "Test: --quote-delim none (unquoted output)"
if zipper --loadfile-only --count 5 --output-path "$TEST_OUTPUT_DIR/unquoted" \
    --col-delim "char:|" --quote-delim none > /dev/null 2>&1; then
    dat_file=$(find "$TEST_OUTPUT_DIR/unquoted" -name "*.dat" -print -quit)
    if head -1 "$dat_file" | grep -q '|'; then
        pass "--quote-delim none produces pipe-delimited unquoted output"
    else
        fail "--quote-delim none: no pipe delimiter found"
    fi
else
    fail "--quote-delim none command failed"
fi

print_info "Test: --multi-delim recorded in properties"
if zipper --loadfile-only --count 5 --output-path "$TEST_OUTPUT_DIR/multi_delim" \
    --multi-delim "char:;" --column-profile standard > /dev/null 2>&1; then
    props_file=$(find "$TEST_OUTPUT_DIR/multi_delim" -name "*_properties.json" -print -quit)
    if grep -q '"multiValue": "char:;"' "$props_file"; then
        pass "--multi-delim recorded in properties"
    else
        fail "--multi-delim not found in properties"
    fi
else
    fail "--multi-delim command failed"
fi

print_info "Test: --nested-delim recorded in properties"
if zipper --loadfile-only --count 5 --output-path "$TEST_OUTPUT_DIR/nested_delim" \
    --nested-delim 'char:\' --column-profile standard > /dev/null 2>&1; then
    props_file=$(find "$TEST_OUTPUT_DIR/nested_delim" -name "*_properties.json" -print -quit)
    if grep -q '"nestedValue":' "$props_file"; then
        pass "--nested-delim recorded in properties"
    else
        fail "--nested-delim not found in properties"
    fi
else
    fail "--nested-delim command failed"
fi

# --- load-file-format edrm-xml (canonical name) ---

print_info "Test: --load-file-format edrm-xml"
if zipper --type pdf --count 5 --output-path "$TEST_OUTPUT_DIR/edrm_xml" --load-file-format edrm-xml > /dev/null 2>&1; then
    xml_file=$(find "$TEST_OUTPUT_DIR/edrm_xml" -name "*.xml" -print -quit)
    if [[ -n "$xml_file" ]] && grep -q '<documents>' "$xml_file"; then
        pass "--load-file-format edrm-xml produces valid XML"
    else
        fail "--load-file-format edrm-xml: no valid XML found"
    fi
else
    fail "--load-file-format edrm-xml command failed"
fi

# --- Summary ---

echo ""
rm -rf "$TEST_OUTPUT_DIR"
TOTAL=$((PASSED + FAILED))
if [[ "$FAILED" -eq 0 ]]; then
    print_success "All E2E coverage gap tests passed! ($PASSED/$TOTAL)"
else
    print_error "E2E coverage gap tests: $FAILED/$TOTAL FAILED"
    exit 1
fi
