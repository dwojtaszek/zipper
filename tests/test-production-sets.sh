#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/production-sets"
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

print_info "Running Production Sets E2E Test"

# Clean up previous test results
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# --- Test Case 1: Basic Production Set ---

print_info "Test Case 1: Basic production set generation"

zipper \
  --production-set \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test1" \
  --bates-prefix "PROD" \
  --volume-size 3

# Find production dir
prod_dir=$(find "$TEST_OUTPUT_DIR/test1" -type d -name "PRODUCTION_*" | head -n 1)

if [ -z "$prod_dir" ]; then
  print_error "Test 1: No production directory found."
fi

# Verify structure
if [ ! -d "$prod_dir/DATA" ]; then print_error "Missing DATA dir"; fi
if [ ! -d "$prod_dir/NATIVES" ]; then print_error "Missing NATIVES dir"; fi
if [ ! -d "$prod_dir/IMAGES" ]; then print_error "Missing IMAGES dir"; fi
if [ ! -d "$prod_dir/TEXT" ]; then print_error "Missing TEXT dir"; fi

# Verify load files exist
if [ ! -f "$prod_dir/DATA/loadfile.dat" ]; then print_error "Missing DAT load file"; fi
if [ ! -f "$prod_dir/DATA/loadfile.opt" ]; then print_error "Missing OPT load file"; fi
if [ ! -f "$prod_dir/_manifest.json" ]; then print_error "Missing manifest JSON"; fi

# Verify volumes (10 docs / 3 = 4 volumes)
vol1=$(find "$prod_dir/NATIVES" -name "VOL001")
vol4=$(find "$prod_dir/NATIVES" -name "VOL004")
if [ -z "$vol1" ]; then print_error "Missing VOL001"; fi
if [ -z "$vol4" ]; then print_error "Missing VOL004"; fi

# Verify DAT contents
if ! grep -q "PROD00000001" "$prod_dir/DATA/loadfile.dat"; then
  print_error "Bates start not found in DAT"
fi

print_success "Test Case 1: Basic production set passed"


# --- Test Case 2: Production ZIP ---

print_info "Test Case 2: Production set with --production-zip"

zipper \
  --production-set \
  --production-zip \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test2" \
  --bates-prefix "ZIP"

zip_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.zip" | head -n 1)

if [ -z "$zip_file" ]; then
  print_error "Test 2: No ZIP archive generated."
fi

# Make sure manifest exists and matches
prod_dir=$(find "$TEST_OUTPUT_DIR/test2" -type d -name "PRODUCTION_*" | head -n 1)
if [ ! -f "$prod_dir/_manifest.json" ]; then print_error "Missing manifest JSON"; fi

print_success "Test Case 2: Production ZIP passed"

# --- All Tests Passed ---

print_success "All Production Sets E2E tests passed!"
