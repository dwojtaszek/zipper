#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Test Configuration ---

# The directory where test output will be generated.
TEST_OUTPUT_DIR="./test_output"

# The .NET project to run.
PROJECT="Zipper/Zipper.csproj"

# --- Helper Functions ---

# Prints a message with a green background.
function print_success() {
  echo -e "\e[42m[ SUCCESS ]\e[0m $1"
}

# Prints a message with a blue background.
function print_info() {
  echo -e "\e[44m[ INFO ]\e[0m $1"
}

# Prints a message with a red background and exits.
function print_error() {
  echo -e "\e[41m[ ERROR ]\e[0m $1"
  exit 1
}

# Verifies the output of a test case.
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

  local zip_file=$(find "$test_dir" -name "*.zip")
  local dat_file=$(find "$test_dir" -name "*.dat")

  if [ -z "$zip_file" ]; then
    print_error "No .zip file found in $test_dir"
  fi
  if [ -z "$dat_file" ]; then
    print_error "No .dat file found in $test_dir"
  fi

  local dat_content_cmd="cat"
  if [ "$encoding" = "UTF-16" ]; then
    dat_content_cmd="iconv -f UTF-16LE -t UTF-8"
  elif [ "$encoding" = "ANSI" ]; then
    dat_content_cmd="iconv -f WINDOWS-1252 -t UTF-8"
  fi

  # Verify line count in .dat file (+1 for header)
  local line_count_raw=$($dat_content_cmd < "$dat_file" | wc -l)
  local line_count=$(echo "$line_count_raw" | tr -d ' ') # Trim whitespace
  local expected_line_count=$((expected_count + 1))
  if [ "$line_count" -ne "$expected_line_count" ]; then
    print_error "Incorrect line count in .dat file. Expected $expected_line_count, found $line_count."
  fi
  print_info ".dat file line count is correct ($line_count)."

  # Verify header
  local header=$($dat_content_cmd < "$dat_file" | head -n 1)
  IFS=',' read -ra cols <<< "$expected_header"
  for col in "${cols[@]}"; do
    if ! echo "$header" | grep -q "$col"; then
      print_error "Header validation failed. Expected to find '$col' in '$header'."
    fi
  done
  print_info ".dat file header is correct."

  # Verify file count in zip
  local zip_file_count=$(unzip -l "$zip_file" | strings | grep -c "\.$file_type")
  if [ "$zip_file_count" -ne "$expected_count" ]; then
    print_error "Incorrect file count in .zip file. Expected $expected_count, found $zip_file_count."
  fi
  print_info ".zip file count for .$file_type is correct ($zip_file_count)."

  # Verify text file count if required
  if [ "$check_text" = "true" ]; then
    local txt_count=$(unzip -l "$zip_file" | strings | grep -c "\.txt")
    if [ "$txt_count" -ne "$expected_count" ]; then
      print_error "Incorrect .txt file count in .zip file. Expected $expected_count, found $txt_count."
    fi
    print_info ".zip file count for .txt is correct ($txt_count)."
  fi
}

# Verifies the output of an EML test case.
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

  local dat_file=$(find "$test_dir" -name "*.dat")
  local dat_content_cmd="cat"
  if [ "$encoding" = "UTF-16" ]; then
    dat_content_cmd="iconv -f UTF-16LE -t UTF-8"
  elif [ "$encoding" = "ANSI" ]; then
    dat_content_cmd="iconv -f WINDOWS-1252 -t UTF-8"
  fi

  # Verify that some attachments are listed in the .dat file
  # We check for more than 2 because the header has "Attachment"
  local attachment_count=$($dat_content_cmd < "$dat_file" | grep -c "attachment")
  if [ "$attachment_count" -lt 2 ]; then
    print_error "No attachments found in .dat file, but they were expected."
  fi
  print_info "Found attachments in .dat file."
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

    local zip_file=$(find "$test_dir" -name "*.zip")
    if [ -z "$zip_file" ]; then
        print_error "No .zip file found in $test_dir"
    fi

    local actual_size_bytes=$(stat -c%s "$zip_file")
    local min_size=$((target_size_bytes - tolerance_bytes))
    local max_size=$((target_size_bytes + tolerance_bytes))

    if [ "$actual_size_bytes" -lt "$min_size" ] || [ "$actual_size_bytes" -gt "$max_size" ]; then
        print_error "Zip file size is out of tolerance. Expected around ${target_size_mb}MB, found $(($actual_size_bytes / 1024 / 1024))MB."
    fi

    print_info "Zip file size is within the expected range."
}


# --- Test Cases ---

print_info "Starting test suite..."

