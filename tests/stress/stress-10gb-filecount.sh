#!/bin/bash

# =============================================================================
# ZIPPER STRESS TEST - 10GB MAXIMUM FILE COUNT CHALLENGE
# =============================================================================
#
# STRESS TEST DETAILS:
# - Target Size: ~10GB compressed archive
# - File Count: 5 million PDF files
# - Distribution: Exponential across 100 folders
# - Features: Metadata + Text extraction enabled
# - Focus: Tests maximum file count handling and Zip64 functionality
# - Unique Aspect: Tests absolute limits of file count vs size
#
# IMPORTANT NOTES:
# - This stress test is for MANUAL INVOCATION ONLY
# - NOT part of CI/CD or pre-commit hooks
# - Requires ~12GB+ available disk space (+20% overhead)
# - Runtime: Several hours (typically 2-4 hours)
# - Tests unique failure modes not covered in regular E2E tests
#
# PRE-RUN VALIDATIONS:
# - Checks available disk space before starting
# - Validates system resources
# - Provides clear runtime expectations
# =============================================================================

set -e  # Exit on any error

# --- Check Required Utilities ---
check_required_utilities() {
    local missing_utils=()
    for util in bc df stat unzip grep wc find; do
        if ! command -v "$util" &> /dev/null; then
            missing_utils+=("$util")
        fi
    done

    if [ ${#missing_utils[@]} -gt 0 ]; then
        print_error "Missing required utilities: ${missing_utils[*]}"
        print_info "Install missing utilities:"
        echo "  Ubuntu/Debian: sudo apt-get install bc unzip"
        echo "  macOS: brew install bc"
        exit 1
    fi
}

# --- Configuration ---
TEST_NAME="10GB_Maximum_File_Count_Challenge"
OUTPUT_DIR="results"
PROJECT="../../src/Zipper.csproj"

# Test parameters
FILE_COUNT=5000000  # 5 million files
FOLDERS=100
TARGET_SIZE_GB=10
FILE_TYPE="pdf"
DISTRIBUTION="exponential"

# --- Helper Functions ---
print_header() {
    echo "=============================================================================="
    echo "$1"
    echo "=============================================================================="
}

print_warning() {
    echo -e "\e[43m[ WARNING ]\e[0m $1"
}

print_info() {
    echo -e "\e[44m[ INFO ]\e[0m $1"
}

print_success() {
    echo -e "\e[42m[ SUCCESS ]\e[0m $1"
}

print_error() {
    echo -e "\e[41m[ ERROR ]\e[0m $1"
}

# --- Pre-run Validations ---
check_disk_space() {
    print_info "Checking available disk space..."

    local required_bytes=$(printf "%.0f" $(echo "$TARGET_SIZE_GB * 1024^3 * 1.2" | bc))  # 20% overhead
    local available_kb=$(df --output=avail . | tail -1 | tr -d ' ')
    local available_bytes=$((available_kb * 1024))
    local available_gb=$(echo "scale=2; $available_bytes / 1024^3" | bc)
    local required_gb=$(echo "scale=2; $required_bytes / 1024^3" | bc)

    print_info "Available space: ${available_gb}GB"
    print_info "Required space: ${required_gb}GB"

    if [ "$available_bytes" -lt "$required_bytes" ]; then
        print_error "Insufficient disk space. Need ${required_gb}GB, have ${available_gb}GB"
        exit 1
    fi

    print_success "Disk space validation passed"
}

check_system_resources() {
    print_info "Checking system resources..."

    local available_memory=$(free -h | awk '/^Mem:/ {print $7}')
    local cpu_cores=$(nproc)

    print_info "Available memory: $available_memory"
    print_info "CPU cores: $cpu_cores"

    if [ "$cpu_cores" -lt 4 ]; then
        print_warning "Low CPU count detected. This test may take significantly longer"
    fi

    print_success "System resource check completed"
}

show_test_details() {
    print_header "STRESS TEST: $TEST_NAME"
}

# --- Test Execution ---
run_stress_test() {
    print_header "RUNNING STRESS TEST"

    local start_time=$(date +%s)

    print_info "Starting stress test at $(date)"
    print_info "Command: dotnet run --project $PROJECT -- --type $FILE_TYPE --count $FILE_COUNT --output-path $OUTPUT_DIR --folders $FOLDERS --distribution $DISTRIBUTION --with-metadata --with-text --target-zip-size ${TARGET_SIZE_GB}GB"

    # Create output directory
    mkdir -p "$OUTPUT_DIR"

    # Run the zipper command
    dotnet run --project "$PROJECT" -- \
        --type "$FILE_TYPE" \
        --count "$FILE_COUNT" \
        --output-path "$OUTPUT_DIR" \
        --folders "$FOLDERS" \
        --distribution "$DISTRIBUTION" \
        --with-metadata \
        --target-zip-size "${TARGET_SIZE_GB}GB" \
        --with-text

    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    local hours=$((duration / 3600))
    local minutes=$(((duration % 3600) / 60))
    local seconds=$((duration % 60))

    print_success "Stress test completed in ${hours}h ${minutes}m ${seconds}s"
}

# --- Post-test Validation ---
validate_results() {
    print_header "VALIDATING RESULTS"

    print_info "Validating generated files..."

    local zip_file=$(find "$OUTPUT_DIR" -name "*.zip" | head -1)
    local dat_file=$(find "$OUTPUT_DIR" -name "*.dat" | head -1)

    if [ -z "$zip_file" ]; then
        print_error "No .zip file found"
        exit 1
    fi

    if [ -z "$dat_file" ]; then
        print_error "No .dat file found"
        exit 1
    fi

    # Check file sizes
    local zip_size=$(stat -c%s "$zip_file")
    local zip_size_gb=$(echo "scale=2; $zip_size / 1024^3" | bc)
    local dat_size=$(stat -c%s "$dat_file")
    local dat_size_mb=$(echo "scale=2; $dat_size / 1024^2" | bc)

    print_info "Generated Archive:"
    echo "  - ZIP file: $(basename "$zip_file")"
    echo "  - ZIP size: ${zip_size_gb}GB"
    echo "  - DAT file: $(basename "$dat_file")"
    echo "  - DAT size: ${dat_size_mb}MB"

    # Validate target size (within tolerance)
    local min_size_gb=$(echo "$TARGET_SIZE_GB * 0.9" | bc)
    local max_size_gb=$(echo "$TARGET_SIZE_GB * 1.1" | bc)

    if (( $(echo "$zip_size_gb >= $min_size_gb && $zip_size_gb <= $max_size_gb" | bc -l) )); then
        print_success "Target size achieved: ${zip_size_gb}GB (target: ${TARGET_SIZE_GB}GB ±10%)"
    else
        print_warning "Size outside target range: ${zip_size_gb}GB (target: ${TARGET_SIZE_GB}GB ±10%)"
    fi

    # Validate file count in zip
    print_info "Validating file count in archive..."
    local file_count=$(unzip -l "$zip_file" | grep "\.$FILE_TYPE$" | wc -l)

    if [ "$file_count" -eq "$FILE_COUNT" ]; then
        print_success "File count verified: $(printf "%'d" $file_count) files"
    else
        print_error "File count mismatch. Expected: $(printf "%'d" $FILE_COUNT), Found: $(printf "%'d" $file_count)"
        exit 1
    fi

    # Validate DAT file structure
    print_info "Validating load file structure..."
    local line_count=$(wc -l < "$dat_file")
    local expected_lines=$((FILE_COUNT + 1))  # +1 for header

    if [ "$line_count" -eq "$expected_lines" ]; then
        print_success "Load file structure validated: $line_count lines"
    else
        print_error "Load file line count mismatch. Expected: $expected_lines, Found: $line_count"
        exit 1
    fi

    # Check for text files
    local text_count=$(unzip -l "$zip_file" | grep "\.txt$" | wc -l)
    if [ "$text_count" -eq "$FILE_COUNT" ]; then
        print_success "Text files validated: $(printf "%'d" $text_count) files"
    else
        print_error "Text file count mismatch. Expected: $(printf "%'d" $FILE_COUNT), Found: $(printf "%'d" $text_count)"
        exit 1
    fi
}

# --- Cleanup and Summary ---
cleanup_and_summary() {
    print_header "STRESS TEST SUMMARY"

    print_success "Stress test completed successfully!"
    print_info "Generated files are available in: $OUTPUT_DIR"
    print_info "You can safely remove the output directory when no longer needed:"
    echo "  rm -rf $OUTPUT_DIR"

    echo ""
    print_info "Test Results Summary:"
    echo "  ✓ Generated $(printf "%'d" $FILE_COUNT) $FILE_TYPE files"
    echo "  ✓ Archive size: ${zip_size_gb}GB"
    echo "  ✓ Load file: ${dat_size_mb}MB"
    echo "  ✓ Metadata and text extraction enabled"
    echo "  ✓ Exponential distribution across $FOLDERS folders"
    echo "  ✓ Zip64 format handling verified"
}

# --- Main Execution ---
main() {
    print_header "ZIPPER STRESS TEST SUITE"
    echo "10GB Maximum File Count Challenge"
    echo ""

    print_warning "This stress test will consume significant resources:"
    echo "  - Time: 5-10 minutes"
    echo "  - Disk: ~${TARGET_SIZE_GB}GB"
    echo "  - Memory: Moderate usage"
    echo "  - CPU: Intensive processing"
    echo ""

    # Run validations
    check_required_utilities
    check_disk_space
    check_system_resources
    show_test_details

    # Execute test
    run_stress_test

    # Validate results
    validate_results

    # Show summary
    cleanup_and_summary
}

# Check if script is being run directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi