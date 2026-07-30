#!/bin/bash

# Exit immediately if a command exits with a non-zero status, use unset variable as error, and fail on pipe failures.
set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/source-driven"

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

print_info "Running Source-Driven Generation E2E Test"

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

val_err_dir=$(mktemp -d)
trap 'rm -rf "$val_err_dir"' EXIT

# --- Test Case 1: Source CSV drives archive paths and Load File identity ---

print_info "Test Case 1: Source CSV (3 mixed rows) drives archive entries and DAT rows"

cat > "$TEST_OUTPUT_DIR/source.csv" <<'CSV'
ControlNumber,FilePath,FileType
CTRL-001,docs/a.pdf,pdf
CTRL-002,b.eml,eml
CTRL-003,deep/nested/c.tiff,tiff
CSV

zipper \
  --input-csv "$TEST_OUTPUT_DIR/source.csv" \
  --output-path "$TEST_OUTPUT_DIR/csv_archive" \
  --seed 42

zip_file=$(find "$TEST_OUTPUT_DIR/csv_archive" -name "*.zip" -print -quit)
dat_file=$(find "$TEST_OUTPUT_DIR/csv_archive" -name "*.dat" -print -quit)
opt_file=$(find "$TEST_OUTPUT_DIR/csv_archive" -name "*.opt" -print -quit)

if [[ -z "$zip_file" ]]; then
  print_error "Test 1: No .zip file found"
fi
if [[ -z "$dat_file" ]]; then
  print_error "Test 1: No .dat file found"
fi
if [[ -z "$opt_file" ]]; then
  print_error "Test 1: No .opt file found (tiff row should default to DAT+OPT)"
fi

zip_listing=$(unzip -l "$zip_file")
for expected in "docs/a.pdf" "b.eml" "deep/nested/c.tiff"; do
  if ! echo "$zip_listing" | grep -q "$expected"; then
    print_error "Test 1: Archive entry '$expected' not found"
  fi
done

native_count=$(echo "$zip_listing" | grep -cE "\.(pdf|eml|tiff)$" || true)
if [[ "$native_count" -ne 3 ]]; then
  print_error "Test 1: Expected exactly 3 native entries, found $native_count"
fi

header=$(head -n 1 "$dat_file")
if ! echo "$header" | grep -q "File Type"; then
  print_error "Test 1: 'File Type' column not found in .dat header (mixed source types)"
fi

# Columns: Control Number, File Path, File Type
ctrl_1=$(awk -F'\024' 'NR==2 {print $1}' "$dat_file" | tr -d 'þ\r')
path_1=$(awk -F'\024' 'NR==2 {print $2}' "$dat_file" | tr -d 'þ\r')
type_1=$(awk -F'\024' 'NR==2 {print $3}' "$dat_file" | tr -d 'þ\r')
ctrl_3=$(awk -F'\024' 'NR==4 {print $1}' "$dat_file" | tr -d 'þ\r')
path_3=$(awk -F'\024' 'NR==4 {print $2}' "$dat_file" | tr -d 'þ\r')
type_3=$(awk -F'\024' 'NR==4 {print $3}' "$dat_file" | tr -d 'þ\r')

if [[ "$ctrl_1" != "CTRL-001" || "$path_1" != "docs/a.pdf" || "$type_1" != "PDF" ]]; then
  print_error "Test 1: Row 1 mismatch (control='$ctrl_1' path='$path_1' type='$type_1')"
fi
if [[ "$ctrl_3" != "CTRL-003" || "$path_3" != "deep/nested/c.tiff" || "$type_3" != "TIFF" ]]; then
  print_error "Test 1: Row 3 mismatch (control='$ctrl_3' path='$path_3' type='$type_3')"
fi

data_rows=$(($(wc -l < "$dat_file") - 1))
if [[ "$data_rows" -ne 3 ]]; then
  print_error "Test 1: Expected 3 data rows in .dat, found $data_rows"
fi

print_success "Test Case 1: Source CSV archive passed"

# --- Test Case 2: Directory template mirrors nested structure ---

print_info "Test Case 2: Directory template recreates nested paths without copying content"

mkdir -p "$TEST_OUTPUT_DIR/template/folder_a/deep"
echo "real content a" > "$TEST_OUTPUT_DIR/template/root.pdf"
echo "real content b" > "$TEST_OUTPUT_DIR/template/folder_a/inner.eml"
echo "real content c" > "$TEST_OUTPUT_DIR/template/folder_a/deep/x.tiff"

zipper \
  --directory-template "$TEST_OUTPUT_DIR/template" \
  --output-path "$TEST_OUTPUT_DIR/dir_archive" \
  --seed 42

