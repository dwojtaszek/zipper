#!/bin/bash
# Comprehensive EML Test Suite - Unix Version (Linux/macOS)
# Constitutional Requirement: Must test ALL EML functionality scenarios
# Tests both Windows and Unix compatibility for EML feature implementation

# set -e # Exit on any error - disabled for better debugging

echo "========================================"
echo "Comprehensive EML Test Suite - Unix"
echo "========================================"
echo

# Set test environment
TEST_DIR="/tmp/zipper-eml-test-$$"
REPO_ROOT="$(pwd)"
ZIPPER_CMD="dotnet run --project $REPO_ROOT/Zipper/Zipper.csproj --"
FILE_COUNT=20 # Use a consistent number of files for most tests

# Clean up function
cleanup() {
    if [ -d "$TEST_DIR" ]; then
        echo "Cleaning up test directory: $TEST_DIR"
        rm -rf "$TEST_DIR"
    fi
}

# Set trap for cleanup on exit
trap cleanup EXIT

# Build the project first
echo "Building Zipper project..."
if ! dotnet build Zipper/Zipper.csproj > /dev/null 2>&1; then
    echo "✗ Build failed. Exiting."
    exit 1
fi

echo "Creating test directory: $TEST_DIR"
mkdir -p "$TEST_DIR"

# Test counters
TEST_COUNT=0
PASSED_COUNT=0

# Function to check if a header contains all expected columns
verify_headers() {
    local header_line="$1"
    shift
    local expected_headers=("$@")
    local all_found=true

    for expected in "${expected_headers[@]}"; do
        if [[ "$header_line" != *"$expected"* ]]; then
            echo "  ✗ Header check FAILED. Missing expected column: '$expected'"
            all_found=false
        fi
    done

    if [ "$all_found" = true ]; then
        echo "  ✓ Header check PASSED. All expected columns are present."
    fi
    return $([ "$all_found" = true ] && echo 0 || echo 1)
}

# Function to verify attachments
verify_attachments() {
    local dat_file="$1"
    local zip_file="$2"
    local attachment_rate="$3"
    local eml_count="$4"

    echo "  - Verifying attachments..."
    
    local header
    header=$(head -n 1 "$dat_file")

    local attachment_col_index
    attachment_col_index=$(echo "$header" | tr '\024' '\n' | grep -n 'þAttachmentþ' | cut -d: -f1)

    if [ -z "$attachment_col_index" ]; then
        echo "  ✗ Could not find 'Attachment' column in DAT file."
        return 1
    fi

    local attachment_dat_count
    attachment_dat_count=$(tail -n +2 "$dat_file" | cut -d$'\024' -f"$attachment_col_index" | grep -c -v '^þþ$')
    
    local attachment_zip_count
    attachment_zip_count=$(unzip -Z -1 "$zip_file" | grep -c -v -E '\.eml$|\.txt$')

    echo "  - Attachments found in ZIP: $attachment_zip_count"
    echo "  - Attachments referenced in DAT: $attachment_dat_count"

    if [ "$attachment_dat_count" -ne "$attachment_zip_count" ]; then
        echo "  ✗ Mismatch between attachments in ZIP ($attachment_zip_count) and references in DAT file ($attachment_dat_count)."
        return 1
    fi

    local min_expected
    min_expected=$(echo "$eml_count * $attachment_rate / 100 * 0.5" | bc -l | awk '{print int($1)}')
    if [ "$attachment_dat_count" -ge "$min_expected" ]; then
        echo "  ✓ Attachment count ($attachment_dat_count) is plausible for a $attachment_rate% rate."
        return 0
    else
        echo "  ✗ Attachment count ($attachment_dat_count) seems too low for a $attachment_rate% rate (expected at least $min_expected)."
        return 1
    fi
}


