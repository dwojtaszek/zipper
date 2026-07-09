#!/bin/bash

# Exit immediately if a command exits with a non-zero status, use unset variable as error, and fail on pipe failures.
set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/load-file-formats"
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

print_info "Running Load File Formats E2E Test"

# Clean up previous test results
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# --- Test Case 1: OPT Format ---

print_info "Test Case 1: OPT (comma-delimited, no-header) format"

zipper \
  --type pdf \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test1" \
  --load-file-format opt

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.zip" -print -quit)
opt_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.opt" -print -quit)

if [[ -z "$zip_file" ]]; then
  print_error "Test 1: No .zip file found"
fi

if [[ -z "$opt_file" ]]; then
  print_error "Test 1: No .opt file found"
fi

# Verify ZIP contains expected PDF files
pdf_count=$(unzip -l "$zip_file" 2>/dev/null | grep -c "\.pdf" || true)
if [[ "$pdf_count" -lt 10 ]]; then
  print_error "Test 1: Expected at least 10 PDF files in zip, found $pdf_count"
fi

# Check for comma delimiter (OPT uses comma per Opticon 7-column standard)
if ! grep ',' "$opt_file" > /dev/null; then
  print_error "Test 1: No comma delimiter found in .opt file"
fi

# Verify first line is a data row (OPT has no header), not a header row with "Control Number"
first_line=$(head -n 1 "$opt_file")
if echo "$first_line" | grep -q "Control Number"; then
  print_error "Test 1: OPT should not contain header row, found 'Control Number'"
fi

print_success "Test Case 1: OPT format passed"

# --- Test Case 2: CSV Format ---

print_info "Test Case 2: CSV format"

zipper \
  --type pdf \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test2" \
  --load-file-format csv

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.zip" -print -quit)
csv_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.csv" -print -quit)

if [[ -z "$zip_file" ]]; then
  print_error "Test 2: No .zip file found"
fi

if [[ -z "$csv_file" ]]; then
  print_error "Test 2: No .csv file found"
fi

# Verify ZIP contains expected PDF files
pdf_count=$(unzip -l "$zip_file" 2>/dev/null | grep -c "\.pdf" || true)
if [[ "$pdf_count" -lt 10 ]]; then
  print_error "Test 2: Expected at least 10 PDF files in zip, found $pdf_count"
fi

# Verify header contains expected columns
first_line=$(head -n 1 "$csv_file")
if ! echo "$first_line" | grep -qi "Control Number"; then
  print_error "Test 2: 'Control Number' column not found in .csv header"
fi

# Check for comma delimiter
comma_count=$(head -n 1 "$csv_file" | grep -o "," | wc -l)
if [[ "$comma_count" -lt 1 ]]; then
  print_error "Test 2: Expected comma delimiters in .csv file"
fi

print_success "Test Case 2: CSV format passed"

# --- Test Case 3: XML Format ---

print_info "Test Case 3: XML format"

zipper \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test3" \
  --load-file-format xml

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test3" -name "*.zip" -print -quit)
xml_file=$(find "$TEST_OUTPUT_DIR/test3" -name "*.xml" -print -quit)

if [[ -z "$zip_file" ]]; then
  print_error "Test 3: No .zip file found"
fi

if [[ -z "$xml_file" ]]; then
  print_error "Test 3: No .xml file found"
fi

# Verify ZIP contains expected PDF files
pdf_count=$(unzip -l "$zip_file" 2>/dev/null | grep -c "\.pdf" || true)
if [[ "$pdf_count" -lt 5 ]]; then
  print_error "Test 3: Expected at least 5 PDF files in zip, found $pdf_count"
fi

# Verify XML structure
if ! grep -q "<?xml" "$xml_file"; then
  print_error "Test 3: XML declaration not found"
fi

if ! grep -q "<Root DataInterchangeType=" "$xml_file"; then
  print_error "Test 3: Root element <Root DataInterchangeType=...> not found"
fi

if ! grep -q "<Batch>" "$xml_file"; then
  print_error "Test 3: <Batch> element not found"
fi

if grep -q "<Documents>" "$xml_file"; then
  print_error "Test 3: Plural wrapper <Documents> element should not be present"
fi

if ! grep -q "<Document DocID=" "$xml_file"; then
  print_error "Test 3: <Document DocID=...> element not found"
fi

print_success "Test Case 3: XML format passed"

# --- Test Case 4: CONCORDANCE Format ---

print_info "Test Case 4: CONCORDANCE format"

zipper \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test4" \
  --load-file-format concordance

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test4" -name "*.zip" -print -quit)
concordance_file=$(find "$TEST_OUTPUT_DIR/test4" -name "*.dat" -print -quit)

if [[ -z "$zip_file" ]]; then
  print_error "Test 4: No .zip file found"