# Create a temporary directory for test output.
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# Test Case 1: Basic PDF generation
print_info "Running Test Case 1: Basic PDF generation"
dotnet run --project "$PROJECT" -- --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_basic"
verify_output "$TEST_OUTPUT_DIR/pdf_basic" 10 "Control Number,File Path" "pdf" "false" "UTF-8"
print_success "Test Case 1 passed."

# Test Case 2: JPG generation with different encoding
print_info "Running Test Case 2: JPG generation with UTF-16 encoding"
dotnet run --project "$PROJECT" -- --type jpg --count 10 --output-path "$TEST_OUTPUT_DIR/jpg_encoding" --encoding UTF-16
verify_output "$TEST_OUTPUT_DIR/jpg_encoding" 10 "Control Number,File Path" "jpg" "false" "UTF-16"
print_success "Test Case 2 passed."

# Test Case 3: TIFF generation with multiple folders and proportional distribution
print_info "Running Test Case 3: TIFF generation with multiple folders and proportional distribution"
dotnet run --project "$PROJECT" -- --type tiff --count 100 --output-path "$TEST_OUTPUT_DIR/tiff_folders" --folders 5 --distribution proportional
verify_output "$TEST_OUTPUT_DIR/tiff_folders" 100 "Control Number,File Path" "tiff" "false" "UTF-8"
print_success "Test Case 3 passed."

# Test Case 4: PDF generation with Gaussian distribution
print_info "Running Test Case 4: PDF generation with Gaussian distribution"
dotnet run --project "$PROJECT" -- --type pdf --count 100 --output-path "$TEST_OUTPUT_DIR/pdf_gaussian" --folders 10 --distribution gaussian
verify_output "$TEST_OUTPUT_DIR/pdf_gaussian" 100 "Control Number,File Path" "pdf" "false" "UTF-8"
print_success "Test Case 4 passed."

# Test Case 5: JPG generation with Exponential distribution
print_info "Running Test Case 5: JPG generation with Exponential distribution"
dotnet run --project "$PROJECT" -- --type jpg --count 100 --output-path "$TEST_OUTPUT_DIR/jpg_exponential" --folders 10 --distribution exponential
verify_output "$TEST_OUTPUT_DIR/jpg_exponential" 100 "Control Number,File Path" "jpg" "false" "UTF-8"
print_success "Test Case 5 passed."

# Test Case 6: PDF generation with metadata
print_info "Running Test Case 6: PDF generation with metadata"
dotnet run --project "$PROJECT" -- --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_metadata" --with-metadata
verify_output "$TEST_OUTPUT_DIR/pdf_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size" "pdf" "false" "UTF-8"
print_success "Test Case 6 passed."

# Test Case 7: All options combined
print_info "Running Test Case 7: All options combined"
dotnet run --project "$PROJECT" -- --type tiff --count 100 --output-path "$TEST_OUTPUT_DIR/all_options" --folders 20 --encoding ANSI --distribution gaussian --with-metadata
verify_output "$TEST_OUTPUT_DIR/all_options" 100 "Control Number,File Path,Custodian,Date Sent,Author,File Size" "tiff" "false" "ANSI"
print_success "Test Case 7 passed."

# Test Case 8: With text
print_info "Running Test Case 8: With text"
dotnet run --project "$PROJECT" -- --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_with_text" --with-text
verify_output "$TEST_OUTPUT_DIR/pdf_with_text" 10 "Control Number,File Path,Extracted Text" "pdf" "true" "UTF-8"
print_success "Test Case 8 passed."

# Test Case 9: With text and metadata
print_info "Running Test Case 9: With text and metadata"
dotnet run --project "$PROJECT" -- --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_with_text_and_metadata" --with-text --with-metadata
verify_output "$TEST_OUTPUT_DIR/pdf_with_text_and_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size,Extracted Text" "pdf" "true" "UTF-8"
print_success "Test Case 9 passed."

# Test Case 10: EML generation with attachments
print_info "Running Test Case 10: EML generation with attachments"
dotnet run --project "$PROJECT" -- --type eml --count 20 --output-path "$TEST_OUTPUT_DIR/eml_attachments" --attachment-rate 50
verify_eml_output "$TEST_OUTPUT_DIR/eml_attachments" 20 "Control Number,File Path,To,From,Subject,Sent Date,Attachment" "eml" "false" "UTF-8"
print_success "Test Case 10 passed."

# Test Case 11: Target zip size
print_info "Running Test Case 11: Target zip size"
dotnet run --project "$PROJECT" -- --type pdf --count 100 --output-path "$TEST_OUTPUT_DIR/pdf_target_size" --target-zip-size 1MB
verify_zip_size "$TEST_OUTPUT_DIR/pdf_target_size" 1
print_success "Test Case 11 passed."

# --- Cleanup ---

print_info "Cleaning up test output..."
rm -rf "$TEST_OUTPUT_DIR"

print_success "All tests passed successfully!"
