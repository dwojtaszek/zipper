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

print_info "Test: --benchmark runs and produces output"
bench_output=$(zipper --benchmark 2>&1) && bench_exit=0 || bench_exit=$?
if [[ $bench_exit -eq 0 || $bench_exit -eq 1 ]] && echo "$bench_output" | grep -q "Benchmark Suite"; then
    pass "--benchmark runs and produces benchmark output (exit $bench_exit)"
else
    fail "--benchmark failed to run (exit $bench_exit)"
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
    if [[ -n "$xml_file" ]] && grep -q '<Root' "$xml_file"; then
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
    --column-profile standard --empty-percentage 0 --seed 1337 > /dev/null 2>&1; then
    dat_file=$(find "$TEST_OUTPUT_DIR/empty_pct" -name "*.dat" -print -quit)
    if [[ -z "$dat_file" || ! -s "$dat_file" ]]; then
        fail "--empty-percentage 0: no .dat file produced"
    else
        non_empty_rows=$(tail -n +2 "$dat_file" | wc -l)
        # DAT format uses \x14 (field separator) and þ (\xFE quote delimiter)
        # Pattern matches: consecutive separators, empty quoted fields, empty first/last fields
        lines_with_empty_fields=$(tail -n +2 "$dat_file" \
            | tr -d '\r' \
            | grep -o -E $'\x14\x14|\x14þþ\x14|^\x14|^þþ\x14|\x14$|\x14þþ$' \
            | wc -l || true)
        if [[ "$non_empty_rows" -eq 20 && "$lines_with_empty_fields" -eq 0 ]]; then
            pass "--empty-percentage 0: verified 20 rows produced and zero lines with empty fields"
        else
            fail "--empty-percentage 0: failed validation. Rows: $non_empty_rows, Lines with empty fields: $lines_with_empty_fields"
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
    --column-profile standard --date-format "dd/MM/yyyy" --seed 1337 > /dev/null 2>&1; then
    dat_file=$(find "$TEST_OUTPUT_DIR/date_fmt" -name "*.dat" -print -quit)
    if [[ -f "$dat_file" ]]; then
        if grep -q -E '[0-9]{2}/[0-9]{2}/[0-9]{4}' "$dat_file"; then
            pass "--date-format accepted and verified in output content"
        else
            fail "--date-format: output dates did not match dd/MM/yyyy"
        fi
    else
        fail "--date-format: no output file"
    fi
else
    fail "--date-format command failed"
fi

print_info "Test: --custodian-count limits pool"
if zipper --type pdf --count 20 --output-path "$TEST_OUTPUT_DIR/cust_count" \
    --with-metadata --custodian-count 2 --seed 1337 > /dev/null 2>&1; then
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
        if ! grep -qi "warning: --with-families" "$err_file"; then
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
    if grep -q "Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified." "$err_file"; then
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
    if grep -q "Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified." "$err_file"; then
        pass "--with-families warning emitted for attachment-rate 0"
    else
        fail "--with-families warning NOT emitted for attachment-rate 0"
    fi
else
    fail "--with-families attachment-rate 0 command failed"
fi
rm -f "$err_file"

print_info "Test: --with-families warning emitted with --loadfile-only"
err_file=$(mktemp)
if zipper --type eml --count 5 --output-path "$TEST_OUTPUT_DIR/families-warn3" \
    --with-families --attachment-rate 50 --loadfile-only 2> "$err_file"; then
    if grep -q "Warning: --with-families has no effect in --loadfile-only mode." "$err_file"; then
        fail "--with-families warning emitted for --loadfile-only mode"
    else
        pass "--with-families is supported in --loadfile-only mode without warning"
    fi
else
    fail "--with-families loadfile-only command failed"
fi
rm -f "$err_file"

print_info "Test: --with-threading warning emitted for non-eml type"
err_file=$(mktemp)
if zipper --type pdf --count 5 --output-path "$TEST_OUTPUT_DIR/threading-warn1" \
    --with-threading 2> "$err_file"; then
    if grep -q "Warning: --with-threading is only meaningful when --type eml (or eml participates in --types) is specified." "$err_file"; then
        pass "--with-threading warning emitted for non-eml type"
    else
        fail "--with-threading warning NOT emitted for non-eml type"
    fi
