#!/bin/bash

# Exit immediately if a command exits with a non-zero status, use unset variable as error, and fail on pipe failures.
set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/bates-numbering"
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

# --- Test Setup ---

print_info "Running Bates Numbering E2E Test"

# Clean up previous test results
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# --- Test Case 1: Basic Bates Numbering ---

print_info "Test Case 1: Basic Bates numbering with default prefix"

zipper \
  --type pdf \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test1" \
  --bates-prefix "TEST" \
  --bates-start 1 \
  --bates-digits 8

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.zip" -print -quit)
dat_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.dat" -print -quit)

if [[ -z "$zip_file" ]]; then
  print_error "Test 1: No .zip file found"
fi
if [[ -z "$dat_file" ]]; then
  print_error "Test 1: No .dat file found"
fi

# Check for Bates numbers in load file
if ! grep -q "TEST00000001" "$dat_file"; then
  print_error "Test 1: Bates number 'TEST00000001' not found in .dat file"
fi

if ! grep -q "TEST00000010" "$dat_file"; then
  print_error "Test 1: Bates number 'TEST00000010' not found in .dat file"
fi

# Verify Bates Number column exists
first_line=$(head -n 1 "$dat_file")
if ! echo "$first_line" | grep -q "Bates Number"; then
  print_error "Test 1: 'Bates Number' column not found in .dat header"
fi

print_success "Test Case 1: Basic Bates numbering passed"

# --- Test Case 2: Custom Bates Configuration ---

print_info "Test Case 2: Custom Bates prefix, start, and digits"

zipper \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test2" \
  --bates-prefix "CLIENT001" \
  --bates-start 100 \
  --bates-digits 6

# Verify output
dat_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.dat" -print -quit)

if ! grep -q "CLIENT001000100" "$dat_file"; then
  print_error "Test 2: Bates number 'CLIENT001000100' not found in .dat file"
fi

if ! grep -q "CLIENT001000104" "$dat_file"; then
  print_error "Test 2: Bates number 'CLIENT001000104' not found in .dat file"
fi

print_success "Test Case 2: Custom Bates configuration passed"

# --- Test Case 3: Bates with Different File Types ---

print_info "Test Case 3: Bates numbering with TIFF files"

zipper \
  --type tiff \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test3" \
  --bates-prefix "IMG" \
  --bates-start 1 \
  --bates-digits 8

# Verify output
dat_file=$(find "$TEST_OUTPUT_DIR/test3" -name "*.dat" -print -quit)

if ! grep -q "IMG00000001" "$dat_file"; then
  print_error "Test 3: Bates number 'IMG00000001' not found in .dat file"
fi

print_success "Test Case 3: Bates numbering with TIFF passed"

# --- Test Case 4: Bates with Office Formats ---

print_info "Test Case 4: Bates numbering with DOCX files"

zipper \
  --type docx \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test4" \
  --bates-prefix "DOCX" \
  --bates-start 500 \
  --bates-digits 10

# Verify output
dat_file=$(find "$TEST_OUTPUT_DIR/test4" -name "*.dat" -print -quit)

if ! grep -q "DOCX0000000500" "$dat_file"; then
  print_error "Test 4: Bates number 'DOCX0000000500' not found in .dat file"
fi

print_success "Test Case 4: Bates numbering with DOCX passed"

# --- Test Case 5: Page-level Bates suffix on one-page documents ---

print_info "Test Case 5: Page-level Bates suffix (--bates-page-suffix)"

zipper \
  --type pdf \
  --count 3 \
  --output-path "$TEST_OUTPUT_DIR/test5" \
  --bates-prefix "PGS" \
  --bates-page-suffix \
  --load-file-format opt

# Verify output
opt_file=$(find "$TEST_OUTPUT_DIR/test5" -name "*.opt" -print -quit)

if [[ -z "$opt_file" ]]; then
  print_error "Test 5: No .opt file found"
fi

if ! grep -q "^PGS00000001_001," "$opt_file"; then
  print_error "Test 5: Page-level Bates 'PGS00000001_001' not found in .opt file"
fi

