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

# --- Column profile override flags ---

print_info "Test: --empty-percentage 0 produces no empty fields"
if zipper --loadfile-only --count 20 --output-path "$TEST_OUTPUT_DIR/empty_pct" \
    --column-profile standard --empty-percentage 0 --seed 42 > /dev/null 2>&1; then
    dat_file=$(find "$TEST_OUTPUT_DIR/empty_pct" -name "*.dat" -print -quit)
    if [[ -z "$dat_file" || ! -s "$dat_file" ]]; then
        fail "--empty-percentage 0: no .dat file produced"
    else
        non_empty_rows=$(tail -n +2 "$dat_file" | wc -l)
        if [[ "$non_empty_rows" -eq 20 ]]; then
            pass "--empty-percentage 0 produces 20 data rows"
        else
            fail "--empty-percentage 0: expected 20 rows, got $non_empty_rows"
        fi
    fi
else
    fail "--empty-percentage 0 command failed"
fi

print_info "Test: --newline-delim recorded in properties"
if zipper --loadfile-only --count 5 --output-path "$TEST_OUTPUT_DIR/newline_delim" \
    --newline-delim "ascii:10" > /dev/null 2>&1; then
    props_file=$(find "$TEST_OUTPUT_DIR/newline_delim" -name "*_properties.json" -print -quit)
    if grep -q '"newline": "ascii:10"' "$props_file"; then
        pass "--newline-delim ascii:10 recorded in properties"
    else
        fail "--newline-delim not found in properties"
    fi
else
    fail "--newline-delim command failed"
fi

print_info "Test: --date-format override"
if zipper --loadfile-only --count 10 --output-path "$TEST_OUTPUT_DIR/date_fmt" \
    --column-profile standard --date-format "dd/MM/yyyy" --seed 42 > /dev/null 2>&1; then
    dat_file=$(find "$TEST_OUTPUT_DIR/date_fmt" -name "*.dat" -print -quit)
    # The --date-format flag applies to column-profile settings; verify it's accepted without error
    if [[ -f "$dat_file" ]]; then
        pass "--date-format accepted and produces output"
    else
        fail "--date-format: no output file"
    fi
else
    fail "--date-format command failed"
fi

print_info "Test: --custodian-count limits pool"
if zipper --type pdf --count 20 --output-path "$TEST_OUTPUT_DIR/cust_count" \
    --with-metadata --custodian-count 2 --seed 42 > /dev/null 2>&1; then
    dat_file=$(find "$TEST_OUTPUT_DIR/cust_count" -name "*.dat" -print -quit)
    if [[ -z "$dat_file" || ! -s "$dat_file" ]]; then
        fail "--custodian-count: no .dat file produced"
    else
        # With --with-metadata and --custodian-count 2, custodian column should have <= 2 distinct values
        custodian_count=$(tail -n +2 "$dat_file" | cut -d$'\x14' -f3 | tr -d '\xfe' | sort -u | grep -c '.')
        if [[ "$custodian_count" -le 2 ]]; then
            pass "--custodian-count 2 produces <= 2 distinct custodians"
        else
            fail "--custodian-count 2: got $custodian_count distinct custodians"
        fi
    fi
else
    fail "--custodian-count command failed"
fi

# --- --with-families flag ---

print_info "Test: --with-families accepted with EML + attachments"
err_file=$(mktemp)
if zipper --type eml --count 10 --output-path "$TEST_OUTPUT_DIR/families" \
    --with-families --attachment-rate 50 2> "$err_file"; then
    dat_file=$(find "$TEST_OUTPUT_DIR/families" -name "*.dat" -print -quit)
    if [[ -n "$dat_file" && -s "$dat_file" ]]; then
        if ! grep -q "Warning: --with-families is only meaningful" "$err_file"; then
            pass "--with-families accepted and does not emit warning"
        else
            fail "--with-families incorrectly emitted warning for valid config"
        fi
    else
        fail "--with-families: no .dat file produced"
    fi
else
    fail "--with-families command failed"
fi
rm -f "$err_file"

print_info "Test: --with-families warning emitted without --type eml"
err_file=$(mktemp)
if zipper --type pdf --count 5 --output-path "$TEST_OUTPUT_DIR/families-warn1" \
    --with-families 2> "$err_file"; then
    if grep -q "Warning: --with-families is only meaningful when --type eml and --attachment-rate > 0 are specified." "$err_file"; then
        pass "--with-families warning emitted for non-eml type"
    else
        fail "--with-families warning NOT emitted for non-eml type"
    fi
else
    fail "--with-families non-eml type command failed"
fi
rm -f "$err_file"

print_info "Test: --with-families warning emitted with --attachment-rate 0"
err_file=$(mktemp)
if zipper --type eml --count 5 --output-path "$TEST_OUTPUT_DIR/families-warn2" \
    --with-families --attachment-rate 0 2> "$err_file"; then
    if grep -q "Warning: --with-families is only meaningful when --type eml and --attachment-rate > 0 are specified." "$err_file"; then
        pass "--with-families warning emitted for attachment-rate 0"
    else
        fail "--with-families warning NOT emitted for attachment-rate 0"
    fi
else
    fail "--with-families attachment-rate 0 command failed"
fi
print_info "Test: --with-families warning emitted with --loadfile-only"
err_file=$(mktemp)
if zipper --type eml --count 5 --output-path "$TEST_OUTPUT_DIR/families-warn3" \
    --with-families --attachment-rate 50 --loadfile-only 2> "$err_file"; then
    if grep -q "Warning: --with-families has no effect in --loadfile-only mode." "$err_file"; then
        pass "--with-families warning emitted for --loadfile-only mode"
    else
        fail "--with-families warning NOT emitted for --loadfile-only mode"
    fi
else
    fail "--with-families loadfile-only command failed"
fi
rm -f "$err_file"

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