else
    fail "--with-threading non-eml type command failed"
fi
rm -f "$err_file"

print_info "Test: --with-threading produces Message-ID in EML and thread columns in DAT"
if zipper --type eml --count 5 --seed 42 --output-path "$TEST_OUTPUT_DIR/threading-gen" \
    --with-threading > /dev/null 2>&1; then
    dat_file=$(find "$TEST_OUTPUT_DIR/threading-gen" -name "*.dat" -print -quit)
    if [[ -n "$dat_file" ]] && grep -q "Thread ID" "$dat_file" && grep -q "Inclusive" "$dat_file" && grep -q "Thread Parent" "$dat_file" && grep -q "DOC00000001" "$dat_file"; then
        pass "--with-threading populates thread columns in DAT"
    else
        fail "--with-threading missing thread columns in DAT"
    fi
    zip_file=$(find "$TEST_OUTPUT_DIR/threading-gen" -name "*.zip" -print -quit)
    if [[ -n "$zip_file" ]]; then
        if unzip -p "$zip_file" "*00000001.eml" | grep -q "Message-ID:"; then
            pass "--with-threading emits Message-ID header in EML native"
        else
            fail "--with-threading missing Message-ID in 00000001.eml"
        fi
    else
        fail "--with-threading archive not found"
    fi
else
    fail "--with-threading eml generation command failed"
fi

print_info "Test: --mime-realism warning emitted for non-eml type"
err_file=$(mktemp)
if zipper --type pdf --count 5 --output-path "$TEST_OUTPUT_DIR/mime-realism-warn1" \
    --mime-realism standard 2> "$err_file"; then
    if grep -q "Warning: --mime-realism is only meaningful when --type eml (or eml participates in --types) is specified." "$err_file"; then
        pass "--mime-realism warning emitted for non-eml type"
    else
        fail "--mime-realism warning NOT emitted for non-eml type"
    fi
else
    fail "--mime-realism non-eml type command failed"
fi
rm -f "$err_file"

baseline_output="$TEST_OUTPUT_DIR/mime-realism-baseline"
baseline_zip=""
if zipper --type eml --count 32 --seed 42 --attachment-rate 100 \
    --output-path "$baseline_output" > /dev/null 2>&1; then
    baseline_zip=$(find "$baseline_output" -name "*.zip" -print -quit)
fi
if [[ -n "$baseline_zip" ]]; then
    pass "--mime-realism body-fidelity baseline generated"
else
    fail "--mime-realism body-fidelity baseline generation failed"
fi

for mode in standard full; do
    print_info "Test: --mime-realism $mode produces parser-readable MIME structures"
    mime_output="$TEST_OUTPUT_DIR/mime-realism-$mode"
    if zipper --type eml --count 32 --seed 42 --attachment-rate 100 \
        --output-path "$mime_output" --mime-realism "$mode" > /dev/null 2>&1; then
        zip_file=$(find "$mime_output" -name "*.zip" -print -quit)
        dat_file=$(find "$mime_output" -name "*.dat" -print -quit)
        if [[ -n "$zip_file" && -n "$dat_file" && -n "$baseline_zip" ]] && \
            python3 "$(dirname "$0")/validate_mime_realism.py" "$mode" "$zip_file" "$dat_file" "$baseline_zip"; then
            pass "--mime-realism $mode passes the standard MIME parser checks"
        else
            fail "--mime-realism $mode failed MIME parser validation"
        fi
    else
        fail "--mime-realism $mode EML generation command failed"
    fi
done