if grep -q "^PGS00000001," "$opt_file"; then
  print_error "Test 5: Document-level Bates 'PGS00000001' should not be an OPT key with --bates-page-suffix"
fi

print_success "Test Case 5: Page-level Bates suffix passed"

# --- Test Case 6: Over-limit Bates configuration warns and still generates ---

print_info "Test Case 6: Bates prefix over the Everlaw limit warns"

if ! warn_output=$(zipper \
  --type pdf \
  --count 2 \
  --output-path "$TEST_OUTPUT_DIR/test6" \
  --bates-prefix "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDE" 2>&1); then
  print_error "Test 6: zipper failed on an over-limit Bates prefix (limits must warn, not fail)"
fi

if ! grep -q "Warning: Bates prefix 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDE' is 41 characters" <<< "$warn_output"; then
  print_error "Test 6: Everlaw prefix warning not printed"
fi

print_success "Test Case 6: Over-limit Bates prefix warning passed"

# --- Test Case 7: Page-suffix width warning follows the pages each output numbers ---

print_info "Test Case 7: Page-suffix width warning covers Loadfile-Only pages only"

if ! pdf_output=$(zipper \
  --type pdf \
  --count 1 \
  --output-path "$TEST_OUTPUT_DIR/test7-pdf" \
  --bates-prefix "CL001" \
  --bates-digits 12 \
  --bates-page-suffix \
  --tiff-pages 1-1000 2>&1); then
  print_error "Test 7: zipper failed on a PDF run with --tiff-pages past 999"
fi

if grep -q "Warning: Bates numbers are" <<< "$pdf_output"; then
  print_error "Test 7: PDF run should not warn about a page suffix it never numbers"
fi

if ! loadfile_output=$(zipper \
  --type pdf \
  --count 1 \
  --loadfile-only \
  --output-path "$TEST_OUTPUT_DIR/test7-loadfile" \
  --load-file-format opt \
  --bates-prefix "CL001" \
  --bates-digits 12 \
  --bates-page-suffix \
  --tiff-pages 1000-1000 2>&1); then
  print_error "Test 7: zipper failed on a Loadfile-Only run with --tiff-pages 1000"
fi

if ! grep -q "Warning: Bates numbers are 17 characters including the page suffix" <<< "$loadfile_output"; then
  print_error "Test 7: Loadfile-Only page suffix width warning not printed"
fi

print_success "Test Case 7: Page-suffix width warning passed"

# --- Test Case 8: Page-suffix width warning needs a page label in the output ---

print_info "Test Case 8: Page-suffix width warning needs a page label in the output"

if ! plain_dat_output=$(zipper \
  --type tiff \
  --count 1 \
  --output-path "$TEST_OUTPUT_DIR/test8-plain-dat" \
  --bates-prefix "CL001" \
  --bates-digits 12 \
  --bates-page-suffix \
  --tiff-pages 1000-1000 \
  --loadfile-format dat 2>&1); then
  print_error "Test 8: zipper failed on a plain TIFF DAT run with --tiff-pages 1000"
fi

if grep -q "Warning: Bates numbers are" <<< "$plain_dat_output"; then
  print_error "Test 8: plain TIFF DAT run should not warn about a page suffix it never writes"
fi

if ! realistic_dat_output=$(zipper \
  --type tiff \
  --count 1 \
  --output-path "$TEST_OUTPUT_DIR/test8-realistic-dat" \
  --bates-prefix "CL001" \
  --bates-digits 12 \
  --bates-page-suffix \
  --tiff-pages 1000-1000 \
  --loadfile-format dat \
  --with-tiff-realism 2>&1); then
  print_error "Test 8: zipper failed on a realistic TIFF DAT run with --tiff-pages 1000"
fi

if ! grep -q "Warning: Bates numbers are 17 characters including the page suffix" <<< "$realistic_dat_output"; then
  print_error "Test 8: realistic TIFF endorses page labels, so the page suffix width warning should print"
fi

print_success "Test Case 8: Page-suffix width warning needs a page label in the output passed"

# --- All Tests Passed ---

print_success "All Bates Numbering E2E tests passed!"
