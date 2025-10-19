#!/bin/bash

# --- Optimized Pre-commit Test Configuration ---
# This script runs unit tests + one basic E2E test for faster pre-commit checks

# The directory where test output will be generated.
TEST_OUTPUT_DIR="./test_output"

# The .NET project to run.
PROJECT="Zipper/Zipper.csproj"

# --- Helper Functions ---

print_success() {
    echo "[ SUCCESS ] $1"
}

print_info() {
    echo "[ INFO ] $1"
}

print_error() {
    echo "[ ERROR ] $1"
    exit 1
}

verify_output() {
    local test_dir="$1"
    local expected_count="$2"
    local expected_header_str="$3"
    local file_type="$4"

    print_info "Verifying output in $test_dir"

    zip_file=$(find "$test_dir" -name "*.zip" | head -1)
    dat_file=$(find "$test_dir" -name "*.dat" | head -1)

    if [ -z "$zip_file" ]; then
        print_error "No .zip file found in $test_dir"
    fi
    if [ -z "$dat_file" ]; then
        print_error "No .dat file found in $test_dir"
    fi

    # Verify line count in .dat file (+1 for header)
    line_count=$(wc -l < "$dat_file")
    expected_line_count=$((expected_count + 1))
    if [ "$line_count" -ne "$expected_line_count" ]; then
        print_error "Incorrect line count in .dat file. Expected $expected_line_count, found $line_count."
    fi
    print_info ".dat file line count is correct ($line_count)."

    # Verify header
    header=$(head -n 1 "$dat_file")
    IFS=',' read -ra headers <<< "$expected_header_str"
    for header_field in "${headers[@]}"; do
        if [[ "$header" != *"$header_field"* ]]; then
            print_error "Header validation failed. Expected to find '$header_field' in '$header'."
        fi
    done
    print_info ".dat file header is correct."

    # Verify file count in zip
    zip_file_count=$(unzip -l "$zip_file" | grep "\.$file_type$" | wc -l)
    if [ "$zip_file_count" -ne "$expected_count" ]; then
        print_error "Incorrect file count in .zip file. Expected $expected_count, found $zip_file_count."
    fi
    print_info ".zip file count for .$file_type is correct ($zip_file_count)."
}

# --- Optimized Test Suite ---

print_info "Running optimized pre-commit test suite..."

# Step 1: Run Unit Tests
print_info "Running unit tests..."
dotnet test Zipper/Zipper.Tests/ --verbosity quiet
if [ $? -ne 0 ]; then
    print_error "Unit tests failed"
fi

print_success "Unit tests passed."

# Step 2: Run One Basic E2E Test
print_info "Running basic E2E test..."

# Create a temporary directory for test output.
if [ -d "$TEST_OUTPUT_DIR" ]; then
    rm -rf "$TEST_OUTPUT_DIR"
fi
mkdir -p "$TEST_OUTPUT_DIR"

# Basic PDF generation test (Test Case 1 from full suite)
print_info "Running E2E Test: Basic PDF generation"
dotnet run --project "$PROJECT" -- --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_basic"
if [ $? -ne 0 ]; then
    print_error "E2E test failed"
fi
verify_output "$TEST_OUTPUT_DIR/pdf_basic" 10 "Control Number,File Path" "pdf"

# --- Cleanup ---
print_info "Cleaning up test output..."
rm -rf "$TEST_OUTPUT_DIR"

print_success "Optimized pre-commit tests passed successfully!"
exit 0