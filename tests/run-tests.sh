#!/bin/bash

# Exit immediately if a command exits with a non-zero status, use unset variable as error, and fail on pipe failures.
set -euo pipefail

TOTAL_FAILURES=0
AGGREGATE_FAILURE=0

# --- Test Configuration ---

# The directory where test output will be generated.
TEST_OUTPUT_DIR="./results"

# The .NET project to run.
PROJECT="src/Zipper.csproj"

# --- Helper Functions ---

# Prints a message with a green background.
function print_success() {
  echo -e "\e[42m[ SUCCESS ]\e[0m $1"
}

# Prints a message with a blue background.
function print_info() {
  echo -e "\e[44m[ INFO ]\e[0m $1"
}

# Prints a message with a red background and records/flags a failure (does not exit).
function print_error() {
  echo -e "\e[41m[ ERROR ]\e[0m $1"
  ((TOTAL_FAILURES+=1)) || true
}

# Optimized function to get unzip listing once and cache it
# Arguments:
# $1: Zip file path
# $2: Cache variable name to store the listing
function get_zip_listing() {
    local zip_file="$1"
    local cache_var="$2"
    local listing=$(unzip -l "$zip_file")
    printf -v "$cache_var" '%s' "$listing"
}

# Optimized verify_output function that reduces process spawning
# Arguments:
# $1: Test case directory
# $2: Expected file count
# $3: Expected header columns (comma-separated)
# $4: File type (e.g., "pdf")
# $5: Check for text files (true/false)
# $6: Encoding of the .dat file (e.g., "UTF-8", "UTF-16", "ANSI")
function verify_output() {
  local test_dir="$1"
  local expected_count="$2"
  local expected_header="$3"
  local file_type="$4"
  local check_text="$5"
  local encoding="$6"

  print_info "Verifying output in $test_dir (Encoding: $encoding)"

  local zip_file=$(find "$test_dir" -name "*.zip" -print -quit)
  local dat_file=$(find "$test_dir" -name "*.dat" -print -quit)

  if [[ -z "$zip_file" ]]; then
    print_error "No .zip file found in $test_dir"
    AGGREGATE_FAILURE=1; return 0
  fi
  if [[ -z "$dat_file" ]]; then
    print_error "No .dat file found in $test_dir"
    AGGREGATE_FAILURE=1; return 0
  fi

  # Get zip listing once and reuse it (major performance optimization)
  local zip_listing
  get_zip_listing "$zip_file" zip_listing

  local dat_content_cmd="cat"
  if [[ "$encoding" = "UTF-16" ]]; then
    dat_content_cmd="iconv -f UTF-16LE -t UTF-8"
  elif [[ "$encoding" = "ANSI" ]]; then
    dat_content_cmd="iconv -f WINDOWS-1252 -t UTF-8"
  fi

  # Read and process .dat file content once
  local dat_content=$($dat_content_cmd < "$dat_file")

  # Verify line count in .dat file (+1 for header)
  local line_count=$(echo "$dat_content" | wc -l)
  line_count=$(echo "$line_count" | tr -d ' ') # Trim whitespace
  local expected_line_count=$((expected_count + 1))
  if [[ "$line_count" -ne "$expected_line_count" ]]; then
    print_error "Incorrect line count in .dat file. Expected $expected_line_count, found $line_count."
  fi
  print_info ".dat file line count is correct ($line_count)."

  # Verify header (using cached content)
  local header
  IFS= read -r header <<< "$dat_content"
  IFS=',' read -ra cols <<< "$expected_header"
  for col in "${cols[@]}"; do
    if ! echo "$header" | grep -q "$col"; then
      print_error "Header validation failed. Expected to find '$col' in '$header'."
      AGGREGATE_FAILURE=1; return 0
    fi
  done
  print_info ".dat file header is correct."

  # Verify file count in zip (using cached zip listing)
  local zip_file_count=$(echo "$zip_listing" | grep -c "\.$file_type" || true)
  if [[ "$zip_file_count" -ne "$expected_count" ]]; then
    print_error "Incorrect file count in .zip file. Expected $expected_count, found $zip_file_count."
    AGGREGATE_FAILURE=1; return 0
  fi
  print_info ".zip file count for .$file_type is correct ($zip_file_count)."

  # Verify text file count if required (using cached zip listing)
  if [[ "$check_text" = "true" ]]; then
    local txt_count=0
    if [[ "$file_type" = "eml" ]]; then
      # For EML files, only count text files that don't have "attachment" in the name
      txt_count=$(echo "$zip_listing" | grep "\.txt$" | grep -v "attachment" | wc -l)
    else
      # For other file types, count all text files
      txt_count=$(echo "$zip_listing" | grep -c "\.txt" || true)
    fi
    if [[ "$txt_count" -ne "$expected_count" ]]; then
      print_error "Incorrect .txt file count in .zip file. Expected $expected_count, found $txt_count."
    fi
    print_info ".zip file count for .txt is correct ($txt_count)."
  fi
}

