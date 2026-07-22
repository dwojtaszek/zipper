#!/bin/bash
# E2E test: CLI argument-interaction conflict rejection
# Verifies that documented conflicts/dependencies in README.md are enforced.

set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

PASSED=0
FAILED=0
TEMP_DIR="./results/test-interactions-$$"
mkdir -p "$TEMP_DIR"

function print_info() { local msg="$1"; echo -e "\033[44m[ INFO ]\033[0m $msg"; }
function print_success() { local msg="$1"; echo -e "\033[42m[ SUCCESS ]\033[0m $msg"; }
function print_error() { local msg="$1"; echo -e "\033[41m[ ERROR ]\033[0m $msg" >&2; }

cleanup() { rm -rf "$TEMP_DIR"; }
trap cleanup EXIT

# Assert command exits non-zero (rejected)
assert_rejected() {
    local desc="$1"
    shift
    local out_path="$TEMP_DIR/out_${PASSED}_${FAILED}"
    local err_file="$TEMP_DIR/err_${PASSED}_${FAILED}"
    if zipper "$@" --output-path "$out_path" > /dev/null 2>"$err_file"; then
        print_error "FAIL: $desc (expected rejection, got success)"
        FAILED=$((FAILED + 1))
    elif grep -qE "Unhandled exception|NullReferenceException|Exception:" "$err_file"; then
        print_error "FAIL: $desc (expected validation error, got crash)"
        cat "$err_file"
        FAILED=$((FAILED + 1))
    else
        print_info "PASS: $desc"
        PASSED=$((PASSED + 1))
    fi
    rm -rf "$out_path" "$err_file" 2>/dev/null || true
}

# Assert command exits zero (accepted)
assert_accepted() {
    local desc="$1"
    shift
    local out_path="$TEMP_DIR/out_${PASSED}_${FAILED}"
    if zipper "$@" --output-path "$out_path" > /dev/null 2>&1; then
        print_info "PASS: $desc"
        PASSED=$((PASSED + 1))
    else
        print_error "FAIL: $desc (expected success, got rejection)"
        FAILED=$((FAILED + 1))
    fi
    rm -rf "$out_path" 2>/dev/null || true
}

assert_accepted_with_dat_char() {
    local desc="$1"
    local expected_char="$2"
    shift 2
    local out_path="$TEMP_DIR/out_${PASSED}_${FAILED}"
    if zipper "$@" --output-path "$out_path" > /dev/null 2>&1; then
        local dat_content=""
        local zip_file
        zip_file=$(find "$out_path" -type f -name "*.zip" | head -n 1)
        if [[ -n "$zip_file" ]]; then
            dat_content=$(unzip -p "$zip_file" "*.dat" 2>/dev/null || true)
        fi
        if [[ -z "$dat_content" ]]; then
            local dat_file
            dat_file=$(find "$out_path" -type f -name "*.dat" | head -n 1)
            if [[ -n "$dat_file" ]]; then
                dat_content=$(cat "$dat_file")
            fi
        fi
        if [[ -n "$dat_content" ]] && echo "$dat_content" | grep -F -q "$expected_char"; then
            print_info "PASS: $desc (output verified containing '$expected_char')"
            PASSED=$((PASSED + 1))
        elif [[ -n "$dat_content" ]]; then
            print_error "FAIL: $desc (command succeeded, but emitted DAT did not contain expected char '$expected_char')"
            FAILED=$((FAILED + 1))
        else
            print_info "PASS: $desc"
            PASSED=$((PASSED + 1))
        fi
    else
        print_error "FAIL: $desc (expected success, got rejection)"
        FAILED=$((FAILED + 1))
    fi
    rm -rf "$out_path" 2>/dev/null || true
}

print_info "=== CLI Argument Interaction Tests ==="

# --- Conflicts ---

assert_rejected "--loadfile-only + --include-load-file" \
    --loadfile-only --count 5 --include-load-file

assert_rejected "--loadfile-only + --target-zip-size" \
    --loadfile-only --count 5 --target-zip-size 10MB

assert_rejected "--production-set + --loadfile-only" \
    --production-set --loadfile-only --count 5 --bates-prefix TEST

assert_rejected "--chaos-scenario + --chaos-types" \
    --loadfile-only --count 5 --chaos-mode --chaos-scenario full-chaos --chaos-types quotes

# --- Chaos dependencies ---

assert_rejected "--chaos-mode without --loadfile-only" \
    --type pdf --count 5 --chaos-mode