zip_file=$(find "$TEST_OUTPUT_DIR/dir_archive" -name "*.zip" -print -quit)
if [[ -z "$zip_file" ]]; then
  print_error "Test 2: No .zip file found"
fi

zip_listing=$(unzip -l "$zip_file")
for expected in "root.pdf" "folder_a/inner.eml" "folder_a/deep/x.tiff"; do
  if ! echo "$zip_listing" | grep -q "$expected"; then
    print_error "Test 2: Archive entry '$expected' not found"
  fi
done

# Placeholder bytes are generated, not copied: template content must not appear in the archive
scratch_dir=$(mktemp -d)
unzip -q "$zip_file" -d "$scratch_dir"
if grep -rq "real content" "$scratch_dir"; then
  rm -rf "$scratch_dir"
  print_error "Test 2: Source bytes were copied into the archive (placeholders required)"
fi
rm -rf "$scratch_dir"

dat_file=$(find "$TEST_OUTPUT_DIR/dir_archive" -name "*.dat" -print -quit)
data_rows=$(($(wc -l < "$dat_file") - 1))
if [[ "$data_rows" -ne 3 ]]; then
  print_error "Test 2: Expected 3 data rows in .dat, found $data_rows"
fi

print_success "Test Case 2: Directory template passed"

# --- Test Case 3: Loadfile-Only source CSV emits records without natives ---

print_info "Test Case 3: --loadfile-only with source CSV creates only Load File output"

zipper \
  --loadfile-only \
  --input-csv "$TEST_OUTPUT_DIR/source.csv" \
  --output-path "$TEST_OUTPUT_DIR/csv_loadfile" \
  --seed 42

zip_found=$(find "$TEST_OUTPUT_DIR/csv_loadfile" -name "*.zip" | wc -l | tr -d ' ')
if [[ "$zip_found" -ne 0 ]]; then
  print_error "Test 3: Loadfile-Only must not create an Archive"
fi

dat_file=$(find "$TEST_OUTPUT_DIR/csv_loadfile" -name "*.dat" -print -quit)
if [[ -z "$dat_file" ]]; then
  print_error "Test 3: No .dat file found"
fi

data_rows=$(($(wc -l < "$dat_file") - 1))
if [[ "$data_rows" -ne 3 ]]; then
  print_error "Test 3: Expected 3 data rows, found $data_rows"
fi

ctrl_2=$(awk -F'\024' 'NR==3 {print $1}' "$dat_file" | tr -d 'þ\r')
path_2=$(awk -F'\024' 'NR==3 {print $2}' "$dat_file" | tr -d 'þ\r')
if [[ "$ctrl_2" != "CTRL-002" || "$path_2" != "b.eml" ]]; then
  print_error "Test 3: Row 2 mismatch (control='$ctrl_2' path='$path_2')"
fi

print_success "Test Case 3: Loadfile-Only source passed"

# --- Test Case 4: Extra CSV columns map through a Column Profile ---

print_info "Test Case 4: Extra source Metadata column surfaces via --column-profile"

cat > "$TEST_OUTPUT_DIR/profile.csv" <<'CSV'
FilePath,FileType,Reviewed
a.pdf,pdf,yes-source
b.pdf,pdf,no-source
CSV

cat > "$TEST_OUTPUT_DIR/profile.json" <<'JSON'
{
  "name": "source-test",
  "settings": { "emptyValuePercentage": 0 },
  "columns": [
    { "name": "DOCID", "type": "identifier", "required": true },
    { "name": "FILEPATH", "type": "text", "required": true },
    { "name": "REVIEWED", "type": "text", "required": true }
  ]
}
JSON

zipper \
  --input-csv "$TEST_OUTPUT_DIR/profile.csv" \
  --column-profile "$TEST_OUTPUT_DIR/profile.json" \
  --output-path "$TEST_OUTPUT_DIR/profile_out" \
  --seed 42

dat_file=$(find "$TEST_OUTPUT_DIR/profile_out" -name "*.dat" -print -quit)
if [[ -z "$dat_file" ]]; then
  print_error "Test 4: No .dat file found"
fi

header=$(head -n 1 "$dat_file")
if ! echo "$header" | grep -q "REVIEWED"; then
  print_error "Test 4: REVIEWED column not found in .dat header"
fi

reviewed_1=$(awk -F'\024' 'NR==2 {print $3}' "$dat_file" | tr -d 'þ\r')
reviewed_2=$(awk -F'\024' 'NR==3 {print $3}' "$dat_file" | tr -d 'þ\r')
if [[ "$reviewed_1" != "yes-source" || "$reviewed_2" != "no-source" ]]; then
  print_error "Test 4: Source Metadata not mapped (row1='$reviewed_1' row2='$reviewed_2')"
