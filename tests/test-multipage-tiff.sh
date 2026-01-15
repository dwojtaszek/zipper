#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/multipage-tiff"
PROJECT="src/Zipper.csproj"

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

# Helper to extract page count from .dat line (handles DAT format delimiter)
function extract_page_count() {
  # DAT format uses \x14 delimiter, extract last numeric value
  echo "$1" | grep -oE '[0-9]+' | tail -n 1
}

# --- Test Setup ---

print_info "Running Multipage TIFF E2E Test"

# Clean up previous test results
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# --- Test Case 1: Single Page TIFF (default) ---

print_info "Test Case 1: Single page TIFF (default behavior)"

dotnet run --project "$PROJECT" -- \
  --type tiff \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test1"

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.zip")
dat_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.dat")

if [ -z "$zip_file" ]; then
  print_error "Test 1: No .zip file found"
fi
if [ -z "$dat_file" ]; then
  print_error "Test 1: No .dat file found"
fi

# Verify TIFF files were created
tif_count=$(unzip -l "$zip_file" | grep -c "\.tif" || true)
if [ "$tif_count" -lt 5 ]; then
  print_error "Test 1: Expected at least 5 TIFF files in zip, found $tif_count"
fi

print_success "Test Case 1: Single page TIFF passed"

# --- Test Case 2: TIFF Page Range 1-20 ---

print_info "Test Case 2: TIFF with page range 1-20"

dotnet run --project "$PROJECT" -- \
  --type tiff \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test2" \
  --tiff-pages "1-20"

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.zip")
dat_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.dat")

if [ -z "$zip_file" ]; then
  print_error "Test 2: No .zip file found"
fi
if [ -z "$dat_file" ]; then
  print_error "Test 2: No .dat file found"
fi

# Verify TIFF files were created
tif_count=$(unzip -l "$zip_file" | grep -c "\.tif" || true)
if [ "$tif_count" -lt 10 ]; then
  print_error "Test 2: Expected at least 10 TIFF files in zip, found $tif_count"
fi

# Check for Page Count column in header
first_line=$(head -n 1 "$dat_file")
if ! echo "$first_line" | grep -q "Page Count"; then
  print_error "Test 2: 'Page Count' column not found in .dat header"
fi

# Verify page counts are within range 1-20
tail -n +2 "$dat_file" | while IFS= read -r line; do
  page_count=$(extract_page_count "$line")
  if [ -z "$page_count" ] || [ "$page_count" -lt 1 ] || [ "$page_count" -gt 20 ]; then
    print_error "Test 2: Page count '$page_count' is outside range 1-20"
  fi
done

print_success "Test Case 2: TIFF page range 1-20 passed"

# --- Test Case 3: TIFF Page Range 5-10 ---

print_info "Test Case 3: TIFF with page range 5-10"

dotnet run --project "$PROJECT" -- \
  --type tiff \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test3" \
  --tiff-pages "5-10"

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test3" -name "*.zip")
dat_file=$(find "$TEST_OUTPUT_DIR/test3" -name "*.dat")

if [ -z "$zip_file" ]; then
  print_error "Test 3: No .zip file found"
fi
if [ -z "$dat_file" ]; then
  print_error "Test 3: No .dat file found"
fi

# Verify TIFF files were created
tif_count=$(unzip -l "$zip_file" | grep -c "\.tif" || true)
if [ "$tif_count" -lt 10 ]; then
  print_error "Test 3: Expected at least 10 TIFF files in zip, found $tif_count"
fi

# Verify page counts are within range 5-10
tail -n +2 "$dat_file" | while IFS= read -r line; do
  page_count=$(extract_page_count "$line")
  if [ -z "$page_count" ] || [ "$page_count" -lt 5 ] || [ "$page_count" -gt 10 ]; then
    print_error "Test 3: Page count '$page_count' is outside range 5-10"
  fi
done

print_success "Test Case 3: TIFF page range 5-10 passed"

# --- Test Case 4: TIFF Page Range with Bates Numbering ---

print_info "Test Case 4: TIFF with page range and Bates numbering"

dotnet run --project "$PROJECT" -- \
  --type tiff \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test4" \
  --tiff-pages "1-15" \
  --bates-prefix "TIFF" \
  --bates-start 1000 \
  --bates-digits 8

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test4" -name "*.zip")
dat_file=$(find "$TEST_OUTPUT_DIR/test4" -name "*.dat")

if [ -z "$zip_file" ]; then
  print_error "Test 4: No .zip file found"
fi
if [ -z "$dat_file" ]; then
  print_error "Test 4: No .dat file found"
fi

# Verify TIFF files were created
tif_count=$(unzip -l "$zip_file" | grep -c "\.tif" || true)
if [ "$tif_count" -lt 5 ]; then
  print_error "Test 4: Expected at least 5 TIFF files in zip, found $tif_count"
fi

# Check for both Bates Number and Page Count columns
first_line=$(head -n 1 "$dat_file")
if ! echo "$first_line" | grep -q "Bates Number"; then
  print_error "Test 4: 'Bates Number' column not found in .dat header"
fi

if ! echo "$first_line" | grep -q "Page Count"; then
  print_error "Test 4: 'Page Count' column not found in .dat header"
fi

# Verify Bates numbers
if ! grep -q "TIFF00001000" "$dat_file"; then
  print_error "Test 4: Bates number 'TIFF00001000' not found"
fi

# Verify page counts are within range 1-15
tail -n +2 "$dat_file" | while IFS= read -r line; do
  page_count=$(extract_page_count "$line")
  if [ -z "$page_count" ] || [ "$page_count" -lt 1 ] || [ "$page_count" -gt 15 ]; then
    print_error "Test 4: Page count '$page_count' is outside range 1-15"
  fi
done

print_success "Test Case 4: TIFF with page range and Bates numbering passed"

# --- Test Case 5: Deterministic Page Counts ---

print_info "Test Case 5: Verify deterministic page counts for same file index"

# Generate twice with same parameters
dotnet run --project "$PROJECT" -- \
  --type tiff \
  --count 3 \
  --output-path "$TEST_OUTPUT_DIR/test5a" \
  --tiff-pages "1-50"

dotnet run --project "$PROJECT" -- \
  --type tiff \
  --count 3 \
  --output-path "$TEST_OUTPUT_DIR/test5b" \
  --tiff-pages "1-50"

# Extract page counts from both runs
dat_file_a=$(find "$TEST_OUTPUT_DIR/test5a" -name "*.dat")
dat_file_b=$(find "$TEST_OUTPUT_DIR/test5b" -name "*.dat")

# Extract page counts using the helper function (handles DAT format delimiter correctly)
page_counts_a=$(tail -n +2 "$dat_file_a" | while IFS= read -r line; do extract_page_count "$line"; done)
page_counts_b=$(tail -n +2 "$dat_file_b" | while IFS= read -r line; do extract_page_count "$line"; done)

# Convert to arrays for comparison
counts_a=($page_counts_a)
counts_b=($page_counts_b)

# Verify page counts are the same (deterministic)
for i in "${!counts_a[@]}"; do
  if [ "${counts_a[$i]}" -ne "${counts_b[$i]}" ]; then
    print_error "Test 5: Page counts are not deterministic. Expected ${counts_a[$i]}, got ${counts_b[$i]}"
  fi
done

print_success "Test Case 5: Deterministic page counts verified"

# --- All Tests Passed ---

print_success "All Multipage TIFF E2E tests passed!"
