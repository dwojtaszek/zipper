#!/bin/bash

# E2E tests for --loadfile-only and Chaos Engine features.
# Builds the binary ONCE and reuses it.
#
# Covers: DAT loadfile-only, OPT loadfile-only, custom delimiters, EOL,
#         chaos mode, properties JSON, dependency rejection.

set -euo pipefail

# --- Configuration ---

TEST_OUTPUT_DIR="./results/e2e-loadfile"
PROJECT="src/Zipper.csproj"

# Dynamically locate the built framework directory
BUILD_DIR=$(find src/bin/Release -mindepth 1 -maxdepth 1 -type d -name "net*" -print -quit 2>/dev/null)
[[ -z "$BUILD_DIR" ]] && BUILD_DIR="src/bin/Release/net10.0" # Fallback

# --- Helper Functions ---

function print_success() {
  echo -e "\e[42m[ SUCCESS ]\e[0m $1"
}

function print_info() {
  echo -e "\e[44m[ INFO ]\e[0m $1"
}

function print_error() {
  echo -e "\e[41m[ ERROR ]\e[0m $1"
  exit 1
}

# --- Build Once ---

print_info "Building project (one-time)..."
dotnet build "$PROJECT" -c Release --nologo -v quiet 2>/dev/null || {
    echo "Build failed. Run 'dotnet build $PROJECT -c Release' for details."
    exit 1
}

# Resolve binary path
if [[ -f "$BUILD_DIR/Zipper" ]]; then
    BINARY=("$BUILD_DIR/Zipper")
elif [[ -f "$BUILD_DIR/Zipper.exe" ]]; then
    BINARY=("$BUILD_DIR/Zipper.exe")
else
    BINARY=("dotnet" "run" "--project" "$PROJECT" "--no-build" "-c" "Release" "--")
fi
print_info "Using binary: ${BINARY[*]}"

# --- Setup ---

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"
PASSED=0
TOTAL=0

function run_test() {
    local test_name="$1"
    shift
    print_info "START: $test_name"
    "${BINARY[@]}" "$@"
    print_info "END: $test_name"
}

# ================================================================
# Test 1: Basic DAT loadfile-only generation
# ================================================================
TOTAL=$((TOTAL + 1))
run_test "DAT loadfile-only" \
    --loadfile-only --count 100 --output-path "$TEST_OUTPUT_DIR/dat_basic"

dat_file=$(find "$TEST_OUTPUT_DIR/dat_basic" -name "*.dat" -print -quit)
[[ -z "$dat_file" ]] && print_error "No .dat file found"

# Verify line count (header + 100 data rows)
line_count=$(wc -l < "$dat_file" | tr -d ' ')
[[ "$line_count" -ne 101 ]] && print_error "DAT line count: expected 101, got $line_count"
print_info "DAT line count OK ($line_count)"

# Verify no ZIP was created
zip_count=$(find "$TEST_OUTPUT_DIR/dat_basic" -name "*.zip" -print -quit | wc -l)
[[ "$zip_count" -ne 0 ]] && print_error "Expected no .zip file in loadfile-only mode"
print_info "No ZIP file created (correct)"

# Verify properties JSON
props_file=$(find "$TEST_OUTPUT_DIR/dat_basic" -name "*_properties.json" -print -quit)
[[ -z "$props_file" ]] && print_error "No _properties.json file found"
grep -q '"format"' "$props_file" || print_error "Properties JSON missing Format field"
grep -q '"totalRecords"' "$props_file" || print_error "Properties JSON missing TotalRecords field"
grep -q '"delimiters"' "$props_file" || print_error "Properties JSON missing Delimiters field"
print_info "Properties JSON structure OK"

PASSED=$((PASSED + 1))
print_success "Test 1: Basic DAT loadfile-only — PASSED"

# ================================================================
# Test 2: OPT loadfile-only (Opticon 7-column format)
# ================================================================
TOTAL=$((TOTAL + 1))
run_test "OPT loadfile-only" \
    --loadfile-only --loadfile-format opt --count 50 --output-path "$TEST_OUTPUT_DIR/opt_basic"

