#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/office-formats"
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

print_info "Running Office Formats E2E Test"

# Clean up previous test results
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# --- Test Case 1: DOCX Generation ---

print_info "Test Case 1: DOCX file generation"

dotnet run --project "$PROJECT" -- \
  --type docx \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test1" \
  --folders 3

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.zip")
dat_file=$(find "$TEST_OUTPUT_DIR/test1" -name "*.dat")

if [ -z "$zip_file" ]; then
  print_error "Test 1: No .zip file found"
fi
if [ -z "$dat_file" ]; then
  print_error "Test 1: No .dat file found"
fi

# Verify DOCX files were created
docx_count=$(unzip -l "$zip_file" | grep -c "\.docx" || true)
if [ "$docx_count" -lt 10 ]; then
  print_error "Test 1: Expected at least 10 DOCX files in zip, found $docx_count"
fi

# Verify file extension in load file
if ! grep -q "\.docx" "$dat_file"; then
  print_error "Test 1: No .docx extension found in .dat file"
fi

print_success "Test Case 1: DOCX generation passed"

# --- Test Case 2: XLSX Generation ---

print_info "Test Case 2: XLSX file generation"

dotnet run --project "$PROJECT" -- \
  --type xlsx \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test2" \
  --folders 2

# Verify output
zip_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.zip")
dat_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.dat")

if [ -z "$zip_file" ]; then
  print_error "Test 2: No .zip file found"
fi
if [ -z "$dat_file" ]; then
  print_error "Test 2: No .dat file found"
fi

# Verify XLSX files were created
xlsx_count=$(unzip -l "$zip_file" | grep -c "\.xlsx" || true)
if [ "$xlsx_count" -lt 10 ]; then
  print_error "Test 2: Expected at least 10 XLSX files in zip, found $xlsx_count"
fi

# Verify file extension in load file
if ! grep -q "\.xlsx" "$dat_file"; then
  print_error "Test 2: No .xlsx extension found in .dat file"
fi

print_success "Test Case 2: XLSX generation passed"

# --- Test Case 3: DOCX with Metadata ---

print_info "Test Case 3: DOCX with metadata"

dotnet run --project "$PROJECT" -- \
  --type docx \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test3" \
  --with-metadata

# Verify output
dat_file=$(find "$TEST_OUTPUT_DIR/test3" -name "*.dat")

# Check for metadata columns
first_line=$(head -n 1 "$dat_file")
if ! echo "$first_line" | grep -q "Custodian"; then
  print_error "Test 3: 'Custodian' column not found in .dat header"
fi

if ! echo "$first_line" | grep -q "Date Sent"; then
  print_error "Test 3: 'Date Sent' column not found in .dat header"
fi

if ! echo "$first_line" | grep -q "Author"; then
  print_error "Test 3: 'Author' column not found in .dat header"
fi

if ! echo "$first_line" | grep -q "File Size"; then
  print_error "Test 3: 'File Size' column not found in .dat header"
fi

print_success "Test Case 3: DOCX with metadata passed"

# --- Test Case 4: DOCX with Bates Numbering ---

print_info "Test Case 4: DOCX with Bates numbering"

dotnet run --project "$PROJECT" -- \
  --type docx \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test4" \
  --bates-prefix "OFFICE" \
  --bates-start 500 \
  --bates-digits 10

# Verify output
dat_file=$(find "$TEST_OUTPUT_DIR/test4" -name "*.dat")

# Check for Bates Number column
first_line=$(head -n 1 "$dat_file")
if ! echo "$first_line" | grep -q "Bates Number"; then
  print_error "Test 4: 'Bates Number' column not found in .dat header"
fi

# Verify Bates numbers
if ! grep -q "OFFICE0000000500" "$dat_file"; then
  print_error "Test 4: Bates number 'OFFICE0000000500' not found"
fi

print_success "Test Case 4: DOCX with Bates numbering passed"

# --- Test Case 5: XLSX with Multiple Load File Formats ---

print_info "Test Case 5: XLSX with different load file formats"

for format in "dat" "opt" "csv" "xml"; do
  dotnet run --project "$PROJECT" -- \
    --type xlsx \
    --count 3 \
    --output-path "$TEST_OUTPUT_DIR/test5_$format" \
    --load-file-format "$format"

  # Find the load file
  case "$format" in
    "dat") ext="dat" ;;
    "opt") ext="opt" ;;
    "csv") ext="csv" ;;
    "xml") ext="xml" ;;
  esac

  load_file=$(find "$TEST_OUTPUT_DIR/test5_$format" -name "*.$ext")

  if [ -z "$load_file" ]; then
    print_error "Test 5: No .$ext file found for format $format"
  fi

  # Verify XLSX extension is in load file
  if ! grep -q "\.xlsx" "$load_file"; then
    print_error "Test 5: .xlsx extension not found in $format load file"
  fi

  print_success "Test Case 5: XLSX with $format format passed"
done

# --- Test Case 6: DOCX Files are Valid ZIP Archives ---

print_info "Test Case 6: Verify generated DOCX files are valid ZIP archives"

dotnet run --project "$PROJECT" -- \
  --type docx \
  --count 3 \
  --output-path "$TEST_OUTPUT_DIR/test6"

# Extract one DOCX file and verify it's a valid ZIP
zip_file=$(find "$TEST_OUTPUT_DIR/test6" -name "*.zip")

# Get the first DOCX file from the archive
docx_filename=$(unzip -l "$zip_file" | grep "\.docx" | head -n 1 | awk '{print $4}')

if [ -z "$docx_filename" ]; then
  print_error "Test 6: Could not find DOCX file in archive"
fi

# Extract the DOCX to a temporary location
temp_dir="$TEST_OUTPUT_DIR/test6/temp"
mkdir -p "$temp_dir"
unzip -q "$zip_file" "$docx_filename" -d "$temp_dir"

# Verify the extracted DOCX is a valid ZIP archive
unzip -t "$temp_dir/$docx_filename" > /dev/null 2>&1
if [ $? -eq 0 ]; then
  print_success "Test Case 6: DOCX file is valid ZIP archive"
else
  print_error "Test 6: Extracted DOCX file is not a valid ZIP archive"
fi

# --- All Tests Passed ---

print_success "All Office Formats E2E tests passed!"
