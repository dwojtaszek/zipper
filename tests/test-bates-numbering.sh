#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

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

dotnet run --project "$PROJECT" -- \
  --type pdf \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test1" \
  --bates-prefix "TEST" \
  --bates-start 1 \
  --bates-digits 8

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.zip")
dat_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.dat")

if [ -z "$zip_file" ]; then
  print_error "Test 1: No .zip file found"
fi
if [ -z "$dat_file" ]; then
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

dotnet run --project "$PROJECT" -- \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test2" \
  --bates-prefix "CLIENT001" \
  --bates-start 100 \
  --bates-digits 6

# Verify output
dat_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.dat")

if ! grep -q "CLIENT001000100" "$dat_file"; then
  print_error "Test 2: Bates number 'CLIENT001000100' not found in .dat file"
fi

if ! grep -q "CLIENT001000104" "$dat_file"; then
  print_error "Test 2: Bates number 'CLIENT001000104' not found in .dat file"
fi

print_success "Test Case 2: Custom Bates configuration passed"

# --- Test Case 3: Bates with Different File Types ---

print_info "Test Case 3: Bates numbering with TIFF files"

dotnet run --project "$PROJECT" -- \
  --type tiff \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test3" \
  --bates-prefix "IMG" \
  --bates-start 1 \
  --bates-digits 8

# Verify output
dat_file=$(find "$TEST_OUTPUT_DIR/test3" -name "*.dat")

if ! grep -q "IMG00000001" "$dat_file"; then
  print_error "Test 3: Bates number 'IMG00000001' not found in .dat file"
fi

print_success "Test Case 3: Bates numbering with TIFF passed"

# --- Test Case 4: Bates with Office Formats ---

print_info "Test Case 4: Bates numbering with DOCX files"

dotnet run --project "$PROJECT" -- \
  --type docx \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test4" \
  --bates-prefix "DOCX" \
  --bates-start 500 \
  --bates-digits 10

# Verify output
dat_file=$(find "$TEST_OUTPUT_DIR/test4" -name "*.dat")

if ! grep -q "DOCX0000000500" "$dat_file"; then
  print_error "Test 4: Bates number 'DOCX0000000500' not found in .dat file"
fi

print_success "Test Case 4: Bates numbering with DOCX passed"

# --- All Tests Passed ---

print_success "All Bates Numbering E2E tests passed!"