opt_file=$(find "$TEST_OUTPUT_DIR/opt_basic" -name "*.opt" -print -quit)
[[ -z "$opt_file" ]] && print_error "No .opt file found"

# Verify no header (OPT has no header row, and documents expand to page level)
opt_line_count=$(wc -l < "$opt_file" | tr -d ' ')
[[ "$opt_line_count" -lt 50 ]] && print_error "OPT line count: expected at least 50, got $opt_line_count"

# Verify 7-column comma-separated format (6 commas per line)
bad_lines=0
while IFS= read -r line; do
    comma_count=$(echo "$line" | tr -cd ',' | wc -c)
    [[ "$comma_count" -ne 6 ]] && bad_lines=$((bad_lines + 1))
done < "$opt_file"
[[ "$bad_lines" -ne 0 ]] && print_error "$bad_lines OPT lines don't have 6 commas (7 columns)"
print_info "All OPT lines have correct 7-column format"

# Verify first line starts with Bates Number and has Y in doc-break position
first_line=$(head -n 1 "$opt_file" | sed $'s/^\xEF\xBB\xBF//')
echo "$first_line" | grep -q "^IMG" || print_error "OPT first line doesn't start with IMG prefix"
echo "$first_line" | cut -d',' -f4 | grep -q "Y" || print_error "OPT first line missing Y for doc-break"

PASSED=$((PASSED + 1))
print_success "Test 2: OPT loadfile-only — PASSED"

# ================================================================
# Test 3: Custom delimiters with strict prefix
# ================================================================
TOTAL=$((TOTAL + 1))
run_test "Custom delimiters" \
    --loadfile-only --count 20 --output-path "$TEST_OUTPUT_DIR/dat_custom_delim" \
    --col-delim "char:|" --quote-delim "char:\"" --eol LF

dat_file=$(find "$TEST_OUTPUT_DIR/dat_custom_delim" -name "*.dat" -print -quit)
[[ -z "$dat_file" ]] && print_error "No .dat file found"

# Verify pipe delimiter is present
grep -q "|" "$dat_file" || print_error "Pipe delimiter not found in output"
print_info "Pipe delimiter found OK"

# Verify LF line ending (no CR)
if od -c "$dat_file" | grep -q '\\r'; then
    print_error "Found CR in output, expected LF-only"
fi
print_info "LF line endings OK"

PASSED=$((PASSED + 1))
print_success "Test 3: Custom delimiters — PASSED"

# ================================================================
# Test 4: Chaos mode generates anomalies
# ================================================================
TOTAL=$((TOTAL + 1))
run_test "Chaos mode" \
    --loadfile-only --count 200 --output-path "$TEST_OUTPUT_DIR/dat_chaos" \
    --chaos-mode --chaos-amount "5%" --seed 12345

props_file=$(find "$TEST_OUTPUT_DIR/dat_chaos" -name "*_properties.json" -print -quit)
[[ -z "$props_file" ]] && print_error "No _properties.json file found for chaos test"

# Verify chaos section in properties JSON
grep -q '"enabled": true' "$props_file" || print_error "ChaosMode.Enabled not true in properties"
grep -q '"totalAnomalies"' "$props_file" || print_error "ChaosMode.TotalAnomalies missing"

# Extract anomaly count and verify it's > 0
anomaly_count=$(grep -o '"totalAnomalies": [0-9]*' "$props_file" | grep -o '[0-9]*$')
[[ "$anomaly_count" -eq 0 ]] && print_error "Expected anomalies but TotalAnomalies is 0"
print_info "Chaos anomalies injected: $anomaly_count"

# Verify InjectedAnomalies array exists
grep -q '"injectedAnomalies"' "$props_file" || print_error "Missing InjectedAnomalies array"

PASSED=$((PASSED + 1))
print_success "Test 4: Chaos mode — PASSED"

# ================================================================
# Test 5: Chaos with specific types filter
# ================================================================
TOTAL=$((TOTAL + 1))
run_test "Chaos with type filter" \
    --loadfile-only --count 100 --output-path "$TEST_OUTPUT_DIR/dat_chaos_typed" \
    --chaos-mode --chaos-amount "10" --chaos-types "quotes,columns" --seed 12345

