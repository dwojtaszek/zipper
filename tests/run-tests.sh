#!/bin/bash

# Exit immediately if a command exits with a non-zero status, use unset variable as error, and fail on pipe failures.
set -euo pipefail

TOTAL_FAILURES=0
AGGREGATE_FAILURE=0

# Parallel worker configuration for standalone feature suites.
# Default to up to 4 workers (override via RUN_TESTS_JOBS=N, --jobs N, or --serial).
if [[ -z "${RUN_TESTS_JOBS:-}" ]]; then
    _detected_cpus=$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo 4)
    if [[ "$_detected_cpus" -gt 4 ]]; then
        RUN_TESTS_JOBS=4
    elif [[ "$_detected_cpus" -lt 1 ]]; then
        RUN_TESTS_JOBS=1
    else
        RUN_TESTS_JOBS="$_detected_cpus"
    fi
fi

while [[ $# -gt 0 ]]; do
    case "$1" in
        --serial)
            RUN_TESTS_JOBS=1
            shift
            ;;
        -j|--jobs)
            if [[ $# -lt 2 || ! "${2:-}" =~ ^[0-9]+$ || "$2" -lt 1 ]]; then
                echo "Error: $1 requires a positive integer" >&2
                exit 1
            fi
            RUN_TESTS_JOBS="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 1
            ;;
    esac
done

PARALLEL_LOG_DIR=""
if ! PARALLEL_LOG_DIR=$(mktemp -d 2>/dev/null) || [[ -z "$PARALLEL_LOG_DIR" ]]; then
    echo "Warning: failed to create temporary directory for parallel logs; falling back to serial execution." >&2
    RUN_TESTS_JOBS=1
fi
PARALLEL_PIDS=()
PARALLEL_LOGS=()
PARALLEL_NAMES=()
PARALLEL_NEXT_IDX=0

cleanup_parallel_logs() {
    for pid in ${PARALLEL_PIDS[@]+"${PARALLEL_PIDS[@]}"}; do
        kill -TERM -- "-$pid" 2>/dev/null || kill -TERM "$pid" 2>/dev/null || true
    done
    for pid in ${PARALLEL_PIDS[@]+"${PARALLEL_PIDS[@]}"}; do
        wait "$pid" 2>/dev/null || true
    done
    if [[ -n "$PARALLEL_LOG_DIR" && -d "$PARALLEL_LOG_DIR" ]]; then
        rm -rf "$PARALLEL_LOG_DIR"
    fi
}
trap cleanup_parallel_logs EXIT
trap 'cleanup_parallel_logs; exit 130' INT TERM

reap_oldest_parallel_suite() {
    if [[ "${#PARALLEL_PIDS[@]}" -eq 0 ]]; then
        return 0
    fi
    local pid="${PARALLEL_PIDS[0]}"
    local log_file="${PARALLEL_LOGS[0]}"
    local script_name="${PARALLEL_NAMES[0]}"
    local rc=0
    wait "$pid" || rc=$?
    if [[ -f "$log_file" ]]; then
        cat "$log_file"
        rm -f "$log_file"
    fi
    if [[ "$rc" -ne 0 ]]; then
        print_error "${script_name} failed."
    fi
    PARALLEL_PIDS=("${PARALLEL_PIDS[@]:1}")
    PARALLEL_LOGS=("${PARALLEL_LOGS[@]:1}")
    PARALLEL_NAMES=("${PARALLEL_NAMES[@]:1}")
}

flush_parallel_suites() {
    while [[ "${#PARALLEL_PIDS[@]}" -gt 0 ]]; do
        reap_oldest_parallel_suite
    done
}

# Parallel safety contract for standalone suites:
# - Every standalone suite runs AFTER `source ./tests/_zipper-cli.sh` and inherits
#   `ZIPPER_EXECUTABLE`, so child scripts never invoke `dotnet build` or compete for `obj/`/`bin/`.
# - Every parallel-eligible suite writes exclusively to its own `./results/<suite-specific-dir>`
#   subdirectory (verified across `tests/test-*.sh` and `tests/run-e2e-*.sh`) and receives an
#   isolated per-worker `TMPDIR` under `$PARALLEL_LOG_DIR`.
# - Suites that invoke `dotnet msbuild` or inspect shared repo/workflow state are listed in
#   `force_serial` below and run serially after flushing active workers.
run_standalone_suite() {
    local label="$1"
    shift
    local script_arg="${2:-$1}"
    local script_name="${script_arg#./tests/}"
    local force_serial=0
    case "$script_name" in
        test-artifact-handling.sh|test-run-tests-guard-parity.sh|test-ci-build-props.sh|./.github/actions/coverage-gate/test-coverage-gate.sh)
            force_serial=1
            ;;
    esac

    if [[ "$RUN_TESTS_JOBS" -le 1 || "$force_serial" -eq 1 ]]; then
        if [[ "$force_serial" -eq 1 ]]; then
            flush_parallel_suites
        fi
        print_info "Running ${label}..."
        if "$@"; then
            print_success "${label} passed."
        else
            print_error "${script_name} failed."
        fi
        return 0
    fi

    while [[ "${#PARALLEL_PIDS[@]}" -ge "$RUN_TESTS_JOBS" ]]; do
        reap_oldest_parallel_suite
    done

    local idx="$PARALLEL_NEXT_IDX"
    ((PARALLEL_NEXT_IDX+=1)) || true
    local log_file="$PARALLEL_LOG_DIR/suite_${idx}.log"
    local worker_tmp="$PARALLEL_LOG_DIR/tmp_${idx}"
    mkdir -p "$worker_tmp"
    set -m
    (
        export ZIPPER_EXECUTABLE="${ZIPPER_EXECUTABLE:-${_ZIPPER_BIN:-}}"
        export TMPDIR="$worker_tmp"
        print_info "Running ${label}..."
        if "$@"; then
            print_success "${label} passed."
        else
            exit 1
        fi
    ) </dev/null >"$log_file" 2>&1 &
    PARALLEL_PIDS+=("$!")
    set +m
    PARALLEL_LOGS+=("$log_file")
    PARALLEL_NAMES+=("$script_name")
}

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

  local first_bytes
  if command -v xxd >/dev/null 2>&1; then
    first_bytes=$(head -c 3 "$dat_file" | xxd -p | tr -d ' ' | tr a-z A-Z)
  elif command -v od >/dev/null 2>&1; then
    first_bytes=$(od -An -tx1 -N3 "$dat_file" | tr -d ' \n' | tr a-z A-Z)
  else
    first_bytes=$(python3 -c 'import sys; print(open(sys.argv[1], "rb").read(3).hex().upper())' "$dat_file" 2>/dev/null || true)
  fi
  if [[ "$encoding" = "UTF-8" ]]; then
    if [[ "$first_bytes" != "EFBBBF" ]]; then
      print_error "Missing UTF-8 BOM in .dat file."
      AGGREGATE_FAILURE=1; return 0
    fi
  elif [[ "$encoding" = "UTF-16" ]]; then
    if [[ "${first_bytes:0:4}" != "FFFE" ]]; then
      print_error "Missing UTF-16LE BOM in .dat file."
      AGGREGATE_FAILURE=1; return 0
    fi
  elif [[ "$encoding" = "ANSI" ]]; then
    if [[ "${first_bytes:0:6}" == "EFBBBF" ]] || [[ "${first_bytes:0:4}" == "FFFE" ]]; then
      print_error "Unexpected BOM in ANSI .dat file."
      AGGREGATE_FAILURE=1; return 0
    fi
  fi

  # Ensure trailing newline
  local last_char=$($dat_content_cmd < "$dat_file" | tail -c 1)
  if [[ -n "$last_char" ]]; then
    print_error "Missing trailing newline in .dat file."
    AGGREGATE_FAILURE=1; return 0
  fi

  # Read and process .dat file content once
  local dat_content=$($dat_content_cmd < "$dat_file")
  LAST_ZIP_LISTING="$zip_listing"
  LAST_DAT_CONTENT="$dat_content"

  # Verify line count in .dat file (+1 for header) using cached content
  local line_count=$(printf '%s\n' "$dat_content" | wc -l)
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
  local zip_file_count=$(echo "$zip_listing" | grep -c "\.$file_type$" || true)
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
      txt_count=$(echo "$zip_listing" | grep -c "\.txt$" || true)
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
# $7: Expected attachment count
function verify_eml_output() {
  local test_dir="$1"
  local expected_count="$2"
  local expected_header="$3"
  local file_type="$4"
  local check_text="$5"
  local encoding="$6"
  local expected_attachment_count="$7"

  verify_output "$@"

  # Reuse listing and .dat content already extracted in verify_output
  local zip_listing="${LAST_ZIP_LISTING:-}"
  local dat_content="${LAST_DAT_CONTENT:-}"

  # Verify that attachment files are present (using cached zip listing)
  local attachment_files=$(echo "$zip_listing" | grep "attachment.*\.\(pdf\|jpg\|tiff\)$" | wc -l)

  if [[ "$attachment_files" -ne "$expected_attachment_count" ]]; then
    print_error "Expected exactly $expected_attachment_count attachment files in ZIP, but found $attachment_files."
  fi
  print_info "Found $attachment_files attachment files in ZIP archive (expected exactly $expected_attachment_count)."

  # Verify that some attachments are listed in the .dat file (using cached content)
  local attachment_count=$(echo "$dat_content" | grep -c "attachment" || true)
  if [[ "$attachment_count" -ne "$expected_attachment_count" ]]; then
    print_error "Expected exactly $expected_attachment_count attachments in .dat file, found $attachment_count."
  fi
  print_info "Found $attachment_count attachments in .dat file."

  # Verify attachment text files if text extraction is enabled (using cached zip listing)
  if [[ "$check_text" = "true" ]]; then
    local attachment_text_files=$(echo "$zip_listing" | grep "attachment.*\.txt$" | wc -l)
    if [[ "$attachment_text_files" -ne "$expected_attachment_count" ]]; then
      print_error "Expected exactly $expected_attachment_count attachment text files, but found $attachment_text_files."
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

    local first_bytes
    if command -v xxd >/dev/null 2>&1; then
        first_bytes=$(head -c 3 "$extracted_dat" | xxd -p | tr -d ' ' | tr a-z A-Z)
    elif command -v od >/dev/null 2>&1; then
        first_bytes=$(od -An -tx1 -N3 "$extracted_dat" | tr -d ' \n' | tr a-z A-Z)
    else
        first_bytes=$(python3 -c 'import sys; print(open(sys.argv[1], "rb").read(3).hex().upper())' "$extracted_dat" 2>/dev/null || true)
    fi
    if [[ "$encoding" = "UTF-8" ]]; then
        if [[ "$first_bytes" != "EFBBBF" ]]; then
            print_error "Missing UTF-8 BOM in extracted .dat file."
            AGGREGATE_FAILURE=1; return 0
        fi
    elif [[ "$encoding" = "UTF-16" ]]; then
        if [[ "${first_bytes:0:4}" != "FFFE" ]]; then
            print_error "Missing UTF-16LE BOM in extracted .dat file."
            AGGREGATE_FAILURE=1; return 0
        fi
    elif [[ "$encoding" = "ANSI" ]]; then
        if [[ "${first_bytes:0:6}" == "EFBBBF" ]] || [[ "${first_bytes:0:4}" == "FFFE" ]]; then
            print_error "Unexpected BOM in ANSI extracted .dat file."
            AGGREGATE_FAILURE=1; return 0
        fi
    fi

    # Ensure trailing newline
    local last_char=$($dat_content_cmd < "$extracted_dat" | tail -c 1)
    if [[ -n "$last_char" ]]; then
        print_error "Missing trailing newline in extracted .dat file."
        AGGREGATE_FAILURE=1; return 0
    fi

    # Read content once for verification
    local dat_content=$($dat_content_cmd < "$extracted_dat")

    # Verify line count in extracted .dat file (+1 for header) using cached content
    local line_count=$(printf '%s\n' "$dat_content" | wc -l)
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
    local zip_file_count=$(echo "$zip_listing" | grep -c "\.$file_type$" || true)
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

# Source the shared binary-resolution helper so all inline cases and parallel standalone suites share one Release build.
# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"
export ZIPPER_EXECUTABLE="${ZIPPER_EXECUTABLE:-${_ZIPPER_BIN:-}}"

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
verify_output "$TEST_OUTPUT_DIR/pdf_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size,Compression Method" "pdf" "false" "UTF-8"
print_success "Test Case 6 passed."

# Test Case 7: All options combined
run_test_case "Test Case 7: All options combined" --type tiff --count 15 --output-path "$TEST_OUTPUT_DIR/all_options" --folders 5 --encoding ANSI --distribution gaussian --with-metadata
verify_output "$TEST_OUTPUT_DIR/all_options" 15 "Control Number,File Path,Custodian,Date Sent,Author,File Size,Compression Method" "tiff" "false" "ANSI"
print_success "Test Case 7 passed."

# Test Case 8: With text
run_test_case "Test Case 8: With text" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_with_text" --with-text
verify_output "$TEST_OUTPUT_DIR/pdf_with_text" 10 "Control Number,File Path,Extracted Text" "pdf" "true" "UTF-8"
print_success "Test Case 8 passed."

# Test Case 9: With text and metadata
run_test_case "Test Case 9: With text and metadata" --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_with_text_and_metadata" --with-text --with-metadata
verify_output "$TEST_OUTPUT_DIR/pdf_with_text_and_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size,Compression Method,Extracted Text" "pdf" "true" "UTF-8"
print_success "Test Case 9 passed."

# Test Case 10: EML generation with attachments
run_test_case "Test Case 10: EML generation with attachments" --type eml --count 20 --output-path "$TEST_OUTPUT_DIR/eml_attachments" --attachment-rate 50 --seed 42
verify_eml_output "$TEST_OUTPUT_DIR/eml_attachments" 20 "Control Number,File Path,To,From,Subject,Sent Date,Attachment" "eml" "false" "UTF-8" 14
print_success "Test Case 10 passed."

# Test Case 11: EML generation with metadata
run_test_case "Test Case 11: EML generation with metadata" --type eml --count 10 --output-path "$TEST_OUTPUT_DIR/eml_metadata" --with-metadata
verify_output "$TEST_OUTPUT_DIR/eml_metadata" 10 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Compression Method,Attachment" "eml" "false" "UTF-8"
print_success "Test Case 11 passed."

# Test Case 12: EML generation with text
run_test_case "Test Case 12: EML generation with text" --type eml --count 10 --output-path "$TEST_OUTPUT_DIR/eml_text" --with-text
verify_output "$TEST_OUTPUT_DIR/eml_text" 10 "Control Number,File Path,To,From,Subject,Sent Date,Attachment,Extracted Text" "eml" "true" "UTF-8"
print_success "Test Case 12 passed."

# Test Case 13: EML generation with metadata and text
run_test_case "Test Case 13: EML generation with metadata and text" --type eml --count 10 --output-path "$TEST_OUTPUT_DIR/eml_metadata_text" --with-metadata --with-text
verify_output "$TEST_OUTPUT_DIR/eml_metadata_text" 10 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Compression Method,Attachment,Extracted Text" "eml" "true" "UTF-8"
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
verify_eml_output "$TEST_OUTPUT_DIR/eml_attachments_full" 15 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Compression Method,Attachment,Extracted Text" "eml" "true" "UTF-8" 13
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

# Test Case 21: Unknown CLI arguments hard-fail, including value position
print_info "START: Test Case 21: Unknown CLI arguments hard-fail"
if zipper --type pdf --count 1 --output-path "$TEST_OUTPUT_DIR/unknown_arg" --unknown-flag > /tmp/zipper_unknown_arg.out 2> /tmp/zipper_unknown_arg.err; then
  print_error "Test 21: Unknown flag should fail"
fi
if ! grep -q "Unknown argument or unconsumed value '--unknown-flag'" /tmp/zipper_unknown_arg.err; then
  print_error "Test 21: Unknown flag error message not found"
fi
if zipper --type pdf --count 1 --output-path --unknown-flag > /tmp/zipper_unknown_value.out 2> /tmp/zipper_unknown_value.err; then
  print_error "Test 21: Unknown flag in value position should fail"
fi
if ! grep -q "requires a value" /tmp/zipper_unknown_value.err; then
  print_error "Test 21: Unknown flag in value position did not fail as a missing value"
fi
if zipper --version --unknown-flag > /tmp/zipper_version_unknown.out 2> /tmp/zipper_version_unknown.err; then
  print_error "Test 21: --version with unknown flag should fail"
fi
print_success "Test Case 21 passed."

# --- Cleanup ---

print_info "Cleaning up test output..."
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# --- Standalone Feature Test Suites ---
# Run all standalone test scripts to ensure comprehensive coverage

print_info "Running standalone feature test suites (jobs=$RUN_TESTS_JOBS)..."

# Test 1: EML comprehensive tests
run_standalone_suite "EML comprehensive tests" bash ./tests/test-eml-comprehensive.sh

# Test 2: Bates numbering tests
run_standalone_suite "Bates Numbering Tests" bash ./tests/test-bates-numbering.sh

run_standalone_suite "Production Sets Tests" bash ./tests/test-production-sets.sh

# Test 3: Multipage TIFF tests
run_standalone_suite "multipage TIFF tests" bash ./tests/test-multipage-tiff.sh

# Test 4: Office formats tests
run_standalone_suite "office formats tests" bash ./tests/test-office-formats.sh

# Test 5: Load file formats tests
run_standalone_suite "load file formats tests" bash ./tests/test-load-file-formats.sh

# Test 6: Artifact handling tests
run_standalone_suite "artifact handling tests" bash ./tests/test-artifact-handling.sh

# Test 7: Cross-platform tests
run_standalone_suite "cross-platform tests" bash ./tests/test-cross-platform.sh

# Test 10: Path traversal security tests
run_standalone_suite "path traversal security tests" bash ./tests/test-path-traversal-security.sh

# Test 11: Unified workflow tests
run_standalone_suite "unified workflow tests" bash ./tests/test-unified-workflow.sh

# Test 11b: Archive Test workflow CLI tests
run_standalone_suite "Archive Test suite CLI tests" bash ./tests/test-archive-test-suites.sh

# Test 12: Loadfile-only and Chaos Engine tests
run_standalone_suite "loadfile-only and Chaos Engine tests" bash ./tests/run-e2e-loadfile.sh

# Test 13: Chaos anomaly-type and scenario coverage
run_standalone_suite "chaos anomaly coverage tests" bash ./tests/test-chaos-anomaly-coverage.sh

# Test 14: Column-profile built-in matrix (40 combos: 4 profiles × 5 file types × 2 seeds)
run_standalone_suite "column-profile built-in matrix tests" bash ./tests/test-column-profile-builtin-matrix.sh

# Test 15: Column-profile custom every-kind profile
run_standalone_suite "column-profile custom-kinds test" bash ./tests/test-column-profile-custom-kinds.sh

# Test 16: Column-profile EmptyPercentage chi-square
run_standalone_suite "column-profile empty-percentage tests" bash ./tests/test-column-profile-empty-pct.sh

run_standalone_suite "target-zip-size accuracy tests" bash ./tests/test-target-zip-size.sh

run_standalone_suite "argument-interaction conflict tests" bash ./tests/test-argument-interactions.sh

run_standalone_suite "CLI coverage gap tests" bash ./tests/test-cli-coverage-gaps.sh

# Keep the Windows wrapper's child exit codes guarded (#1040)
run_standalone_suite "Run-tests child-exit-code guard parity tests" bash ./tests/test-run-tests-guard-parity.sh

run_standalone_suite "mixed file types tests" bash ./tests/test-mixed-file-types.sh

run_standalone_suite "source-driven generation tests" bash ./tests/test-source-driven.sh

run_standalone_suite "CI build properties and docs-only classification tests" bash ./tests/test-ci-build-props.sh

run_standalone_suite "CI per-file coverage gate action tests" bash ./.github/actions/coverage-gate/test-coverage-gate.sh

# Wait for all parallel suites before running test-perf-measure.sh (which counts /tmp/tmp.* directories)
flush_parallel_suites

print_info "Running performance measurement regression tests..."
bash ./tests/test-perf-measure.sh || print_error "test-perf-measure.sh failed."
python3 -m unittest discover -s tests/perf -p 'test_perf_compare.py' || print_error "test_perf_compare.py failed."
print_success "Performance measurement regression tests passed."

print_info "Running stress test harness regressions..."
if bash ./tests/test-stress-harness.sh; then
    print_success "Stress test harness regressions passed."
else
    print_error "test-stress-harness.sh failed."
fi

print_info "Running TypeSafe audit foundation tests..."
if ! bash ./tests/test-typesafe-audit.sh; then
    print_error "test-typesafe-audit.sh failed."
    exit 1
fi
print_success "TypeSafe audit foundation tests passed."

print_info "Running golden helper and runner regression tests..."
if python3 -m unittest discover -s tests/goldens/lib/_test -p 'test_*.py'; then
    print_success "Golden helper and runner regression tests passed."
else
    print_error "Golden helper and runner regression tests failed."
fi

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
