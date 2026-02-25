#!/bin/bash

# E2E tests for --loadfile-only and Chaos Engine features.
# Builds the binary ONCE and reuses it.
#
# Covers: DAT loadfile-only, OPT loadfile-only, custom delimiters, EOL,
#         chaos mode, properties JSON, dependency rejection.

set -e

# --- Configuration ---

TEST_OUTPUT_DIR="./results/e2e-loadfile"
PROJECT="src/Zipper.csproj"

# Dynamically locate the built framework directory
BUILD_DIR=$(find src/bin/Release -mindepth 1 -maxdepth 1 -type d -name "net*" 2>/dev/null | head -n 1)
[ -z "$BUILD_DIR" ] && BUILD_DIR="src/bin/Release/net8.0" # Fallback

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
if [ -f "$BUILD_DIR/Zipper" ]; then
    BINARY=("$BUILD_DIR/Zipper")
elif [ -f "$BUILD_DIR/Zipper.exe" ]; then
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

dat_file=$(find "$TEST_OUTPUT_DIR/dat_basic" -name "*.dat")
[ -z "$dat_file" ] && print_error "No .dat file found"

# Verify line count (header + 100 data rows)
line_count=$(wc -l < "$dat_file" | tr -d ' ')
[ "$line_count" -ne 101 ] && print_error "DAT line count: expected 101, got $line_count"
print_info "DAT line count OK ($line_count)"

# Verify no ZIP was created
zip_count=$(find "$TEST_OUTPUT_DIR/dat_basic" -name "*.zip" | wc -l)
[ "$zip_count" -ne 0 ] && print_error "Expected no .zip file in loadfile-only mode"
print_info "No ZIP file created (correct)"

# Verify properties JSON
props_file=$(find "$TEST_OUTPUT_DIR/dat_basic" -name "*_properties.json")
[ -z "$props_file" ] && print_error "No _properties.json file found"
grep -q '"Format"' "$props_file" || print_error "Properties JSON missing Format field"
grep -q '"TotalRecords"' "$props_file" || print_error "Properties JSON missing TotalRecords field"
grep -q '"Delimiters"' "$props_file" || print_error "Properties JSON missing Delimiters field"
print_info "Properties JSON structure OK"

PASSED=$((PASSED + 1))
print_success "Test 1: Basic DAT loadfile-only — PASSED"

# ================================================================
# Test 2: OPT loadfile-only (Opticon 7-column format)
# ================================================================
TOTAL=$((TOTAL + 1))
run_test "OPT loadfile-only" \
    --loadfile-only --loadfile-format opt --count 50 --output-path "$TEST_OUTPUT_DIR/opt_basic"

opt_file=$(find "$TEST_OUTPUT_DIR/opt_basic" -name "*.opt")
[ -z "$opt_file" ] && print_error "No .opt file found"

# Verify no header (OPT has no header row)
opt_line_count=$(wc -l < "$opt_file" | tr -d ' ')
[ "$opt_line_count" -ne 50 ] && print_error "OPT line count: expected 50, got $opt_line_count"

# Verify 7-column comma-separated format (6 commas per line)
bad_lines=0
while IFS= read -r line; do
    comma_count=$(echo "$line" | tr -cd ',' | wc -c)
    [ "$comma_count" -ne 6 ] && bad_lines=$((bad_lines + 1))
done < "$opt_file"
[ "$bad_lines" -ne 0 ] && print_error "$bad_lines OPT lines don't have 6 commas (7 columns)"
print_info "All OPT lines have correct 7-column format"

# Verify first line starts with BatesID and has Y in doc-break position
first_line=$(head -n 1 "$opt_file")
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

dat_file=$(find "$TEST_OUTPUT_DIR/dat_custom_delim" -name "*.dat")
[ -z "$dat_file" ] && print_error "No .dat file found"

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
    --chaos-mode --chaos-amount "5%" --seed 42

props_file=$(find "$TEST_OUTPUT_DIR/dat_chaos" -name "*_properties.json")
[ -z "$props_file" ] && print_error "No _properties.json file found for chaos test"

# Verify chaos section in properties JSON
grep -q '"Enabled": true' "$props_file" || print_error "ChaosMode.Enabled not true in properties"
grep -q '"TotalAnomalies"' "$props_file" || print_error "ChaosMode.TotalAnomalies missing"

# Extract anomaly count and verify it's > 0
anomaly_count=$(grep -o '"TotalAnomalies": [0-9]*' "$props_file" | grep -o '[0-9]*$')
[ "$anomaly_count" -eq 0 ] && print_error "Expected anomalies but TotalAnomalies is 0"
print_info "Chaos anomalies injected: $anomaly_count"

# Verify InjectedAnomalies array exists
grep -q '"InjectedAnomalies"' "$props_file" || print_error "Missing InjectedAnomalies array"

PASSED=$((PASSED + 1))
print_success "Test 4: Chaos mode — PASSED"

# ================================================================
# Test 5: Chaos with specific types filter
# ================================================================
TOTAL=$((TOTAL + 1))
run_test "Chaos with type filter" \
    --loadfile-only --count 100 --output-path "$TEST_OUTPUT_DIR/dat_chaos_typed" \
    --chaos-mode --chaos-amount "10" --chaos-types "quotes,columns" --seed 42

props_file=$(find "$TEST_OUTPUT_DIR/dat_chaos_typed" -name "*_properties.json")
[ -z "$props_file" ] && print_error "No _properties.json file found"

# Verify only specified types appear
if grep -q '"encoding"' "$props_file"; then
    print_error "Found 'encoding' chaos type despite not being in --chaos-types filter"
fi
if grep -q '"eol"' "$props_file"; then
    print_error "Found 'eol' chaos type despite not being in --chaos-types filter"
fi
print_info "Chaos type filtering OK"

PASSED=$((PASSED + 1))
print_success "Test 5: Chaos type filter — PASSED"

# ================================================================
# Test 6: Rejection tests (dependency validation)
# ================================================================
TOTAL=$((TOTAL + 1))

# --col-delim without --loadfile-only should fail
if "${BINARY[@]}" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/reject_1" --col-delim "ascii:20" 2>/dev/null; then
    print_error "Should have rejected --col-delim without --loadfile-only"
fi
print_info "Rejected --col-delim without --loadfile-only"

# --chaos-mode without --loadfile-only should fail
if "${BINARY[@]}" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/reject_2" --chaos-mode 2>/dev/null; then
    print_error "Should have rejected --chaos-mode without --loadfile-only"
fi
print_info "Rejected --chaos-mode without --loadfile-only"

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

dat1=$(find "$TEST_OUTPUT_DIR/seed_run1" -name "*.dat")
dat2=$(find "$TEST_OUTPUT_DIR/seed_run2" -name "*.dat")
[ -z "$dat1" ] && print_error "No .dat file found for seed_run1"
[ -z "$dat2" ] && print_error "No .dat file found for seed_run2"

# Compare content (files may have different timestamps in names, compare content only)
if ! diff <(cat "$dat1") <(cat "$dat2") > /dev/null; then
    print_error "Deterministic runs produced different output"
fi
print_info "Deterministic output confirmed"

PASSED=$((PASSED + 1))
print_success "Test 7: Deterministic output — PASSED"

# --- Cleanup ---

print_info "Cleaning up..."
rm -rf "$TEST_OUTPUT_DIR"

print_success "All loadfile-only E2E tests passed! ($PASSED/$TOTAL)"