# Optimized EML verification using cached data
# Arguments:
# $1: Test case directory
# $2: Expected file count
# $3: Expected header columns (comma-separated)
# $4: File type (e.g., "eml")
# $5: Check for text files (true/false)
# $6: Encoding of the .dat file (e.g., "UTF-8", "UTF-16", "ANSI")
function verify_eml_output() {
  local test_dir="$1"
  local expected_count="$2"
  local expected_header="$3"
  local file_type="$4"
  local check_text="$5"
  local encoding="$6"

  verify_output "$@"

  local dat_file=$(find "$test_dir" -name "*.dat" -print -quit)
  local zip_file=$(find "$test_dir" -name "*.zip" -print -quit)

  # Get zip listing once (already cached in verify_output, but we need it here too)
  local zip_listing
  get_zip_listing "$zip_file" zip_listing

  local dat_content_cmd="cat"
  if [[ "$encoding" = "UTF-16" ]]; then
    dat_content_cmd="iconv -f UTF-16LE -t UTF-8"
  elif [[ "$encoding" = "ANSI" ]]; then
    dat_content_cmd="iconv -f WINDOWS-1252 -t UTF-8"
  fi

  # Read dat content once for attachment checks
  local dat_content=$($dat_content_cmd < "$dat_file")

  # Verify that attachment files are present (using cached zip listing)
  local attachment_files=$(echo "$zip_listing" | grep "attachment.*\.\(pdf\|jpg\|tiff\)$" | wc -l)

  # Count EML files to calculate expected attachments (50% attachment rate)
  local eml_files=$(echo "$zip_listing" | grep "\.eml$" | wc -l)
  local min_expected_attachments=$((eml_files / 10)) # Should be at least ~10% due to 50% rate randomness

  if [[ "$attachment_files" -lt "$min_expected_attachments" ]]; then
    print_error "Expected at least $min_expected_attachments attachment files in ZIP, but found $attachment_files."
  fi
  print_info "Found $attachment_files attachment files in ZIP archive (expected at least $min_expected_attachments)."

  # Verify that some attachments are listed in the .dat file (using cached content)
  local attachment_count=$(echo "$dat_content" | grep -c "attachment" || true)
  if [[ "$attachment_count" -lt 2 ]]; then
    print_error "No attachments found in .dat file, but they were expected."
  fi
  print_info "Found attachments in .dat file."

  # Verify attachment text files if text extraction is enabled (using cached zip listing)
  if [[ "$check_text" = "true" ]]; then
    local attachment_text_files=$(echo "$zip_listing" | grep "attachment.*\.txt$" | wc -l)
    if [[ "$attachment_text_files" -lt "$min_expected_attachments" ]]; then
      print_error "Expected at least $min_expected_attachments attachment text files, but found $attachment_text_files."
    fi
    print_info "Found $attachment_text_files attachment text files in ZIP archive."
  fi
}