props_file=$(find "$TEST_OUTPUT_DIR/dat_chaos_typed" -name "*_properties.json" -print -quit)
[[ -z "$props_file" ]] && print_error "No _properties.json file found"

# Verify only specified types appear
grep -Eq '"errorType": "(quotes|columns)"' "$props_file" || \
    print_error "Expected quotes or columns chaos anomalies in filtered output"
if grep -q '"errorType": "encoding"' "$props_file"; then
    print_error "Found 'encoding' chaos type despite not being in --chaos-types filter"
fi
if grep -q '"errorType": "eol"' "$props_file"; then
    print_error "Found 'eol' chaos type despite not being in --chaos-types filter"
fi
print_info "Chaos type filtering OK"

PASSED=$((PASSED + 1))
print_success "Test 5: Chaos type filter — PASSED"

# ================================================================
# Test 6: Rejection tests (dependency validation)
# ================================================================
TOTAL=$((TOTAL + 1))



# --chaos-amount without --chaos-mode should fail
if "${BINARY[@]}" --loadfile-only --count 10 --output-path "$TEST_OUTPUT_DIR/reject_3" --chaos-amount "5%" 2>/dev/null; then
    print_error "Should have rejected --chaos-amount without --chaos-mode"
fi
print_info "Rejected --chaos-amount without --chaos-mode"

# --loadfile-only with --target-zip-size should fail
if "${BINARY[@]}" --loadfile-only --count 10 --output-path "$TEST_OUTPUT_DIR/reject_4" --target-zip-size 100MB 2>/dev/null; then
    print_error "Should have rejected --loadfile-only with --target-zip-size"
fi
print_info "Rejected --loadfile-only with --target-zip-size"

# --col-delim without prefix should fail
if "${BINARY[@]}" --loadfile-only --count 10 --output-path "$TEST_OUTPUT_DIR/reject_5" --col-delim "20" 2>/dev/null; then
    print_error "Should have rejected --col-delim without ascii:/char: prefix"
fi
print_info "Rejected --col-delim without ascii:/char: prefix"

# --chaos-mode with --loadfile-format csv should fail
if "${BINARY[@]}" --loadfile-only --loadfile-format csv --count 10 --output-path "$TEST_OUTPUT_DIR/reject_6" --chaos-mode 2>/dev/null; then
    print_error "Should have rejected --chaos-mode with --loadfile-format csv"
fi
print_info "Rejected --chaos-mode with --loadfile-format csv"

# --chaos-amount with invalid format should fail
if "${BINARY[@]}" --loadfile-only --count 10 --output-path "$TEST_OUTPUT_DIR/reject_7" --chaos-mode --chaos-amount "abc" 2>/dev/null; then
    print_error "Should have rejected invalid --chaos-amount abc"
fi
print_info "Rejected invalid --chaos-amount abc"


PASSED=$((PASSED + 1))
print_success "Test 6: Dependency rejection — PASSED"

# ================================================================
# Test 7: Deterministic output with --seed
# ================================================================
TOTAL=$((TOTAL + 1))
run_test "Deterministic run 1" \
    --loadfile-only --count 20 --output-path "$TEST_OUTPUT_DIR/seed_run1" --seed 999

run_test "Deterministic run 2" \
    --loadfile-only --count 20 --output-path "$TEST_OUTPUT_DIR/seed_run2" --seed 999

dat1=$(find "$TEST_OUTPUT_DIR/seed_run1" -name "*.dat" -print -quit)
dat2=$(find "$TEST_OUTPUT_DIR/seed_run2" -name "*.dat" -print -quit)
[[ -z "$dat1" ]] && print_error "No .dat file found for seed_run1"
[[ -z "$dat2" ]] && print_error "No .dat file found for seed_run2"

# Compare content (files may have different timestamps in names, compare content only)
if ! diff <(cat "$dat1") <(cat "$dat2") > /dev/null; then
    print_error "Deterministic runs produced different output"
fi
print_info "Deterministic output confirmed"

PASSED=$((PASSED + 1))
print_success "Test 7: Deterministic output — PASSED"