fi

print_success "Test Case 4: Column Profile source mapping passed"

# --- Test Case 5: Bates override from source rows ---

print_info "Test Case 5: BatesNumber column overrides Bates sequence values"

cat > "$TEST_OUTPUT_DIR/bates.csv" <<'CSV'
FilePath,FileType,BatesNumber
a.pdf,pdf,ABC_00000099
b.pdf,pdf,
CSV

zipper \
  --input-csv "$TEST_OUTPUT_DIR/bates.csv" \
  --bates-prefix "ABC" \
  --output-path "$TEST_OUTPUT_DIR/bates_out" \
  --seed 42

dat_file=$(find "$TEST_OUTPUT_DIR/bates_out" -name "*.dat" -print -quit)
if [[ -z "$dat_file" ]]; then
  print_error "Test 5: No .dat file found"
fi

# Columns: Control Number, File Path, Bates Number
bates_1=$(awk -F'\024' 'NR==2 {print $3}' "$dat_file" | tr -d 'þ\r')
bates_2=$(awk -F'\024' 'NR==3 {print $3}' "$dat_file" | tr -d 'þ\r')
if [[ "$bates_1" != "ABC_00000099" ]]; then
  print_error "Test 5: Row 1 Bates should be the source override, found '$bates_1'"
fi
if [[ "$bates_2" != "ABC00000002" ]]; then
  print_error "Test 5: Row 2 Bates should come from the sequence, found '$bates_2'"
fi

print_success "Test Case 5: Bates override passed"

# --- Test Case 6: Validation failures ---

print_info "Test Case 6: Source input validation failures"

cat > "$TEST_OUTPUT_DIR/traversal.csv" <<'CSV'
FilePath,FileType
../escape.pdf,pdf
CSV
if zipper --input-csv "$TEST_OUTPUT_DIR/traversal.csv" --output-path "$TEST_OUTPUT_DIR/val1" > /dev/null 2> "$val_err_dir/val1.err"; then
  print_error "Test 6: path traversal row should fail"
fi
if ! grep -q "Row 2" "$val_err_dir/val1.err"; then
  print_error "Test 6: traversal error should name the offending row"
fi

if zipper --input-csv "$TEST_OUTPUT_DIR/source.csv" --count 5 --output-path "$TEST_OUTPUT_DIR/val2" > /dev/null 2> "$val_err_dir/val2.err"; then
  print_error "Test 6: --count mismatch should fail"
fi
if ! grep -q "does not match" "$val_err_dir/val2.err"; then
  print_error "Test 6: count mismatch message not found"
fi

if zipper --input-csv "$TEST_OUTPUT_DIR/source.csv" --production-set --bates-prefix "ABC" --output-path "$TEST_OUTPUT_DIR/val3" > /dev/null 2> "$val_err_dir/val3.err"; then
  print_error "Test 6: --production-set with source input should fail"
fi
if ! grep -q "not supported" "$val_err_dir/val3.err"; then
  print_error "Test 6: production-set conflict message not found"
fi

mkdir -p "$TEST_OUTPUT_DIR/bad-template"
echo "x" > "$TEST_OUTPUT_DIR/bad-template/notes.txt"
if zipper --directory-template "$TEST_OUTPUT_DIR/bad-template" --output-path "$TEST_OUTPUT_DIR/val4" > /dev/null 2> "$val_err_dir/val4.err"; then
  print_error "Test 6: unsupported template extension should fail"
fi
if ! grep -q ".txt" "$val_err_dir/val4.err"; then
  print_error "Test 6: unsupported extension message not found"
fi

if zipper --input-csv "$TEST_OUTPUT_DIR/missing.csv" --output-path "$TEST_OUTPUT_DIR/val5" > /dev/null 2> "$val_err_dir/val5.err"; then
  print_error "Test 6: missing source CSV should fail"
fi
if ! grep -q "does not exist" "$val_err_dir/val5.err"; then
  print_error "Test 6: missing file message not found"
fi

if zipper --input-csv "$TEST_OUTPUT_DIR/source.csv" --type pdf --output-path "$TEST_OUTPUT_DIR/val6" > /dev/null 2> "$val_err_dir/val6.err"; then
  print_error "Test 6: --type with --input-csv should fail"
fi

print_success "Test Case 6: Validation failures passed"

# --- All Tests Passed ---

rm -rf "$TEST_OUTPUT_DIR"
print_success "All Source-Driven Generation E2E tests passed!"
