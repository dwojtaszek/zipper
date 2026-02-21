#!/bin/bash

# Basic E2E smoke suite — fast subset of tests for pre-push validation.
# Builds the binary ONCE and reuses it, eliminating per-test dotnet-run overhead.
#
# Covers: PDF, EML, TIFF, Bates numbering, load-file-in-zip
# Full suite: run-tests.sh (17 cases + 8 standalone suites)

set -e

# --- Configuration ---

TEST_OUTPUT_DIR="./results/e2e-basic"
PROJECT="src/Zipper.csproj"

# Dynamically locate the built framework directory
BUILD_DIR=$(find src/bin/Release -mindepth 1 -maxdepth 1 -type d -name "net*" | head -n 1)
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

function get_zip_listing() {
    local zip_file="$1"
    local cache_var="$2"
    local listing
    listing=$(unzip -l "$zip_file")
    printf -v "$cache_var" '%s' "$listing"
}

function verify_output() {
  local test_dir="$1"
  local expected_count="$2"
  local expected_header="$3"
  local file_type="$4"
  local check_text="$5"
  local encoding="${6:-UTF-8}"

  print_info "Verifying output in $test_dir"

  local zip_file
  zip_file=$(find "$test_dir" -name "*.zip")
  local dat_file
  dat_file=$(find "$test_dir" -name "*.dat")

  [ -z "$zip_file" ] && print_error "No .zip file found in $test_dir"
  [ -z "$dat_file" ] && print_error "No .dat file found in $test_dir"

  local zip_listing
  get_zip_listing "$zip_file" zip_listing

  local dat_content_cmd="cat"
  [ "$encoding" = "UTF-16" ] && dat_content_cmd="iconv -f UTF-16LE -t UTF-8"

  local dat_content
  dat_content=$($dat_content_cmd < "$dat_file")

  # Verify line count
  local line_count
  line_count=$(echo "$dat_content" | wc -l)
  line_count=$(echo "$line_count" | tr -d ' ')
  local expected_line_count=$((expected_count + 1))
  [ "$line_count" -ne "$expected_line_count" ] && \
    print_error "Line count: expected $expected_line_count, got $line_count"
  print_info ".dat line count OK ($line_count)"

  # Verify header
  local header
  header=$(echo "$dat_content" | head -n 1)
  IFS=',' read -ra cols <<< "$expected_header"
  for col in "${cols[@]}"; do
    echo "$header" | grep -q "$col" || \
      print_error "Header missing: '$col'"
  done
  print_info ".dat header OK"

  # Verify file count in zip
  local zip_file_count
  zip_file_count=$(echo "$zip_listing" | grep -c "\.$file_type") || true
  [ "$zip_file_count" -ne "$expected_count" ] && \
    print_error "Zip .$file_type count: expected $expected_count, got $zip_file_count"
  print_info ".zip .$file_type count OK ($zip_file_count)"

  # Verify text files if required
  if [ "$check_text" = "true" ]; then
    local txt_count
    if [ "$file_type" = "eml" ]; then
      txt_count=$(echo "$zip_listing" | grep "\.txt$" | grep -v "attachment" | wc -l)
    else
      txt_count=$(echo "$zip_listing" | grep -c "\.txt")
    fi
    [ "$txt_count" -ne "$expected_count" ] && \
      print_error "Zip .txt count: expected $expected_count, got $txt_count"
    print_info ".zip .txt count OK ($txt_count)"
  fi
}

function verify_load_file_included() {
    local test_dir="$1"
    local expected_count="$2"
    local expected_header="$3"
    local file_type="$4"

    local zip_file
    zip_file=$(find "$test_dir" -name "*.zip")
    [ -z "$zip_file" ] && print_error "No .zip file found in $test_dir"

    # No separate .dat should exist
    local dat_file
    dat_file=$(find "$test_dir" -name "*.dat")
    [ -n "$dat_file" ] && print_error "Found separate .dat file — should be inside zip"

    local zip_listing
    get_zip_listing "$zip_file" zip_listing

    # .dat inside zip
    local dat_in_zip
    dat_in_zip=$(echo "$zip_listing" | grep -c "\.dat$") || true
    [ "$dat_in_zip" -ne 1 ] && print_error "Expected 1 .dat in zip, found $dat_in_zip"
    print_info ".dat correctly inside zip"

    # Verify file count
    local zip_file_count
    zip_file_count=$(echo "$zip_listing" | grep -c "\.$file_type") || true
    [ "$zip_file_count" -ne "$expected_count" ] && \
      print_error "Zip .$file_type count: expected $expected_count, got $zip_file_count"
    print_info ".zip .$file_type count OK ($zip_file_count)"
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
    # Fallback: use dotnet run (slower)
    BINARY=("dotnet" "run" "--project" "$PROJECT" "--no-build" "-c" "Release" "--")
fi
print_info "Using binary: ${BINARY[*]}"

# --- Setup ---

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

function run_test() {
    local test_name="$1"
    shift
    print_info "START: $test_name"
    "${BINARY[@]}" "$@"
    print_info "END: $test_name"
}

# --- Smoke Test Cases ---

# 1. Basic PDF generation (core happy path)
run_test "Basic PDF generation" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_basic"
verify_output "$TEST_OUTPUT_DIR/pdf_basic" 10 "Control Number,File Path" "pdf" "false"
print_success "Test 1: Basic PDF — PASSED"

# 2. EML with attachments (complex format + attachment handling)
run_test "EML with attachments" --type eml --count 10 --output-path "$TEST_OUTPUT_DIR/eml_attach" --attachment-rate 50
verify_output "$TEST_OUTPUT_DIR/eml_attach" 10 "Control Number,File Path,To,From,Subject" "eml" "false"

# Explicitly verify attachments are in the zip
zip_file=$(find "$TEST_OUTPUT_DIR/eml_attach" -name "*.zip")
zip_listing=$(unzip -l "$zip_file")
attachment_count=$(echo "$zip_listing" | grep -c "attachment\.") || true
[ "$attachment_count" -eq 0 ] && print_error "Expected attachments in zip but found none"
print_info "Found $attachment_count attachments in zip"

print_success "Test 2: EML with attachments — PASSED"

# 3. TIFF with folders (folder distribution + image gen)
run_test "TIFF with folders" --type tiff --count 10 --output-path "$TEST_OUTPUT_DIR/tiff_folders" --folders 3
verify_output "$TEST_OUTPUT_DIR/tiff_folders" 10 "Control Number,File Path" "tiff" "false"
print_success "Test 3: TIFF with folders — PASSED"

# 4. Load file included in zip (edge case)
run_test "Include load file in zip" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_include_load" --include-load-file
verify_load_file_included "$TEST_OUTPUT_DIR/pdf_include_load" 10 "Control Number,File Path" "pdf"
print_success "Test 4: Load file in zip — PASSED"

# 5. Bates numbering (feature-specific)
run_test "Bates numbering" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_bates" --bates-prefix "SMOKE" --bates-start 1 --bates-digits 8
dat_file=$(find "$TEST_OUTPUT_DIR/pdf_bates" -name "*.dat")
[ -z "$dat_file" ] && print_error "No .dat file found for Bates test"
grep -q "SMOKE00000001" "$dat_file" || print_error "Bates number SMOKE00000001 not found"
grep -q "SMOKE00000010" "$dat_file" || print_error "Bates number SMOKE00000010 not found"
print_success "Test 5: Bates numbering — PASSED"

# --- Cleanup ---

print_info "Cleaning up..."
rm -rf "$TEST_OUTPUT_DIR"

print_success "All basic E2E smoke tests passed! (5/5)"