# ================================================================
# Test 8: Active subprocess cancellation in Loadfile-Only mode (DAT+OPT)
# ================================================================
TOTAL=$((TOTAL + 1))
print_info "START: Active subprocess cancellation (DAT+OPT)"
CANCEL_OUT="$TEST_OUTPUT_DIR/cancel_loadfile"
mkdir -p "$CANCEL_OUT"
echo "sentinel data" > "$CANCEL_OUT/sentinel.txt"

CANCEL_PID=""
cleanup_cancel_lfo() {
  if [[ -n "$CANCEL_PID" ]]; then
    local cancel_pid="$CANCEL_PID"
    if ! kill -KILL -- "-$cancel_pid" 2>/dev/null; then
      kill -KILL "$cancel_pid" 2>/dev/null || true
    fi
    wait "$cancel_pid" 2>/dev/null || true
    CANCEL_PID=""
  fi
}
trap cleanup_cancel_lfo EXIT

set -m
"${BINARY[@]}" --loadfile-only --load-file-formats dat,opt --count 20000 --output-path "$CANCEL_OUT" >/dev/null 2>&1 &
CANCEL_PID=$!
set +m

CANCEL_SIGNAL_SENT=0
CANCEL_DEADLINE=$((SECONDS + 15))
while kill -0 "$CANCEL_PID" 2>/dev/null; do
  if compgen -G "$CANCEL_OUT/*.dat" >/dev/null && compgen -G "$CANCEL_OUT/*_properties.json" >/dev/null; then
    if kill -INT -- "-$CANCEL_PID" 2>/dev/null || kill -INT "$CANCEL_PID" 2>/dev/null; then
      CANCEL_SIGNAL_SENT=1
    fi
    break
  fi
  if [[ "$SECONDS" -ge "$CANCEL_DEADLINE" ]]; then
    print_error "Loadfile-only cancellation evidence did not appear within 15s"
  fi
  sleep 0.01
done

EXIT_DEADLINE=$((SECONDS + 15))
while kill -0 "$CANCEL_PID" 2>/dev/null; do
  if [[ "$SECONDS" -ge "$EXIT_DEADLINE" ]]; then
    if ! kill -KILL -- "-$CANCEL_PID" 2>/dev/null; then
      kill -KILL "$CANCEL_PID" 2>/dev/null || true
    fi
    print_error "Loadfile-only cancellation run did not exit within 15s"
  fi
  sleep 0.01
done

CANCEL_EXIT_CODE=0
wait "$CANCEL_PID" || CANCEL_EXIT_CODE=$?
CANCEL_PID=""
trap - EXIT

[[ "$CANCEL_SIGNAL_SENT" -ne 1 ]] && print_error "Signal was not sent before process exited"
[[ "$CANCEL_EXIT_CODE" -ne 130 ]] && print_error "Loadfile-only cancellation exit code expected 130, got $CANCEL_EXIT_CODE"

rem_dat=$(find "$CANCEL_OUT" -name "*.dat" | wc -l)
rem_opt=$(find "$CANCEL_OUT" -name "*.opt" | wc -l)
rem_json=$(find "$CANCEL_OUT" -name "*.json" | wc -l)
[[ "$rem_dat" -ne 0 ]] && print_error "Found $rem_dat leftover .dat files after cancellation"
[[ "$rem_opt" -ne 0 ]] && print_error "Found $rem_opt leftover .opt files after cancellation"
[[ "$rem_json" -ne 0 ]] && print_error "Found $rem_json leftover .json files after cancellation"

if [[ ! -f "$CANCEL_OUT/sentinel.txt" ]] || [[ "$(< "$CANCEL_OUT/sentinel.txt")" != "sentinel data" ]]; then
  print_error "Sentinel file was missing or modified after cancellation cleanup"
fi

PASSED=$((PASSED + 1))
print_success "Test 8: Active subprocess cancellation — PASSED"

# --- Cleanup ---

print_info "Cleaning up..."
rm -rf "$TEST_OUTPUT_DIR"

print_success "All loadfile-only E2E tests passed! ($PASSED/$TOTAL)"