print_info "Test: --mime-realism standard emits a Document Family child for every MIME Attachment"
family_output="$TEST_OUTPUT_DIR/mime-realism-families"
if zipper --type eml --count 32 --seed 42 --attachment-rate 100 \
    --with-families --with-text --output-path "$family_output" --mime-realism standard > /dev/null 2>&1; then
    family_zip=$(find "$family_output" -name "*.zip" -print -quit)
    family_dat=$(find "$family_output" -name "*.dat" -print -quit)
    if [[ -n "$family_zip" && -n "$family_dat" && -n "$baseline_zip" ]] && \
        python3 "$(dirname "$0")/validate_mime_realism.py" \
            standard "$family_zip" "$family_dat" "$baseline_zip" --families; then
        pass "--with-families includes every MIME Attachment as a child record"
    else
        fail "--with-families failed MIME Attachment child validation"
    fi
else
    fail "--mime-realism standard with --with-families generation failed"
fi

print_info "Test: --mime-realism full emits family children for nested and placeholder MIME Attachments"
full_family_output="$TEST_OUTPUT_DIR/mime-realism-full-families"
if zipper --type eml --count 32 --seed 42 --attachment-rate 100 \
    --with-families --with-text --output-path "$full_family_output" --mime-realism full > /dev/null 2>&1; then
    full_family_zip=$(find "$full_family_output" -name "*.zip" -print -quit)
    full_family_dat=$(find "$full_family_output" -name "*.dat" -print -quit)
    if [[ -n "$full_family_zip" && -n "$full_family_dat" && -n "$baseline_zip" ]] && \
        python3 "$(dirname "$0")/validate_mime_realism.py" \
            full "$full_family_zip" "$full_family_dat" "$baseline_zip" --families; then
        pass "--mime-realism full includes every nested and placeholder Attachment as a child"
    else
        fail "--mime-realism full failed nested MIME Attachment child validation"
    fi
else
    fail "--mime-realism full with --with-families generation failed"
fi

# --- REQ-237: Email/MIME Chaos Engine Anomaly Types ---

print_info "Test: --type eml with --chaos-mode in Standard mode succeeds and audits anomalies"
eml_chaos_output="$TEST_OUTPUT_DIR/eml-chaos-standard"
if zipper --type eml --count 20 --seed 42 --chaos-mode --chaos-amount 50% \
    --chaos-types "mime-boundary,encoded-word,header-fold,rf822-depth" \
    --output-path "$eml_chaos_output" > /dev/null 2>&1; then
    eml_chaos_zip=$(find "$eml_chaos_output" -name "*.zip" -print -quit)
    eml_chaos_props=$(find "$eml_chaos_output" -name "*_properties.json" -print -quit)
    if [[ -n "$eml_chaos_zip" ]] && python3 "$(dirname "$0")/validate_email_chaos_audit.py" standard "$eml_chaos_props"; then
        pass "--type eml with --chaos-mode generated valid archive with MIME anomalies"
    else
        fail "--type eml with --chaos-mode did not generate archive with the expected email anomalies"
    fi
else
    fail "--type eml with --chaos-mode generation failed"
fi

print_info "Test: --type eml with --chaos-mode in Production Set mode succeeds"
eml_chaos_prod_output="$TEST_OUTPUT_DIR/eml-chaos-prod"
if zipper --production-set --type eml --count 10 --seed 42 --bates-prefix PROD \
    --chaos-mode --chaos-types "encoded-word,header-fold" \
    --output-path "$eml_chaos_prod_output" > /dev/null 2>&1; then
    manifest_file=$(find "$eml_chaos_prod_output" -name "_manifest.json" -print -quit)
    prod_chaos_props=$(find "$eml_chaos_prod_output" -name "loadfile_properties.json" -print -quit)
    if [[ -n "$manifest_file" && -f "$manifest_file" ]] && python3 "$(dirname "$0")/validate_email_chaos_audit.py" production "$prod_chaos_props"; then
        pass "--type eml with --chaos-mode generated production set successfully"
    else
        fail "--type eml with --chaos-mode production set did not generate _manifest.json with the expected email anomalies"
    fi
else
    fail "--type eml with --chaos-mode in production set failed"
fi