# Verifies the size of the generated zip file.
# Arguments:
# $1: Test case directory
# $2: Target size in MB
function verify_zip_size() {
    local test_dir="$1"
    local target_size_mb="$2"
    local target_size_bytes=$((target_size_mb * 1024 * 1024))
    local tolerance_bytes=$((target_size_bytes / 10)) # 10%

    local zip_file=$(find "$test_dir" -name "*.zip" -print -quit)
    if [[ -z "$zip_file" ]]; then
        print_error "No .zip file found in $test_dir"
    AGGREGATE_FAILURE=1; return 0
    fi

    local actual_size_bytes
    if [[ "$(uname)" == "Darwin" ]]; then
        actual_size_bytes=$(stat -f%z "$zip_file")
    else
        actual_size_bytes=$(stat -c%s "$zip_file")
    fi
    local min_size=$((target_size_bytes - tolerance_bytes))
    local max_size=$((target_size_bytes + tolerance_bytes))

    if [[ "$actual_size_bytes" -lt "$min_size" ]] || [[ "$actual_size_bytes" -gt "$max_size" ]]; then
        print_error "Zip file size is out of tolerance. Expected around ${target_size_mb}MB, found $(($actual_size_bytes / 1024 / 1024))MB."
    AGGREGATE_FAILURE=1; return 0
    fi

    print_info "Zip file size is within the expected range."
}

# Verifies that load file is included in the zip archive.
# Arguments:
# $1: Test case directory
# $2: Expected file count
# $3: Expected header columns (comma-separated)
# $4: File type (e.g., "pdf")
# $5: Encoding of the .dat file (e.g., "UTF-8", "UTF-16", "ANSI")
function verify_load_file_included() {
    local test_dir="$1"
    local expected_count="$2"
    local expected_header="$3"
    local file_type="$4"
    local encoding="$5"

    print_info "Verifying load file included in zip archive (Encoding: $encoding)"

    local zip_file=$(find "$test_dir" -name "*.zip" -print -quit)
    if [[ -z "$zip_file" ]]; then
        print_error "No .zip file found in $test_dir"
    AGGREGATE_FAILURE=1; return 0
    fi

    # Verify no separate .dat file in output directory
    local dat_file=$(find "$test_dir" -name "*.dat" -print -quit)
    if [[ -n "$dat_file" ]]; then
        print_error "Found separate .dat file in output directory, but --include-load-file was specified"
    fi

    # Get zip listing once for all operations
    local zip_listing
    get_zip_listing "$zip_file" zip_listing

    # Verify .dat file exists in zip archive
    local dat_in_zip=$(echo "$zip_listing" | grep -c "\.dat$" || true)
    if [[ "$dat_in_zip" -ne 1 ]]; then
        print_error "Expected 1 .dat file in zip archive, found $dat_in_zip"
    fi
    print_info ".dat file correctly included in zip archive."

    # Extract .dat file temporarily to verify content
    local temp_dir=$(mktemp -d)
    trap "rm -rf $temp_dir" RETURN

    unzip -j "$zip_file" "*.dat" -d "$temp_dir" > /dev/null
    local extracted_dat=$(find "$temp_dir" -name "*.dat" -print -quit)

    local dat_content_cmd="cat"
    if [[ "$encoding" = "UTF-16" ]]; then
        dat_content_cmd="iconv -f UTF-16LE -t UTF-8"
    elif [[ "$encoding" = "ANSI" ]]; then
        dat_content_cmd="iconv -f WINDOWS-1252 -t UTF-8"
    fi

    # Read content once for verification
    local dat_content=$($dat_content_cmd < "$extracted_dat")

    # Verify line count in extracted .dat file (+1 for header)
    local line_count=$(echo "$dat_content" | wc -l)
    line_count=$(echo "$line_count" | tr -d ' ') # Trim whitespace
    local expected_line_count=$((expected_count + 1))
    if [[ "$line_count" -ne "$expected_line_count" ]]; then
        print_error "Incorrect line count in .dat file. Expected $expected_line_count, found $line_count."
    fi
    print_info ".dat file line count is correct ($line_count)."

    # Verify header (using cached content)
    local header
    IFS= read -r header <<< "$dat_content"
    IFS=',' read -ra cols <<< "$expected_header"
    for col in "${cols[@]}"; do
        if ! echo "$header" | grep -q "$col"; then
            print_error "Header validation failed. Expected to find '$col' in '$header'."
      AGGREGATE_FAILURE=1; return 0
        fi
    done
    print_info ".dat file header is correct."

    # Verify file count in zip (excluding the .dat file, using cached listing)
    local zip_file_count=$(echo "$zip_listing" | grep -c "\.$file_type" || true)
    if [[ "$zip_file_count" -ne "$expected_count" ]]; then
        print_error "Incorrect file count in .zip file. Expected $expected_count, found $zip_file_count."
    AGGREGATE_FAILURE=1; return 0
    fi
    print_info ".zip file count for .$file_type is correct ($zip_file_count)."
}


