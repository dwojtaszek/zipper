#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/load-file-formats"
PROJECT="Zipper/Zipper.csproj"

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

print_info "Running Load File Formats E2E Test"

# Clean up previous test results
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# --- Test Case 1: OPT Format ---

print_info "Test Case 1: OPT (tab-delimited) format"

dotnet run --project "$PROJECT" -- \
  --type pdf \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test1" \
  --load-file-format opt

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.zip")
opt_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.opt")

if [ -z "$zip_file" ]; then
  print_error "Test 1: No .zip file found"
fi

if [ -z "$opt_file" ]; then
  print_error "Test 1: No .opt file found"
fi

# Verify ZIP contains expected PDF files
pdf_count=$(unzip -l "$zip_file" 2>/dev/null | grep -c "\.pdf" || true)
if [ "$pdf_count" -lt 10 ]; then
  print_error "Test 1: Expected at least 10 PDF files in zip, found $pdf_count"
fi

# Check for tab delimiter (OPT uses tabs) - use $'\t' for cross-platform compatibility
if ! grep $'\t' "$opt_file" > /dev/null; then
  print_error "Test 1: No tab delimiter found in .opt file"
fi

# Verify header contains expected columns
first_line=$(head -n 1 "$opt_file")
if ! echo "$first_line" | grep -q "Control Number"; then
  print_error "Test 1: 'Control Number' column not found in .opt header"
fi

print_success "Test Case 1: OPT format passed"

# --- Test Case 2: CSV Format ---

print_info "Test Case 2: CSV format"

dotnet run --project "$PROJECT" -- \
  --type pdf \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test2" \
  --load-file-format csv

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.zip")
csv_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.csv")

if [ -z "$zip_file" ]; then
  print_error "Test 2: No .zip file found"
fi

if [ -z "$csv_file" ]; then
  print_error "Test 2: No .csv file found"
fi

# Verify ZIP contains expected PDF files
pdf_count=$(unzip -l "$zip_file" 2>/dev/null | grep -c "\.pdf" || true)
if [ "$pdf_count" -lt 10 ]; then
  print_error "Test 2: Expected at least 10 PDF files in zip, found $pdf_count"
fi

# Verify header contains expected columns
first_line=$(head -n 1 "$csv_file")
if ! echo "$first_line" | grep -q "Control Number"; then
  print_error "Test 2: 'Control Number' column not found in .csv header"
fi

# Check for comma delimiter
comma_count=$(head -n 1 "$csv_file" | grep -o "," | wc -l)
if [ "$comma_count" -lt 1 ]; then
  print_error "Test 2: Expected comma delimiters in .csv file"
fi

print_success "Test Case 2: CSV format passed"

# --- Test Case 3: XML Format ---

print_info "Test Case 3: XML format"

dotnet run --project "$PROJECT" -- \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test3" \
  --load-file-format xml

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test3" -name "*.zip")
xml_file=$(find "$TEST_OUTPUT_DIR/test3" -name "*.xml")

if [ -z "$zip_file" ]; then
  print_error "Test 3: No .zip file found"
fi

if [ -z "$xml_file" ]; then
  print_error "Test 3: No .xml file found"
fi

# Verify ZIP contains expected PDF files
pdf_count=$(unzip -l "$zip_file" 2>/dev/null | grep -c "\.pdf" || true)
if [ "$pdf_count" -lt 5 ]; then
  print_error "Test 3: Expected at least 5 PDF files in zip, found $pdf_count"
fi

# Verify XML structure
if ! grep -q "<?xml" "$xml_file"; then
  print_error "Test 3: XML declaration not found"
fi

if ! grep -q "<documents>" "$xml_file"; then
  print_error "Test 3: Root element <documents> not found"
fi

if ! grep -q "<document>" "$xml_file"; then
  print_error "Test 3: <document> element not found"
fi

if ! grep -q "<controlNumber>" "$xml_file"; then
  print_error "Test 3: <controlNumber> element not found"
fi

print_success "Test Case 3: XML format passed"

# --- Test Case 4: CONCORDANCE Format ---

print_info "Test Case 4: CONCORDANCE format"

dotnet run --project "$PROJECT" -- \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test4" \
  --load-file-format concordance

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test4" -name "*.zip")
concordance_file=$(find "$TEST_OUTPUT_DIR/test4" -name "*.dat")

if [ -z "$zip_file" ]; then
  print_error "Test 4: No .zip file found"
fi

if [ -z "$concordance_file" ]; then
  print_error "Test 4: No .dat file found"
fi

# Verify ZIP contains expected PDF files
pdf_count=$(unzip -l "$zip_file" 2>/dev/null | grep -c "\.pdf" || true)
if [ "$pdf_count" -lt 5 ]; then
  print_error "Test 4: Expected at least 5 PDF files in zip, found $pdf_count"
fi

# CONCORDANCE uses ASCII 20 as delimiter - check for CONTROLNUMBER header
if ! grep -q "CONTROLNUMBER" "$concordance_file"; then
  print_error "Test 4: 'CONTROLNUMBER' column not found in .dat header"
fi

print_success "Test Case 4: CONCORDANCE format passed"

# --- Test Case 5: Default DAT Format ---

print_info "Test Case 5: Default DAT format (with caret delimiter)"

dotnet run --project "$PROJECT" -- \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test5" \
  --load-file-format dat

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test5" -name "*.zip")
dat_file=$(find "$TEST_OUTPUT_DIR/test5" -name "*.dat")

if [ -z "$zip_file" ]; then
  print_error "Test 5: No .zip file found"
fi

if [ -z "$dat_file" ]; then
  print_error "Test 5: No .dat file found"
fi

# Verify ZIP contains expected PDF files
pdf_count=$(unzip -l "$zip_file" 2>/dev/null | grep -c "\.pdf" || true)
if [ "$pdf_count" -lt 5 ]; then
  print_error "Test 5: Expected at least 5 PDF files in zip, found $pdf_count"
fi

# Verify header contains expected columns
first_line=$(head -n 1 "$dat_file")
if ! echo "$first_line" | grep -q "Control Number"; then
  print_error "Test 5: 'Control Number' column not found in .dat header"
fi

print_success "Test Case 5: Default DAT format passed"

# --- Test Case 6: Load File Formats with Bates Numbering ---

print_info "Test Case 6: Load file formats with Bates numbering"

for format in "dat" "opt" "csv" "xml" "concordance"; do
  dotnet run --project "$PROJECT" -- \
    --type pdf \
    --count 3 \
    --output-path "$TEST_OUTPUT_DIR/test6_$format" \
    --load-file-format "$format" \
    --bates-prefix "TEST" \
    --bates-start 1 \
    --bates-digits 6

  # Find the load file
  case "$format" in
    "dat") ext="dat" ;;
    "opt") ext="opt" ;;
    "csv") ext="csv" ;;
    "xml") ext="xml" ;;
    "concordance") ext="dat" ;;
  esac

  load_file=$(find "$TEST_OUTPUT_DIR/test6_$format" -name "*.$ext")

  if [ -z "$load_file" ]; then
    print_error "Test 6: No .$ext file found for format $format"
  fi

  # Verify Bates number is present
  if ! grep -q "TEST" "$load_file"; then
    print_error "Test 6: Bates prefix 'TEST' not found in $format load file"
  fi

  print_success "Test Case 6: Bates numbering with $format format passed"
done

# --- All Tests Passed ---

print_success "All Load File Formats E2E tests passed!"