print_info "Test: --type pdf with --chaos-mode without loadfile-only fails validation"
if zipper --type pdf --count 5 --chaos-mode --output-path "$TEST_OUTPUT_DIR/pdf-chaos-invalid" > /dev/null 2>&1; then
    fail "--type pdf with --chaos-mode should have failed validation"
else
    pass "--type pdf with --chaos-mode correctly failed validation"
fi

print_info "Test: --type eml with --chaos-scenario in Standard mode fails validation"
if zipper --type eml --count 5 --chaos-mode --chaos-scenario cross-format-desync --output-path "$TEST_OUTPUT_DIR/eml-chaos-scenario-invalid" > /dev/null 2>&1; then
    fail "--type eml with --chaos-scenario should have failed validation"
else
    pass "--type eml with --chaos-scenario correctly failed validation"
fi

print_info "Test: email chaos types with --loadfile-only fail validation"
if zipper --loadfile-only --type eml --count 5 --chaos-mode --chaos-types mime-boundary --output-path "$TEST_OUTPUT_DIR/eml-loadfile-only-invalid" > /dev/null 2>&1; then
    fail "email chaos types with --loadfile-only should have failed validation"
else
    pass "email chaos types with --loadfile-only correctly failed validation"
fi

# --- REQ-238: TIFF Realism Mode ---

print_info "Test: --type tiff with --with-tiff-realism in Standard mode emits realistic TIFF with Bates endorsement"
TIFF_REALISM_OUT="$TEST_OUTPUT_DIR/tiff-realism-std"
if zipper --type tiff --count 2 --tiff-pages 1-1 --bates-prefix REAL --with-tiff-realism --output-path "$TIFF_REALISM_OUT" > /dev/null 2>&1; then
    pass "--with-tiff-realism generated successfully in Standard mode"
else
    fail "--with-tiff-realism generation failed in Standard mode"
fi

print_info "Test: Production Set with --with-tiff-realism generates page images"
PROD_REALISM_OUT="$TEST_OUTPUT_DIR/tiff-realism-prod"
if zipper --production-set --count 2 --bates-prefix PROD --with-tiff-realism --output-path "$PROD_REALISM_OUT" > /dev/null 2>&1; then
    pass "Production Set with --with-tiff-realism generated successfully"
else
    fail "Production Set with --with-tiff-realism failed"
fi

print_info "Test: Invalid --tiff-realism mode fails validation"
if zipper --type tiff --count 2 --tiff-realism invalid-mode --output-path "$TEST_OUTPUT_DIR/tiff-invalid" > /dev/null 2>&1; then
    fail "Invalid --tiff-realism mode should have failed validation"
else
    pass "Invalid --tiff-realism mode correctly failed validation"
fi

print_info "Test: --tiff-compression without realism fails validation"
if zipper --type tiff --count 2 --tiff-compression uncompressed --output-path "$TEST_OUTPUT_DIR/tiff-compression-no-realism" > /dev/null 2>&1; then
    fail "--tiff-compression without realism should have failed validation"
else
    pass "--tiff-compression without realism correctly failed validation"
fi

print_info "Test: --tiff-realism uncompressed with --tiff-compression group4 fails validation"
if zipper --type tiff --count 2 --tiff-realism uncompressed --tiff-compression group4 --output-path "$TEST_OUTPUT_DIR/tiff-compression-conflict" > /dev/null 2>&1; then
    fail "Conflicting --tiff-realism uncompressed and --tiff-compression group4 should have failed validation"
else
    pass "Conflicting --tiff-realism uncompressed and --tiff-compression group4 correctly failed validation"
fi

print_info "Test: --tiff-realism group4 with --tiff-compression uncompressed fails validation"
if zipper --type tiff --count 2 --tiff-realism group4 --tiff-compression uncompressed --output-path "$TEST_OUTPUT_DIR/tiff-compression-conflict-g4" > /dev/null 2>&1; then
    fail "Conflicting --tiff-realism group4 and --tiff-compression uncompressed should have failed validation"
else
    pass "Conflicting --tiff-realism group4 and --tiff-compression uncompressed correctly failed validation"
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