fi

if [[ -z "$concordance_file" ]]; then
  print_error "Test 4: No .dat file found"
fi

# Verify ZIP contains expected PDF files
pdf_count=$(unzip -l "$zip_file" 2>/dev/null | grep -c "\.pdf" || true)
if [[ "$pdf_count" -lt 5 ]]; then
  print_error "Test 4: Expected at least 5 PDF files in zip, found $pdf_count"
fi

# CONCORDANCE uses ASCII 20 as delimiter - check for CONTROLNUMBER header
if ! grep -q "CONTROLNUMBER" "$concordance_file"; then
  print_error "Test 4: 'CONTROLNUMBER' column not found in .dat header"
fi

print_success "Test Case 4: CONCORDANCE format passed"

# --- Test Case 5: Default DAT Format ---

print_info "Test Case 5: Default DAT format (with caret delimiter)"

zipper \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test5" \
  --load-file-format dat

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test5" -name "*.zip" -print -quit)
dat_file=$(find "$TEST_OUTPUT_DIR/test5" -name "*.dat" -print -quit)

if [[ -z "$zip_file" ]]; then
  print_error "Test 5: No .zip file found"
fi

if [[ -z "$dat_file" ]]; then
  print_error "Test 5: No .dat file found"
fi

# Verify ZIP contains expected PDF files
pdf_count=$(unzip -l "$zip_file" 2>/dev/null | grep -c "\.pdf" || true)
if [[ "$pdf_count" -lt 5 ]]; then
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
  zipper \
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

  load_file=$(find "$TEST_OUTPUT_DIR/test6_$format" -name "*.$ext" -print -quit)

  if [[ -z "$load_file" ]]; then
    print_error "Test 6: No .$ext file found for format $format"
  fi

  # Verify Bates number is present
  if ! grep -q "TEST" "$load_file"; then
    print_error "Test 6: Bates prefix 'TEST' not found in $format load file"
  fi

  print_success "Test Case 6: Bates numbering with $format format passed"
done

# --- Test Case 7: Custom Delimiters (Pipe and Caret) ---

print_info "Test Case 7: Custom delimiters (pipe and caret)"

zipper \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test7" \
  --load-file-format dat \
  --delimiter-column "|" \
  --delimiter-quote "^"

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test7" -name "*.zip" -print -quit)
dat_file=$(find "$TEST_OUTPUT_DIR/test7" -name "*.dat" -print -quit)

if [[ -z "$dat_file" ]]; then
  print_error "Test 7: No .dat file found"
fi

# Check for pipe delimiter
if ! grep -q "|" "$dat_file"; then
  print_error "Test 7: No pipe delimiter found in .dat file"
fi

# Check for caret quote
if ! grep -q "\^" "$dat_file"; then
  print_error "Test 7: No caret quote found in .dat file"
fi

print_success "Test Case 7: Custom delimiters passed"

# --- Test Case 8: ASCII Code Delimiters ---

print_info "Test Case 8: ASCII code delimiters (20, 254)"

zipper \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test8" \
  --load-file-format dat \
  --delimiter-column "20" \
  --delimiter-quote "254"

# Verify output
dat_file=$(find "$TEST_OUTPUT_DIR/test8" -name "*.dat" -print -quit)

if [[ -z "$dat_file" ]]; then
  print_error "Test 8: No .dat file found"
fi

# Verify file was created (ASCII 20 and 254 are non-printable, so we just verify load file exists and has content)
if [[ ! -s "$dat_file" ]]; then
  print_error "Test 8: DAT file is empty"
fi

# Check that it has the expected number of lines (header + 5 data rows)
line_count=$(wc -l < "$dat_file")
if [[ "$line_count" -lt 6 ]]; then
  print_error "Test 8: Expected at least 6 lines in .dat file, found $line_count"
fi

print_success "Test Case 8: ASCII code delimiters passed"

# --- Test Case 9: Delimiter Override (CSV preset with pipe override) ---

print_info "Test Case 9: Delimiter override (CSV preset with pipe column delimiter)"

zipper \
  --type pdf \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test9" \
  --load-file-format dat \
  --dat-delimiters csv \
  --delimiter-column "|"

# Verify output
dat_file=$(find "$TEST_OUTPUT_DIR/test9" -name "*.dat" -print -quit)

if [[ -z "$dat_file" ]]; then
  print_error "Test 9: No .dat file found"
fi

# Check for pipe delimiter (override)
if ! grep -q "|" "$dat_file"; then
  print_error "Test 9: No pipe delimiter found (should override CSV preset)"
fi

# Check for double-quote (from CSV preset)
if ! grep -q '"' "$dat_file"; then
  print_error "Test 9: No double-quote found (should use CSV preset for quote)"
fi

print_success "Test Case 9: Delimiter override passed"