# --- Test Cases ---

print_info "Starting test suite..."

# Create a temporary directory for test output.
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# Source the shared binary-resolution helper so all 17 inline cases share one Release build.
# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

# Function to run a single test case with logging
function run_test_case() {
    local test_name="$1"
    local exit_code=0
    shift
    print_info "START: $test_name at $(date)"
    zipper "$@" || exit_code=$?
    if [[ $exit_code -ne 0 ]]; then
        print_error "$test_name failed with exit code $exit_code"
    fi
    print_info "END: $test_name at $(date)"
}

# Test Case 1: Basic PDF generation
run_test_case "Test Case 1: Basic PDF generation" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_basic"
verify_output "$TEST_OUTPUT_DIR/pdf_basic" 10 "Control Number,File Path" "pdf" "false" "UTF-8"
print_success "Test Case 1 passed."

# Test Case 2: JPG generation with different encoding
run_test_case "Test Case 2: JPG generation with UTF-16 encoding" --type jpg --count 10 --output-path "$TEST_OUTPUT_DIR/jpg_encoding" --encoding UTF-16
verify_output "$TEST_OUTPUT_DIR/jpg_encoding" 10 "Control Number,File Path" "jpg" "false" "UTF-16"
print_success "Test Case 2 passed."

# Test Case 3: TIFF generation with multiple folders and proportional distribution
run_test_case "Test Case 3: TIFF generation" --type tiff --count 20 --output-path "$TEST_OUTPUT_DIR/tiff_folders" --folders 5 --distribution proportional
verify_output "$TEST_OUTPUT_DIR/tiff_folders" 20 "Control Number,File Path" "tiff" "false" "UTF-8"
print_success "Test Case 3 passed."

# Test Case 4: PDF generation with Gaussian distribution
run_test_case "Test Case 4: PDF generation with Gaussian distribution" --type pdf --count 20 --output-path "$TEST_OUTPUT_DIR/pdf_gaussian" --folders 5 --distribution gaussian
verify_output "$TEST_OUTPUT_DIR/pdf_gaussian" 20 "Control Number,File Path" "pdf" "false" "UTF-8"
print_success "Test Case 4 passed."

# Test Case 5: JPG generation with Exponential distribution
run_test_case "Test Case 5: JPG generation with Exponential distribution" --type jpg --count 20 --output-path "$TEST_OUTPUT_DIR/jpg_exponential" --folders 5 --distribution exponential
verify_output "$TEST_OUTPUT_DIR/jpg_exponential" 20 "Control Number,File Path" "jpg" "false" "UTF-8"
print_success "Test Case 5 passed."

# Test Case 6: PDF generation with metadata
run_test_case "Test Case 6: PDF generation with metadata" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_metadata" --with-metadata
verify_output "$TEST_OUTPUT_DIR/pdf_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size" "pdf" "false" "UTF-8"
print_success "Test Case 6 passed."