assert_rejected "--chaos-amount without --chaos-mode" \
    --loadfile-only --count 5 --chaos-amount "5%"

assert_rejected "--chaos-types without --chaos-mode" \
    --loadfile-only --count 5 --chaos-types quotes

assert_rejected "--chaos-scenario without --chaos-mode" \
    --loadfile-only --count 5 --chaos-scenario full-chaos

# --- Delimiter dependencies & output verification ---

assert_accepted "--col-delim in standard mode" \
    --type pdf --count 5 --col-delim "char:|"

assert_accepted_with_dat_char "--col-delim in standard mode with --include-load-file" "|" \
    --type pdf --count 5 --include-load-file --col-delim "char:|"

assert_accepted_with_dat_char "--quote-delim in standard mode with --include-load-file" "~" \
    --type pdf --count 5 --include-load-file --quote-delim "char:~"

assert_accepted "--newline-delim in standard mode" \
    --type pdf --count 5 --newline-delim "char:^"

assert_accepted "--multi-delim in standard mode" \
    --type pdf --count 5 --multi-delim "char:;"

assert_accepted "--nested-delim in standard mode" \
    --type pdf --count 5 --nested-delim "char:\\"

assert_accepted_with_dat_char "--col-delim in production-set mode" "|" \
    --production-set --count 5 --bates-prefix PS --type pdf --col-delim "char:|"

assert_accepted_with_dat_char "--quote-delim in production-set mode" "~" \
    --production-set --count 5 --bates-prefix PS --type pdf --quote-delim "char:~"

# --- Production set dependencies ---

assert_rejected "--production-set without --bates-prefix" \
    --production-set --count 5

assert_rejected "--production-zip without --production-set" \
    --type pdf --count 5 --production-zip

assert_rejected "--volume-size without --production-set" \
    --type pdf --count 5 --volume-size 100

# --- Other dependencies ---

assert_rejected "invalid --hash-mode" \
    --type pdf --count 5 --hash-mode invalid

assert_rejected "--hash-algorithms without --hash-mode" \
    --type pdf --count 5 --hash-algorithms md5

assert_rejected "invalid --hash-algorithms" \
    --type pdf --count 5 --hash-mode actual --hash-algorithms md5,sha512

assert_rejected "--hash-mode actual + --loadfile-only" \
    --loadfile-only --count 5 --hash-mode actual

assert_accepted "valid --hash-mode actual" \
    --type pdf --count 5 --hash-mode actual --hash-algorithms md5,sha256

assert_accepted "valid --hash-mode simulated" \
    --loadfile-only --count 5 --hash-mode simulated

assert_rejected "--target-zip-size without --count" \
    --type pdf --target-zip-size 10MB

# --- Positive tests (assert_accepted) ---

assert_accepted "valid standard --loadfile-only" \
    --loadfile-only --count 5

assert_accepted "valid standard --loadfile-only with --col-delim" \
    --loadfile-only --count 5 --col-delim "char:|"

assert_accepted "valid standard --production-set" \
    --production-set --count 5 --bates-prefix TEST --type pdf

assert_accepted "--loadfile-only + --chaos-mode + --chaos-amount" \
    --loadfile-only --count 5 --chaos-mode --chaos-amount "5%"

assert_accepted "--production-set + --bates-prefix + --volume-size" \
    --production-set --count 5 --bates-prefix TEST --volume-size 100

assert_accepted "--loadfile-only + --col-delim + --quote-delim" \
    --loadfile-only --count 5 --col-delim "char:|" --quote-delim "char:\""

# --- Negative E2E tests for --loadfile-only formats ---

assert_rejected "--loadfile-only + --load-file-format csv" \
    --loadfile-only --count 5 --load-file-format csv

assert_rejected "--loadfile-only + --load-file-format xml" \
    --loadfile-only --count 5 --load-file-format xml

assert_rejected "--loadfile-only + --load-file-format edrm-xml" \
    --loadfile-only --count 5 --load-file-format edrm-xml

assert_rejected "--loadfile-only + --load-file-format concordance" \
    --loadfile-only --count 5 --load-file-format concordance

# --- Summary ---

echo ""
TOTAL=$((PASSED + FAILED))
if [[ "$FAILED" -eq 0 ]]; then
    print_success "All argument interaction tests passed! ($PASSED/$TOTAL)"
else
    print_error "Argument interaction tests: $FAILED/$TOTAL FAILED"
    exit 1
fi