# Main test function
run_test() {
    ((TEST_COUNT++))
    local test_name="$1"
    local command_args="$2"
    local expected_headers=("${@:3}") # All args from 3rd onwards are headers
    local check_text=false
    local check_attachments=false
    local attachment_rate=0

    # Check for special flags in the headers array
    if [[ " ${expected_headers[*]} " =~ " --check-text " ]]; then
        check_text=true
        expected_headers=("${expected_headers[@]/--check-text/}")
    fi
    if [[ " ${expected_headers[*]} " =~ " --check-attachments " ]]; then
        check_attachments=true
        attachment_rate=$(echo "$command_args" | grep -oP '(?<=--attachment-rate )\d+')
        expected_headers=("${expected_headers[@]/--check-attachments/}")
    fi


    echo
    echo "--------------------------------------------------"
    echo "Test $TEST_COUNT: $test_name"
    echo "--------------------------------------------------"

    local test_path="$TEST_DIR/test_$TEST_COUNT"
    mkdir -p "$test_path"
    
    local full_command="$ZIPPER_CMD $command_args --output-path $test_path"
    echo "  - Executing: $full_command"

    if $full_command > "$test_path/test_output.log" 2>&1; then
        echo "  ✓ Command executed successfully."
        
        local archive_file
        archive_file=$(find "$test_path" -name "archive_*.zip")
        local dat_file
        dat_file=$(find "$test_path" -name "archive_*.dat")

        if [ -z "$archive_file" ]; then
            echo "  ✗ Test FAILED. Archive file not created."
            return
        fi
        if [ -z "$dat_file" ]; then
            echo "  ✗ Test FAILED. Load file not created."
            return
        fi

        echo "  - Archive and load file created."
        local header_line
        header_line=$(head -n 1 "$dat_file")

        if verify_headers "$header_line" "${expected_headers[@]}"; then
            local all_checks_passed=true
            
            if [ "$check_text" = true ]; then
                echo "  - Verifying extracted text files..."
                local eml_count
                eml_count=$(unzip -Z -1 "$archive_file" | grep -c '\.eml$')
                local txt_count
                txt_count=$(unzip -Z -1 "$archive_file" | grep -c '\.txt$')
                local attachment_count
                attachment_count=$(unzip -Z -1 "$archive_file" | grep -c -v -E '\.eml$|\.txt$')
                local expected_txt_count=$((eml_count + attachment_count))

                if [ "$expected_txt_count" -eq "$txt_count" ]; then
                    echo "  ✓ Correct number of text files found ($txt_count)."
                else
                    echo "  ✗ Mismatch: Expected $expected_txt_count TXT files, but found $txt_count."
                    all_checks_passed=false
                fi
            fi

            if [ "$check_attachments" = true ]; then
                local eml_count
                eml_count=$(unzip -Z -1 "$archive_file" | grep -c '\.eml$')
                if ! verify_attachments "$dat_file" "$archive_file" "$attachment_rate" "$eml_count"; then
                    all_checks_passed=false
                fi
            fi

            if [ "$all_checks_passed" = true ]; then
                echo "✓ Test $TEST_COUNT PASSED"
                ((PASSED_COUNT++))
            else
                echo "✗ Test $TEST_COUNT FAILED due to verification errors."
            fi
        else
            echo "✗ Test $TEST_COUNT FAILED due to header mismatch."
        fi
    else
        echo "✗ Test $TEST_COUNT FAILED - Command execution failed."
        cat "$test_path/test_output.log"
    fi
}

# ========================================
# Test Scenarios
# ========================================

# Define base headers common to all EML tests
base_headers=("Control Number" "File Path" "Custodian" "Date Sent" "Author" "File Size" "To" "From" "Subject" "Sent Date" "Attachment")
# Metadata headers are now part of the base for EMLs, so this can be empty or used for non-EML tests if needed.
metadata_headers=()
text_header="Extracted Text"

# Test 1: Basic EML Generation
# EMLs get metadata headers by default, so we expect the full set.
run_test "Basic EML Generation" \
    "--type eml --count $FILE_COUNT" \
    "${base_headers[@]}"

# Test 2: EML with Metadata
# This is redundant for EMLs but should be tested to ensure it doesn't break anything. Result is the same as basic.
run_test "EML with Metadata" \
    "--type eml --count $FILE_COUNT --with-metadata" \
    "${base_headers[@]}"

# Test 3: EML with Extracted Text
run_test "EML with Extracted Text" \
    "--type eml --count $FILE_COUNT --with-text" \
    "${base_headers[@]}" "$text_header" "--check-text"

# Test 4: EML with Both Metadata and Text
run_test "EML with Metadata and Text" \
    "--type eml --count $FILE_COUNT --with-metadata --with-text" \
    "${base_headers[@]}" "$text_header" "--check-text"

# Test 5: EML with Attachments (and all flags)
run_test "EML with Attachments, Metadata, and Text" \
    "--type eml --count $FILE_COUNT --with-metadata --with-text --attachment-rate 80" \
    "${base_headers[@]}" "$text_header" "--check-text" "--check-attachments"

# Test 6: High Volume Performance Test
run_test "High Volume Performance Test" \
    "--type eml --count 500 --folders 10 --with-metadata --with-text --attachment-rate 25" \
    "${base_headers[@]}" "$text_header" "--check-text" "--check-attachments"


# ========================================
# Test Results Summary
# ========================================

echo
echo "========================================"
echo "Test Results Summary"
echo "========================================"
echo "Total Tests: $TEST_COUNT"
echo "Passed: $PASSED_COUNT"
FAILED_COUNT=$((TEST_COUNT - PASSED_COUNT))
echo "Failed: $FAILED_COUNT"

if [ $FAILED_COUNT -eq 0 ]; then
    echo
    echo "✓ ALL TESTS PASSED - EML feature implementation is working correctly"
    exit 0
else
    echo
    echo "✗ SOME TESTS FAILED - Please review the implementation"
    exit 1
fi