# Test Case 7: All options combined
run_test_case "Test Case 7: All options combined" --type tiff --count 15 --output-path "$TEST_OUTPUT_DIR/all_options" --folders 5 --encoding ANSI --distribution gaussian --with-metadata
verify_output "$TEST_OUTPUT_DIR/all_options" 15 "Control Number,File Path,Custodian,Date Sent,Author,File Size" "tiff" "false" "ANSI"
print_success "Test Case 7 passed."

# Test Case 8: With text
run_test_case "Test Case 8: With text" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_with_text" --with-text
verify_output "$TEST_OUTPUT_DIR/pdf_with_text" 10 "Control Number,File Path,Extracted Text" "pdf" "true" "UTF-8"
print_success "Test Case 8 passed."

# Test Case 9: With text and metadata
run_test_case "Test Case 9: With text and metadata" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_with_text_and_metadata" --with-text --with-metadata
verify_output "$TEST_OUTPUT_DIR/pdf_with_text_and_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size,Extracted Text" "pdf" "true" "UTF-8"
print_success "Test Case 9 passed."

# Test Case 10: EML generation with attachments
run_test_case "Test Case 10: EML generation with attachments" --type eml --count 20 --output-path "$TEST_OUTPUT_DIR/eml_attachments" --attachment-rate 50 --seed 42
verify_eml_output "$TEST_OUTPUT_DIR/eml_attachments" 20 "Control Number,File Path,To,From,Subject,Sent Date,Attachment" "eml" "false" "UTF-8"
print_success "Test Case 10 passed."

# Test Case 11: EML generation with metadata
run_test_case "Test Case 11: EML generation with metadata" --type eml --count 10 --output-path "$TEST_OUTPUT_DIR/eml_metadata" --with-metadata
verify_output "$TEST_OUTPUT_DIR/eml_metadata" 10 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment" "eml" "false" "UTF-8"
print_success "Test Case 11 passed."

# Test Case 12: EML generation with text
run_test_case "Test Case 12: EML generation with text" --type eml --count 10 --output-path "$TEST_OUTPUT_DIR/eml_text" --with-text
verify_output "$TEST_OUTPUT_DIR/eml_text" 10 "Control Number,File Path,To,From,Subject,Sent Date,Attachment,Extracted Text" "eml" "true" "UTF-8"
print_success "Test Case 12 passed."

# Test Case 13: EML generation with metadata and text
run_test_case "Test Case 13: EML generation with metadata and text" --type eml --count 10 --output-path "$TEST_OUTPUT_DIR/eml_metadata_text" --with-metadata --with-text
verify_output "$TEST_OUTPUT_DIR/eml_metadata_text" 10 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment,Extracted Text" "eml" "true" "UTF-8"
print_success "Test Case 13 passed."

# Test Case 14: Target zip size
run_test_case "Test Case 14: Target zip size" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_target_size" --target-zip-size 1MB
verify_zip_size "$TEST_OUTPUT_DIR/pdf_target_size" 1
print_success "Test Case 14 passed."

# Test Case 15: Include load file in zip
run_test_case "Test Case 15: Include load file in zip" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_include_load" --include-load-file
verify_load_file_included "$TEST_OUTPUT_DIR/pdf_include_load" 10 "Control Number,File Path" "pdf" "UTF-8"
print_success "Test Case 15 passed."

# Test Case 16: EML attachments with metadata and text (comprehensive attachment test)
run_test_case "Test Case 16: EML attachments with metadata and text" --type eml --count 15 --output-path "$TEST_OUTPUT_DIR/eml_attachments_full" --attachment-rate 60 --with-metadata --with-text --seed 42
verify_eml_output "$TEST_OUTPUT_DIR/eml_attachments_full" 15 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment,Extracted Text" "eml" "true" "UTF-8"
print_success "Test Case 16 passed."

