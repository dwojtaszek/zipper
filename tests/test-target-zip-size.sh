#!/bin/bash

# E2E test: --target-zip-size accuracy and padding spread
# Validates that generated archives hit their target size within committed tolerances
# and that padding is evenly distributed (no single file exceeds target/count * 10).

set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOLERANCES_FILE="$SCRIPT_DIR/fixtures/target-zip-size-tolerances.json"
TEST_OUTPUT_DIR="./results/target-zip-size"
PASSED=0
FAILED=0

# --- Output helpers ---

function print_info() { local msg="$1"; echo -e "\033[44m[ INFO ]\033[0m $msg"; }
function print_success() { local msg="$1"; echo -e "\033[42m[ SUCCESS ]\033[0m $msg"; }
function print_error() { local msg="$1"; echo -e "\033[41m[ ERROR ]\033[0m $msg" >&2; }

# --- Helpers ---

get_file_size() {
    local file="$1"
    if [[ "$(uname)" == "Darwin" ]]; then
        stat -f%z "$file"
    else
        stat -c%s "$file"
    fi
}

get_tolerance() {
    local key="$1"
    python3 -c "import json; print(json.load(open('$TOLERANCES_FILE'))['$key'])"
}

assert_size_within_tolerance() {
    local zip_file="$1"
    local target_bytes="$2"
    local tolerance="$3"
    local scenario="$4"

    local actual_bytes
    actual_bytes=$(get_file_size "$zip_file")

    local deviation
    deviation=$(python3 -c "print(abs($actual_bytes - $target_bytes) / $target_bytes)")

    if python3 -c "exit(0 if $deviation <= $tolerance else 1)"; then
        print_info "$scenario: size OK (actual=${actual_bytes}, target=${target_bytes}, deviation=${deviation}, tolerance=${tolerance})"
        PASSED=$((PASSED + 1))
    else
        print_error "$scenario: size FAILED (actual=${actual_bytes}, target=${target_bytes}, deviation=${deviation} > tolerance=${tolerance})"
        FAILED=$((FAILED + 1))
    fi
}

assert_padding_spread() {
    local zip_file="$1"
    local target_bytes="$2"
    local file_count="$3"
    local scenario="$4"

    local max_allowed_per_file=$((target_bytes * 10 / file_count))
    local temp_dir
    temp_dir=$(mktemp -d)

    unzip -q "$zip_file" -d "$temp_dir"

    local oversized=0
    while IFS= read -r -d '' file; do
        local fsize
        fsize=$(get_file_size "$file")
        if [[ "$fsize" -gt "$max_allowed_per_file" ]]; then
            oversized=$((oversized + 1))
            print_info "  Oversized file: $file (${fsize} > ${max_allowed_per_file})"
        fi
    done < <(find "$temp_dir" -type f -print0)

    rm -rf "$temp_dir"

    if [[ "$oversized" -eq 0 ]]; then
        print_info "$scenario: padding spread OK (no file > target/count*10)"
        PASSED=$((PASSED + 1))
    else
        print_error "$scenario: padding spread FAILED ($oversized files exceed target/count*10)"
        FAILED=$((FAILED + 1))
    fi
}

run_scenario() {
    local scenario="$1"
    local target_size="$2"
    local target_bytes="$3"
    local count="$4"
    local type="$5"
    local extra_args="${6:-}"
    local tolerance_key="$7"

    local out_dir="$TEST_OUTPUT_DIR/$scenario"
    rm -rf "$out_dir"
    mkdir -p "$out_dir"

    print_info "Running scenario: $scenario ($target_size, $count x $type)"

    # shellcheck disable=SC2086
    zipper --type "$type" --count "$count" --target-zip-size "$target_size" --output-path "$out_dir" $extra_args > /dev/null 2>&1

    local zip_file
    zip_file=$(find "$out_dir" -name "*.zip" -print -quit)

    if [[ -z "$zip_file" ]]; then
        print_error "$scenario: No zip file generated"
        FAILED=$((FAILED + 2))
        return
    fi

    local tolerance
    tolerance=$(get_tolerance "$tolerance_key")

    assert_size_within_tolerance "$zip_file" "$target_bytes" "$tolerance" "$scenario"
    assert_padding_spread "$zip_file" "$target_bytes" "$count" "$scenario"
}

# --- Clean up ---
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# --- Default-tier matrix (always runs) ---

print_info "=== Default-tier target-zip-size tests ==="

run_scenario "10MB_100_pdf" "10MB" "$((10 * 1024 * 1024))" 100 pdf "" "10MB_100_pdf"
run_scenario "100MB_1000_pdf" "100MB" "$((100 * 1024 * 1024))" 1000 pdf "" "100MB_1000_pdf"
run_scenario "100MB_500_eml" "100MB" "$((100 * 1024 * 1024))" 500 eml "--attachment-rate 30" "100MB_500_eml"
run_scenario "100MB_500_tiff" "100MB" "$((100 * 1024 * 1024))" 500 tiff "" "100MB_500_tiff"

# --- Slow cases (gated by RUN_SLOW_TESTS) ---

if [[ "${RUN_SLOW_TESTS:-0}" == "1" ]]; then
    print_info "=== Slow-tier target-zip-size tests (RUN_SLOW_TESTS=1) ==="

    run_scenario "500MB_5000_pdf" "500MB" "$((500 * 1024 * 1024))" 5000 pdf "" "500MB_5000_pdf"
    run_scenario "1GB_10000_pdf" "1GB" "$((1024 * 1024 * 1024))" 10000 pdf "" "1GB_10000_pdf"
else
    print_info "Skipping slow-tier tests (set RUN_SLOW_TESTS=1 to enable)"
fi

# --- Summary ---

echo ""
TOTAL=$((PASSED + FAILED))
if [[ "$FAILED" -eq 0 ]]; then
    print_success "All target-zip-size tests passed! ($PASSED/$TOTAL)"
else
    print_error "target-zip-size tests: $FAILED/$TOTAL FAILED"
    exit 1
fi
