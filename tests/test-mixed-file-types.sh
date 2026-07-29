#!/bin/bash

# Exit immediately if a command exits with a non-zero status, use unset variable as error, and fail on pipe failures.
set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/mixed-file-types"

# --- Helper Functions ---

function print_success() {
  local message="$1"
  echo -e "\e[42m[ SUCCESS ]\e[0m ${message}"
}

function print_info() {
  local message="$1"
  echo -e "\e[44m[ INFO ]\e[0m ${message}"
}

function print_error() {
  local message="$1"
  echo -e "\e[41m[ ERROR ]\e[0m ${message}" >&2
  exit 1
}

# --- Test Setup ---

print_info "Running Mixed File Types E2E Test"

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# --- Test Case 1: Mixed Archive with exact per-type counts ---

print_info "Test Case 1: Mixed archive (pdf:50,eml:30,tiff:20) produces exact per-type counts"

zipper \
  --types "pdf:50,eml:30,tiff:20" \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/mixed_archive" \
  --seed 42

zip_file=$(find "$TEST_OUTPUT_DIR/mixed_archive" -name "*.zip" -print -quit)
dat_file=$(find "$TEST_OUTPUT_DIR/mixed_archive" -name "*.dat" -print -quit)
opt_file=$(find "$TEST_OUTPUT_DIR/mixed_archive" -name "*.opt" -print -quit)

if [[ -z "$zip_file" ]]; then
  print_error "Test 1: No .zip file found"
fi
if [[ -z "$dat_file" ]]; then
  print_error "Test 1: No .dat file found"
fi
if [[ -z "$opt_file" ]]; then
  print_error "Test 1: No .opt file found (tiff in mix should default to DAT+OPT)"
fi

zip_listing=$(unzip -l "$zip_file")

pdf_count=$(echo "$zip_listing" | grep -c "\.pdf$" || true)
eml_count=$(echo "$zip_listing" | grep -c "\.eml$" || true)
tiff_count=$(echo "$zip_listing" | grep -c "\.tiff$" || true)

if [[ "$pdf_count" -ne 5 ]]; then
  print_error "Test 1: Expected 5 .pdf files in zip, found $pdf_count"
fi
if [[ "$eml_count" -ne 3 ]]; then
  print_error "Test 1: Expected 3 .eml files in zip, found $eml_count"
fi
if [[ "$tiff_count" -ne 2 ]]; then
  print_error "Test 1: Expected 2 .tiff files in zip, found $tiff_count"
fi

# File Type column present and per-record values follow contiguous declared-order ranges
header=$(head -n 1 "$dat_file")
if ! echo "$header" | grep -q "File Type"; then
  print_error "Test 1: 'File Type' column not found in .dat header"
fi

types_col=$(awk -F'\024' 'NR>1 {print $3}' "$dat_file")
pdf_rows=$(echo "$types_col" | grep -c "PDF" || true)
eml_rows=$(echo "$types_col" | grep -c "EML" || true)
tiff_rows=$(echo "$types_col" | grep -c "TIFF" || true)
if [[ "$pdf_rows" -ne 5 || "$eml_rows" -ne 3 || "$tiff_rows" -ne 2 ]]; then
  print_error "Test 1: File Type column counts wrong (PDF=$pdf_rows EML=$eml_rows TIFF=$tiff_rows)"
fi

first_type=$(echo "$types_col" | head -n 1 | tr -d 'þ\r')
last_type=$(echo "$types_col" | tail -n 1 | tr -d 'þ\r')
if [[ "$first_type" != "PDF" ]]; then
  print_error "Test 1: First record File Type should be PDF, found '$first_type'"
fi
if [[ "$last_type" != "TIFF" ]]; then
  print_error "Test 1: Last record File Type should be TIFF, found '$last_type'"
fi

print_success "Test Case 1: Mixed archive passed"

# --- Test Case 2: Page Count only on TIFF records ---

print_info "Test Case 2: Mixed tiff/pdf with --tiff-pages populates Page Count only for TIFF records"

zipper \
  --types "tiff:1,pdf:1" \
  --count 6 \
  --output-path "$TEST_OUTPUT_DIR/mixed_tiff_pages" \
  --tiff-pages "2-4" \
  --seed 7

dat_file=$(find "$TEST_OUTPUT_DIR/mixed_tiff_pages" -name "*.dat" -print -quit)
if [[ -z "$dat_file" ]]; then
  print_error "Test 2: No .dat file found"
fi

header=$(head -n 1 "$dat_file")
if ! echo "$header" | grep -q "Page Count"; then
  print_error "Test 2: 'Page Count' column not found in .dat header (tiff in mix with --tiff-pages)"
fi

# Columns: Control Number, File Path, File Type, Page Count
first_page=$(awk -F'\024' 'NR==2 {print $4}' "$dat_file" | tr -d 'þ\r')
last_page=$(awk -F'\024' 'NR==7 {print $4}' "$dat_file" | tr -d 'þ\r')
if ! [[ "$first_page" =~ ^[2-4]$ ]]; then
  print_error "Test 2: First (TIFF) record Page Count should be 2-4, found '$first_page'"
fi
if [[ -n "$last_page" ]]; then
  print_error "Test 2: Last (PDF) record Page Count should be blank, found '$last_page'"
fi

print_success "Test Case 2: Page Count per-record gating passed"

# --- Test Case 3: Email Metadata only on EML records ---