# Test Case 17: Maximum folders edge case (100 folders)
run_test_case "Test Case 17: Maximum folders edge case (100 folders)" --type pdf --count 25 --output-path "$TEST_OUTPUT_DIR/pdf_max_folders" --folders 10
verify_output "$TEST_OUTPUT_DIR/pdf_max_folders" 25 "Control Number,File Path" "pdf" "false" "UTF-8"
print_success "Test Case 17 passed."

# Test Case 18: CSV load file format (non-DAT format inline validation)
run_test_case "Test Case 18: CSV load file format" --type pdf --count 5 --output-path "$TEST_OUTPUT_DIR/pdf_csv" --load-file-format csv
csv_dir="$TEST_OUTPUT_DIR/pdf_csv"
csv_zip=$(find "$csv_dir" -name "*.zip" -print -quit)
csv_file=$(find "$csv_dir" -name "*.csv" -print -quit)
if [[ -z "$csv_zip" ]]; then
  print_error "Test 18: No .zip file found"
fi
if [[ -z "$csv_file" ]]; then
  print_error "Test 18: No .csv file found"
fi
if ! head -n 1 "$csv_file" | grep -qi "Control Number"; then
  print_error "Test 18: 'Control Number' column not found in .csv header"
fi
print_success "Test Case 18 passed."

# Test Case 19: OPT load file format (non-DAT format inline validation)
run_test_case "Test Case 19: OPT load file format" --type pdf --count 5 --output-path "$TEST_OUTPUT_DIR/pdf_opt" --load-file-format opt
opt_dir="$TEST_OUTPUT_DIR/pdf_opt"
opt_zip=$(find "$opt_dir" -name "*.zip" -print -quit)
opt_file=$(find "$opt_dir" -name "*.opt" -print -quit)
if [[ -z "$opt_zip" ]]; then
  print_error "Test 19: No .zip file found"
fi
if [[ -z "$opt_file" ]]; then
  print_error "Test 19: No .opt file found"
fi
# OPT uses comma delimiter (Opticon 7-column standard)
if ! grep ',' "$opt_file" > /dev/null; then
  print_error "Test 19: No comma delimiter found in .opt file"
fi
# OPT has no header row
if head -n 1 "$opt_file" | grep -q "Control Number"; then
  print_error "Test 19: OPT should not contain header row, found 'Control Number'"
fi
print_success "Test Case 19 passed."

# Test Case 20: High-volume smoke test
run_test_case "Test Case 20: High-volume smoke test" --type pdf --count 5000 --output-path "$TEST_OUTPUT_DIR/pdf_high_volume"
verify_output "$TEST_OUTPUT_DIR/pdf_high_volume" 5000 "Control Number,File Path" "pdf" "false" "UTF-8"
print_success "Test Case 20 passed."

# --- Cleanup ---

print_info "Cleaning up test output..."
rm -rf "$TEST_OUTPUT_DIR"

# --- Standalone Feature Test Suites ---
# Run all standalone test scripts to ensure comprehensive coverage

print_info "Running standalone feature test suites..."

# Test 1: EML comprehensive tests
print_info "Running EML comprehensive tests..."
bash ./tests/test-eml-comprehensive.sh || print_error "test-eml-comprehensive.sh failed."
print_success "EML comprehensive tests passed."

# Test 2: Bates numbering tests
echo "[ INFO ] Running Bates Numbering Tests..."
bash ./tests/test-bates-numbering.sh || print_error "test-bates-numbering.sh failed."

if [[ $? -ne 0 ]]; then
  print_error "Bates Numbering Tests failed."
fi

echo "[ INFO ] Running Production Sets Tests..."
bash ./tests/test-production-sets.sh || print_error "test-production-sets.sh failed."

if [[ $? -ne 0 ]]; then
  print_error "Production Sets Tests failed."
fi

# Test 3: Multipage TIFF tests
print_info "Running multipage TIFF tests..."
bash ./tests/test-multipage-tiff.sh || print_error "test-multipage-tiff.sh failed."
print_success "Multipage TIFF tests passed."

