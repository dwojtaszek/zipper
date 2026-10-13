#!/bin/bash

# Exit immediately if a command exits with a non-zero status, use unset variable as error, and fail on pipe failures.
set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/load-file-formats"
PROJECT="src/Zipper.csproj"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PYTHON_BIN="${PYTHON:-python3}"

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test1" \
  --format opt \
  --count 10 \
  --file-type pdf

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test2" \
  --format csv \
  --count 10 \
  --file-type pdf

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test3" \
  --format xml \
  --count 5 \
  --file-type pdf

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test4" \
  --format concordance \
  --count 5 \
  --file-type pdf

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test5" \
  --format dat \
  --count 5 \
  --file-type pdf

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

  "$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
    --dir "$TEST_OUTPUT_DIR/test6_$format" \
    --format "$format" \
    --count 3 \
    --file-type pdf \
    --bates-prefix "TEST" \
    --bates-start 1 \
    --bates-digits 6

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test7" \
  --format dat \
  --count 5 \
  --file-type pdf \
  --delimiter-column "|" \
  --delimiter-quote "^"

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test8" \
  --format dat \
  --count 5 \
  --file-type pdf \
  --delimiter-column "20" \
  --delimiter-quote "254"

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test9" \
  --format dat \
  --count 5 \
  --file-type pdf \
  --delimiter-column "|" \
  --delimiter-quote '"'

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test10_tiff" \
  --format dat \
  --count 3 \
  --file-type tiff \
  --bates-prefix "TIFF" \
  --bates-start 1 \
  --bates-digits 5 \
  --loadfile-only

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test10_tiff" \
  --format opt \
  --count 3 \
  --file-type tiff \
  --bates-prefix "TIFF" \
  --bates-start 1 \
  --bates-digits 5 \
  --loadfile-only

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test10_jpg" \
  --format dat \
  --count 3 \
  --file-type jpg

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test10_jpg" \
  --format opt \
  --count 3 \
  --file-type jpg

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test11_csv" \
  --format csv \
  --count 5 \
  --file-type eml \
  --with-families \
  --attachment-rate 100

# Standard mode EML with families for Concordance
zipper \
  --type eml \
  --count 5 \
  --attachment-rate 100 \
  --with-families \
  --output-path "$TEST_OUTPUT_DIR/test11_concordance" \
  --load-file-format concordance

concordance_file=$(find "$TEST_OUTPUT_DIR/test11_concordance" -name "*.dat" -print -quit)
if [[ -z "$concordance_file" ]]; then
  print_error "Test 11: No Concordance (.dat) file found"
fi

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test11_concordance" \
  --format concordance \
  --count 5 \
  --file-type eml \
  --with-families \
  --attachment-rate 100

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test11_xml" \
  --format xml \
  --count 5 \
  --file-type eml \
  --with-families \
  --attachment-rate 100

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

"$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" \
  --dir "$TEST_OUTPUT_DIR/test11_loadfile_only" \
  --format dat \
  --count 5 \
  --file-type eml \
  --with-families \
  --attachment-rate 100 \
  --loadfile-only

print_success "Test Case 11: Families support passed"

# --- Test Case 12: Negative and malformed output validation ---

print_info "Test Case 12: Negative and malformed output validation"

# Run oracle unit test suite
"$PYTHON_BIN" -m unittest discover -s "$SCRIPT_DIR" -p "test_validate_load_file_format.py"

mkdir -p "$TEST_OUTPUT_DIR/test12_malformed"

# 1. Header-only CSV fails
echo "CONTROL NUMBER,FILE PATH" > "$TEST_OUTPUT_DIR/test12_malformed/header_only.csv"
if output=$("$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" --load-file "$TEST_OUTPUT_DIR/test12_malformed/header_only.csv" --format csv --count 10 2>&1); then
  print_error "Test 12: Header-only CSV should have failed"
fi
if ! echo "$output" | grep -q "Header-only CSV"; then
  print_error "Test 12: Expected 'Header-only CSV' diagnostic, got: $output"
fi

# 2. Comma-only OPT fails
echo "DOC00000001,VOL001" > "$TEST_OUTPUT_DIR/test12_malformed/comma_only.opt"
if output=$("$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" --load-file "$TEST_OUTPUT_DIR/test12_malformed/comma_only.opt" --format opt --count 1 2>&1); then
  print_error "Test 12: Comma-only OPT should have failed"
fi
if ! echo "$output" | grep -q "OPT row must have exactly 7 columns"; then
  print_error "Test 12: Expected 'OPT row must have exactly 7 columns' diagnostic, got: $output"
fi

# 3. Malformed XML fails
printf '<Root><Batch><Document DocID="DOC1"></Batch></Root>\n' > "$TEST_OUTPUT_DIR/test12_malformed/malformed.xml"
if output=$("$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" --load-file "$TEST_OUTPUT_DIR/test12_malformed/malformed.xml" --format xml --count 1 2>&1); then
  print_error "Test 12: Malformed XML should have failed"
fi
if ! echo "$output" | grep -q "Malformed XML"; then
  print_error "Test 12: Expected 'Malformed XML' diagnostic, got: $output"
fi

# 4. Bates number mismatch fails
printf "CONTROL NUMBER,FILE PATH,BATES NUMBER\nDOC00000001,folder_001/00000001.pdf,WRONG000001\n" > "$TEST_OUTPUT_DIR/test12_malformed/bates_mismatch.csv"
if output=$("$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" --load-file "$TEST_OUTPUT_DIR/test12_malformed/bates_mismatch.csv" --format csv --count 1 --bates-prefix TEST --bates-start 1 --bates-digits 6 2>&1); then
  print_error "Test 12: Wrong Bates values should have failed"
fi
if ! echo "$output" | grep -q "Bates number mismatch"; then
  print_error "Test 12: Expected 'Bates number mismatch' diagnostic, got: $output"
fi

# 5. Ignored delimiter override fails
printf '"Control Number","File Path"\n"DOC00000001","folder_001/00000001.pdf"\n' > "$TEST_OUTPUT_DIR/test12_malformed/ignored_delim.dat"
if output=$("$PYTHON_BIN" "$SCRIPT_DIR/validate_load_file_format.py" --load-file "$TEST_OUTPUT_DIR/test12_malformed/ignored_delim.dat" --format dat --count 1 --delimiter-column "|" 2>&1); then
  print_error "Test 12: Ignored delimiter override should have failed"
fi
if ! echo "$output" | grep -qi "delimiter"; then
  print_error "Test 12: Expected delimiter diagnostic, got: $output"
fi

print_success "Test Case 12: Negative and malformed output validation passed"

# --- All Tests Passed ---

print_success "All Load File Formats E2E tests passed!"