print_info "Test Case 3: Mixed pdf/eml populates Email Metadata only for EML records"

zipper \
  --types "pdf:1,eml:1" \
  --count 4 \
  --output-path "$TEST_OUTPUT_DIR/mixed_eml_meta" \
  --seed 42

dat_file=$(find "$TEST_OUTPUT_DIR/mixed_eml_meta" -name "*.dat" -print -quit)
if [[ -z "$dat_file" ]]; then
  print_error "Test 3: No .dat file found"
fi

header=$(head -n 1 "$dat_file")
if ! echo "$header" | grep -q "Subject"; then
  print_error "Test 3: Email Metadata columns not found in .dat header (eml in mix)"
fi

# Columns: Control Number, File Path, File Type, Custodian, Date Sent, Author, File Size, To, From, CC, Subject, Sent Date, Attachment
# count 4 with pdf:1,eml:1 -> data rows 2-3 are PDF, rows 4-5 are EML
pdf_to=$(awk -F'\024' 'NR==2 {print $8}' "$dat_file" | tr -d 'þ\r')
eml_to=$(awk -F'\024' 'NR==4 {print $8}' "$dat_file" | tr -d 'þ\r')
if [[ -n "$pdf_to" ]]; then
  print_error "Test 3: PDF record 'To' should be blank, found '$pdf_to'"
fi
if ! echo "$eml_to" | grep -q "@"; then
  print_error "Test 3: EML record 'To' should contain an address, found '$eml_to'"
fi

print_success "Test Case 3: Email Metadata per-record gating passed"

# --- Test Case 4: Mixed Production Set ---

print_info "Test Case 4: Mixed production set (pdf:1,eml:1) per-record natives and FILE_TYPE"

zipper \
  --production-set \
  --types "pdf:1,eml:1" \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/mixed_prod" \
  --bates-prefix "MIX" \
  --seed 42

prod_dir=$(find "$TEST_OUTPUT_DIR/mixed_prod" -mindepth 1 -maxdepth 1 -type d -print -quit)
if [[ -z "$prod_dir" ]]; then
  print_error "Test 4: No production directory found"
fi

natives_pdf=$(find "$prod_dir/NATIVES" -name "*.pdf" | wc -l | tr -d ' ')
natives_eml=$(find "$prod_dir/NATIVES" -name "*.eml" | wc -l | tr -d ' ')
if [[ "$natives_pdf" -ne 5 || "$natives_eml" -ne 5 ]]; then
  print_error "Test 4: Expected 5 .pdf and 5 .eml natives, found $natives_pdf and $natives_eml"
fi

prod_dat="$prod_dir/DATA/loadfile.dat"
if [[ ! -f "$prod_dat" ]]; then
  print_error "Test 4: No production loadfile.dat found"
fi

# FILE_TYPE is column 10 in the Production Set DAT schema
prod_types=$(awk -F'\024' 'NR>1 {print $10}' "$prod_dat" | tr -d 'þ\r')
prod_pdf=$(echo "$prod_types" | grep -c "PDF" || true)
prod_eml=$(echo "$prod_types" | grep -c "EML" || true)
if [[ "$prod_pdf" -ne 5 || "$prod_eml" -ne 5 ]]; then
  print_error "Test 4: Production DAT FILE_TYPE counts wrong (PDF=$prod_pdf EML=$prod_eml)"
fi

if ! grep -q '"fileType": "pdf,eml"' "$prod_dir/_manifest.json"; then
  print_error "Test 4: Manifest fileType should be 'pdf,eml'"
fi

print_success "Test Case 4: Mixed production set passed"

# --- Test Case 5: Validation failures ---

print_info "Test Case 5: --types validation failures"

val_err_dir=$(mktemp -d)
trap 'rm -rf "$val_err_dir"' EXIT

if zipper --type pdf --types "eml:1" --count 1 --output-path "$TEST_OUTPUT_DIR/val1" > /dev/null 2> "$val_err_dir/val1.err"; then
  print_error "Test 5: --type with --types should fail"
fi
if ! grep -q "cannot be used together" "$val_err_dir/val1.err"; then
  print_error "Test 5: mutual-exclusion error message not found"
fi

if zipper --types "bogus:1" --count 1 --output-path "$TEST_OUTPUT_DIR/val2" > /dev/null 2> "$val_err_dir/val2.err"; then
  print_error "Test 5: unknown type in --types should fail"
fi
if ! grep -q "bogus" "$val_err_dir/val2.err"; then
  print_error "Test 5: unknown-type error message not found"
fi

if zipper --types "pdf:0" --count 1 --output-path "$TEST_OUTPUT_DIR/val3" > /dev/null 2> "$val_err_dir/val3.err"; then
  print_error "Test 5: zero weight in --types should fail"
fi

if zipper --types "pdf:1,eml:1" --loadfile-only --count 1 --output-path "$TEST_OUTPUT_DIR/val4" > /dev/null 2> "$val_err_dir/val4.err"; then
  print_error "Test 5: --types with --loadfile-only should fail"
fi
if ! grep -q "loadfile-only" "$val_err_dir/val4.err"; then
  print_error "Test 5: loadfile-only conflict message not found"
fi

print_success "Test Case 5: Validation failures passed"

# --- All Tests Passed ---

rm -rf "$TEST_OUTPUT_DIR"
print_success "All Mixed File Types E2E tests passed!"