# Test 4: Office formats tests
print_info "Running office formats tests..."
bash ./tests/test-office-formats.sh || print_error "test-office-formats.sh failed."
print_success "Office formats tests passed."

# Test 5: Load file formats tests
print_info "Running load file formats tests..."
bash ./tests/test-load-file-formats.sh || print_error "test-load-file-formats.sh failed."
print_success "Load file formats tests passed."

# Test 6: Artifact handling tests
print_info "Running artifact handling tests..."
bash ./tests/test-artifact-handling.sh || print_error "test-artifact-handling.sh failed."
print_success "Artifact handling tests passed."

# Test 7: Cross-platform tests
print_info "Running cross-platform tests..."
bash ./tests/test-cross-platform.sh || print_error "test-cross-platform.sh failed."
print_success "Cross-platform tests passed."

# Test 10: Path traversal security tests
print_info "Running path traversal security tests..."
bash ./tests/test-path-traversal-security.sh || print_error "test-path-traversal-security.sh failed."
print_success "Path traversal security tests passed."

# Test 11: Unified workflow tests
print_info "Running unified workflow tests..."
bash ./tests/test-unified-workflow.sh || print_error "test-unified-workflow.sh failed."
print_success "Unified workflow tests passed."

# Test 12: Loadfile-only and Chaos Engine tests
print_info "Running loadfile-only and Chaos Engine tests..."
if ! bash ./tests/run-e2e-loadfile.sh; then
  print_error "Loadfile-only tests failed."
fi
print_success "Loadfile-only tests passed."

# Test 13: Chaos anomaly-type and scenario coverage
print_info "Running chaos anomaly coverage tests..."
bash ./tests/test-chaos-anomaly-coverage.sh || print_error "test-chaos-anomaly-coverage.sh failed."
print_success "Chaos anomaly coverage tests passed."


# Test 14: Column-profile built-in matrix (40 combos: 4 profiles × 5 file types × 2 seeds)
print_info "Running column-profile built-in matrix tests..."
bash ./tests/test-column-profile-builtin-matrix.sh || print_error "test-column-profile-builtin-matrix.sh failed."
print_success "Column-profile built-in matrix tests passed."

# Test 15: Column-profile custom every-kind profile
print_info "Running column-profile custom-kinds test..."
bash ./tests/test-column-profile-custom-kinds.sh || print_error "test-column-profile-custom-kinds.sh failed."
print_success "Column-profile custom-kinds test passed."

# Test 16: Column-profile EmptyPercentage chi-square
print_info "Running column-profile empty-percentage tests..."
bash ./tests/test-column-profile-empty-pct.sh || print_error "test-column-profile-empty-pct.sh failed."
print_success "Column-profile empty-percentage tests passed."

print_info "Running target-zip-size accuracy tests..."
bash ./tests/test-target-zip-size.sh || print_error "test-target-zip-size.sh failed."
print_success "Target-zip-size accuracy tests passed."

print_info "Running argument-interaction conflict tests..."
bash ./tests/test-argument-interactions.sh || print_error "test-argument-interactions.sh failed."
print_success "Argument-interaction conflict tests passed."

print_info "Running CLI coverage gap tests..."
bash ./tests/test-cli-coverage-gaps.sh || print_error "test-cli-coverage-gaps.sh failed."
print_success "CLI coverage gap tests passed."

# FGR guard: FileGenerationRequest must not have flat pass-through properties (see #213).
print_info "Checking for flat pass-through properties on FileGenerationRequest..."
if grep -q 'get => this\.[A-Z][a-z]*\.' src/FileGenerationRequest.cs; then
  print_error "FileGenerationRequest must not have flat pass-through properties (see issue #213)."
fi
print_success "FGR flat-property guard passed."

if [[ "$TOTAL_FAILURES" -ne 0 ]] || [[ "$AGGREGATE_FAILURE" -ne 0 ]]; then
  print_error "Test suite failed with $TOTAL_FAILURES errors."
  exit 1
fi

print_success "All tests passed successfully!"
exit 0