# --- Test Case 10: Auto OPT generation for tiff/jpg types ---

print_info "Test Case 10: Auto OPT generation for tiff and jpg"

# Run for tiff in loadfile-only mode
zipper \
  --type tiff \
  --count 3 \
  --output-path "$TEST_OUTPUT_DIR/test10_tiff" \
  --loadfile-only \
  --bates-prefix "TIFF" \
  --bates-start 1 \
  --bates-digits 5

dat_file=$(find "$TEST_OUTPUT_DIR/test10_tiff" -name "*.dat" -print -quit)
opt_file=$(find "$TEST_OUTPUT_DIR/test10_tiff" -name "*.opt" -print -quit)

if [[ -z "$dat_file" ]]; then
  print_error "Test 10: No .dat file found for tiff"
fi
if [[ -z "$opt_file" ]]; then
  print_error "Test 10: No .opt file found for tiff"
fi

# Verify Bates prefix and suffixes in OPT
if ! grep -q "TIFF00001" "$opt_file"; then
  print_error "Test 10: Base Bates number 'TIFF00001' not found in tiff OPT file"
fi

# Run for jpg in standard mode
zipper \
  --type jpg \
  --count 3 \
  --output-path "$TEST_OUTPUT_DIR/test10_jpg"

zip_file=$(find "$TEST_OUTPUT_DIR/test10_jpg" -name "*.zip" -print -quit)
dat_file=$(find "$TEST_OUTPUT_DIR/test10_jpg" -name "*.dat" -print -quit)
opt_file=$(find "$TEST_OUTPUT_DIR/test10_jpg" -name "*.opt" -print -quit)

if [[ -z "$zip_file" ]]; then
  print_error "Test 10: No .zip file found for jpg"
fi
if [[ -z "$dat_file" ]]; then
  print_error "Test 10: No .dat file found for jpg"
fi
if [[ -z "$opt_file" ]]; then
  print_error "Test 10: No .opt file found for jpg"
fi

print_success "Test Case 10: Auto OPT generation passed"

# --- Test Case 11: Families support across CSV, XML, Concordance, and Loadfile-only ---

print_info "Test Case 11: Families support across load file formats"

# Standard mode EML with families for CSV
zipper \
  --type eml \
  --count 5 \
  --attachment-rate 100 \
  --with-families \
  --output-path "$TEST_OUTPUT_DIR/test11_csv" \
  --load-file-format csv

csv_file=$(find "$TEST_OUTPUT_DIR/test11_csv" -name "*.csv" -print -quit)
if [[ -z "$csv_file" ]]; then
  print_error "Test 11: No .csv file found"
fi

if ! grep -q "BEGATTACH" "$csv_file"; then
  print_error "Test 11: 'BEGATTACH' column not found in .csv header"
fi

# Standard mode EML with families for XML (EDRM-XML)
zipper \
  --type eml \
  --count 5 \
  --attachment-rate 100 \
  --with-families \
  --output-path "$TEST_OUTPUT_DIR/test11_xml" \
  --load-file-format xml

xml_file=$(find "$TEST_OUTPUT_DIR/test11_xml" -name "*.xml" -print -quit)
if [[ -z "$xml_file" ]]; then
  print_error "Test 11: No .xml file found"
fi

if ! grep -q "PARENTDOCID" "$xml_file"; then
  print_error "Test 11: XML did not contain PARENTDOCID tag"
fi

if ! grep -q "ParentDocID" "$xml_file"; then
  print_error "Test 11: XML did not contain relationship with correct ParentDocID casing"
fi

# Loadfile-Only mode with families
zipper \
  --type eml \
  --count 5 \
  --attachment-rate 100 \
  --with-families \
  --output-path "$TEST_OUTPUT_DIR/test11_loadfile_only" \
  --loadfile-only

dat_file=$(find "$TEST_OUTPUT_DIR/test11_loadfile_only" -name "*.dat" -print -quit)
properties_file=$(find "$TEST_OUTPUT_DIR/test11_loadfile_only" -name "*_properties.json" -print -quit)

if [[ -z "$dat_file" ]]; then
  print_error "Test 11: No .dat file found in loadfile-only mode"
fi

if [[ -z "$properties_file" ]]; then
  print_error "Test 11: No properties JSON file found"
fi

# Check that totalRecords in properties JSON is greater than 5 (due to simulated attachments)
total_records=$(grep -o '"totalRecords":[[:space:]]*[0-9]*' "$properties_file" | tr -d -c 0-9)
if [[ "$total_records" -le 5 ]]; then
  print_error "Test 11: Expected totalRecords > 5 in properties.json, found $total_records"
fi

print_success "Test Case 11: Families support passed"

# --- All Tests Passed ---

print_success "All Load File Formats E2E tests passed!"
