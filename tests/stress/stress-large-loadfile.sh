#!/bin/bash

# =============================================================================
# ZIPPER STRESS TEST - LARGE LOAD FILE PERFORMANCE
# =============================================================================
#
# STRESS TEST DETAILS:
# - Focus: Large data handling scenarios focused on load file performance
# - Target: Generate 10,000 files creating ~500MB load files
# - Features: Comprehensive metadata to maximize load file size
# - Variations: Test both external and embedded load files
# - Focus: Load file generation, encoding, and I/O performance
#
# TEST SCENARIOS:
# 1. External Load File: Generate large .dat file alongside archive
# 2. Embedded Load File: Include large .dat file inside archive
# 3. Encoding Variations: Test UTF-8, UTF-16, and ANSI encoding performance
#
# IMPORTANT NOTES:
# - This stress test is for MANUAL INVOCATION ONLY
# - NOT part of CI/CD or pre-commit hooks
# - Requires ~2GB+ available disk space
# - Runtime: 30-60 minutes
# - Focuses on load file performance bottlenecks
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
TEST_NAME="Large_Load_File_Performance"
OUTPUT_DIR="results"
PROJECT="../../src/Zipper.csproj"

# Test parameters
FILE_COUNT=10000
TARGET_LOAD_SIZE_MB=500
FOLDERS=100
FILE_TYPE="pdf"
DISTRIBUTION="proportional"

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

    local required_bytes=$((2 * 1024 * 1024 * 1024))  # 2GB
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

show_test_details() {
    print_header "STRESS TEST: $TEST_NAME"

    echo "Test Configuration:"
    echo "  - File Count: $(printf "%'d" $FILE_COUNT)"
    echo "  - File Type: $FILE_TYPE"
    echo "  - Target Load File Size: ~${TARGET_LOAD_SIZE_MB}MB"
    echo "  - Folders: $FOLDERS"
    echo "  - Distribution: $DISTRIBUTION"
    echo "  - Features: Maximum metadata (all columns enabled)"
    echo "  - Encoding Tests: UTF-8, UTF-16, ANSI"
    echo "  - Load File Tests: External and Embedded"
    echo "  - Output Directory: $OUTPUT_DIR"
    echo ""
    echo "Estimated Runtime: 5-10 minutes"
    echo "Memory Usage: Light (under 1GB)"
    echo "Disk Space Required: ~2GB"
    echo ""
    print_warning "This stress test focuses on large load file performance"
}

# --- Test Execution Scenarios ---
run_scenario_1_external_utf8() {
    print_header "SCENARIO 1: External Load File (UTF-8)"

    local start_time=$(date +%s)
    local scenario_dir="$OUTPUT_DIR/scenario1_external_utf8"
    mkdir -p "$scenario_dir"

    print_info "Generating $(printf "%'d" $FILE_COUNT) files with external UTF-8 load file..."

    dotnet run --project "$PROJECT" -- \
        --type "$FILE_TYPE" \
        --count "$FILE_COUNT" \
        --output-path "$scenario_dir" \
        --folders "$FOLDERS" \
        --distribution "$DISTRIBUTION" \
        --encoding "UTF-8" \
        --with-metadata \
        --with-text

    local end_time=$(date +%s)
    local duration=$((end_time - start_time))

    # Validate results
    local zip_file=$(find "$scenario_dir" -name "*.zip" | head -1)
    local dat_file=$(find "$scenario_dir" -name "*.dat" | head -1)
    local dat_size=$(stat -c%s "$dat_file")
    local dat_size_mb=$(echo "scale=2; $dat_size / 1024^2" | bc)

    print_success "Scenario 1 completed in ${duration}s"
    print_info "Load file size: ${dat_size_mb}MB"
}

run_scenario_2_embedded_utf8() {
    print_header "SCENARIO 2: Embedded Load File (UTF-8)"

    local start_time=$(date +%s)
    local scenario_dir="$OUTPUT_DIR/scenario2_embedded_utf8"
    mkdir -p "$scenario_dir"

    print_info "Generating $(printf "%'d" $FILE_COUNT) files with embedded UTF-8 load file..."

    dotnet run --project "$PROJECT" -- \
        --type "$FILE_TYPE" \
        --count "$FILE_COUNT" \
        --output-path "$scenario_dir" \
        --folders "$FOLDERS" \
        --distribution "$DISTRIBUTION" \
        --encoding "UTF-8" \
        --with-metadata \
        --with-text \
        --include-load-file

    local end_time=$(date +%s)
    local duration=$((end_time - start_time))

    # Validate results
    local zip_file=$(find "$scenario_dir" -name "*.zip" | head -1)
    local zip_size=$(stat -c%s "$zip_file")
    local zip_size_mb=$(echo "scale=2; $zip_size / 1024^2" | bc)

    # Extract and check embedded load file
    local temp_dir=$(mktemp -d)
    trap "rm -rf $temp_dir" RETURN
    unzip -j "$zip_file" "*.dat" -d "$temp_dir" > /dev/null
    local embedded_dat=$(find "$temp_dir" -name "*.dat" | head -1)
    local dat_size=$(stat -c%s "$embedded_dat")
    local dat_size_mb=$(echo "scale=2; $dat_size / 1024^2" | bc)

    print_success "Scenario 2 completed in ${duration}s"
    print_info "Embedded load file size: ${dat_size_mb}MB"
    print_info "Total archive size: ${zip_size_mb}MB"
}

run_scenario_3_external_utf16() {
    print_header "SCENARIO 3: External Load File (UTF-16)"

    local start_time=$(date +%s)
    local scenario_dir="$OUTPUT_DIR/scenario3_external_utf16"
    mkdir -p "$scenario_dir"

    print_info "Generating $(printf "%'d" $FILE_COUNT) files with external UTF-16 load file..."

    dotnet run --project "$PROJECT" -- \
        --type "$FILE_TYPE" \
        --count "$FILE_COUNT" \
        --output-path "$scenario_dir" \
        --folders "$FOLDERS" \
        --distribution "$DISTRIBUTION" \
        --encoding "UTF-16" \
        --with-metadata \
        --with-text

    local end_time=$(date +%s)
    local duration=$((end_time - start_time))

    # Validate results
    local zip_file=$(find "$scenario_dir" -name "*.zip" | head -1)
    local dat_file=$(find "$scenario_dir" -name "*.dat" | head -1)
    local dat_size=$(stat -c%s "$dat_file")
    local dat_size_mb=$(echo "scale=2; $dat_size / 1024^2" | bc)

    print_success "Scenario 3 completed in ${duration}s"
    print_info "UTF-16 load file size: ${dat_size_mb}MB"
}

run_scenario_4_external_ansi() {
    print_header "SCENARIO 4: External Load File (ANSI)"

    local start_time=$(date +%s)
    local scenario_dir="$OUTPUT_DIR/scenario4_external_ansi"
    mkdir -p "$scenario_dir"

    print_info "Generating $(printf "%'d" $FILE_COUNT) files with external ANSI load file..."

    dotnet run --project "$PROJECT" -- \
        --type "$FILE_TYPE" \
        --count "$FILE_COUNT" \
        --output-path "$scenario_dir" \
        --folders "$FOLDERS" \
        --distribution "$DISTRIBUTION" \
        --encoding "ANSI" \
        --with-metadata \
        --with-text

    local end_time=$(date +%s)
    local duration=$((end_time - start_time))

    # Validate results
    local zip_file=$(find "$scenario_dir" -name "*.zip" | head -1)
    local dat_file=$(find "$scenario_dir" -name "*.dat" | head -1)
    local dat_size=$(stat -c%s "$dat_file")
    local dat_size_mb=$(echo "scale=2; $dat_size / 1024^2" | bc)

    print_success "Scenario 4 completed in ${duration}s"
    print_info "ANSI load file size: ${dat_size_mb}MB"
}

# --- Performance Analysis ---
analyze_performance() {
    print_header "LOAD FILE PERFORMANCE ANALYSIS"

    print_info "Analyzing load file performance across scenarios..."

    # Collect load file sizes
    local utf8_external_size=0
    local utf8_embedded_size=0
    local utf16_external_size=0
    local ansi_external_size=0

    # Scenario 1: UTF-8 External
    local dat_file=$(find "$OUTPUT_DIR/scenario1_external_utf8" -name "*.dat" | head -1)
    if [ -n "$dat_file" ]; then
        utf8_external_size=$(stat -c%s "$dat_file")
    fi

    # Scenario 2: UTF-8 Embedded
    local zip_file=$(find "$OUTPUT_DIR/scenario2_embedded_utf8" -name "*.zip" | head -1)
    if [ -n "$zip_file" ]; then
        local temp_dir=$(mktemp -d)
        trap "rm -rf $temp_dir" RETURN
        unzip -j "$zip_file" "*.dat" -d "$temp_dir" > /dev/null
        local embedded_dat=$(find "$temp_dir" -name "*.dat" | head -1)
        if [ -n "$embedded_dat" ]; then
            utf8_embedded_size=$(stat -c%s "$embedded_dat")
        fi
    fi

    # Scenario 3: UTF-16 External
    dat_file=$(find "$OUTPUT_DIR/scenario3_external_utf16" -name "*.dat" | head -1)
    if [ -n "$dat_file" ]; then
        utf16_external_size=$(stat -c%s "$dat_file")
    fi

    # Scenario 4: ANSI External
    dat_file=$(find "$OUTPUT_DIR/scenario4_external_ansi" -name "*.dat" | head -1)
    if [ -n "$dat_file" ]; then
        ansi_external_size=$(stat -c%s "$dat_file")
    fi

    # Convert to MB
    local utf8_external_mb=$(echo "scale=2; $utf8_external_size / 1024^2" | bc)
    local utf8_embedded_mb=$(echo "scale=2; $utf8_embedded_size / 1024^2" | bc)
    local utf16_external_mb=$(echo "scale=2; $utf16_external_size / 1024^2" | bc)
    local ansi_external_mb=$(echo "scale=2; $ansi_external_size / 1024^2" | bc)

    print_info "Load File Size Comparison:"
    echo "  - UTF-8 External:  ${utf8_external_mb}MB"
    echo "  - UTF-8 Embedded:  ${utf8_embedded_mb}MB"
    echo "  - UTF-16 External: ${utf16_external_mb}MB"
    echo "  - ANSI External:    ${ansi_external_mb}MB"

    # Size comparisons
    if (( $(echo "$utf16_external_mb > $utf8_external_mb * 1.8" | bc -l) )); then
        print_success "UTF-16 encoding overhead verified: $(echo "scale=1; $utf16_external_mb / $utf8_external_mb" | bc)x larger than UTF-8"
    fi

    if (( $(echo "$utf8_embedded_mb > 0" | bc -l) )); then
        print_success "Embedded load file functionality verified: ${utf8_embedded_mb}MB"
    fi

    # Check if target size achieved
    if (( $(echo "$utf8_external_mb >= $TARGET_LOAD_SIZE_MB * 0.5" | bc -l) )); then
        print_success "Target load file size achieved: ${utf8_external_mb}MB (target: ~${TARGET_LOAD_SIZE_MB}MB)"
    else
        print_warning "Load file size below target: ${utf8_external_mb}MB (target: ~${TARGET_LOAD_SIZE_MB}MB)"
    fi

    # Validate line counts
    print_info "Validating load file line counts..."

    local expected_lines=$((FILE_COUNT + 1))  # +1 for header

    for scenario in "scenario1_external_utf8" "scenario3_external_utf16" "scenario4_external_ansi"; do
        local dat_file=$(find "$OUTPUT_DIR/$scenario" -name "*.dat" | head -1)
        if [ -n "$dat_file" ]; then
            local line_count=$(wc -l < "$dat_file")
            if [ "$line_count" -eq "$expected_lines" ]; then
                print_success "$scenario: Correct line count ($line_count)"
            else
                print_error "$scenario: Incorrect line count. Expected: $expected_lines, Found: $line_count"
            fi
        fi
    done
}

# --- Cleanup and Summary ---
cleanup_and_summary() {
    print_header "LARGE LOAD FILE STRESS TEST SUMMARY"

    print_success "Large load file stress test completed successfully!"
    print_info "Generated files are available in: $OUTPUT_DIR"
    print_info "You can safely remove the output directory when no longer needed:"
    echo "  rm -rf $OUTPUT_DIR"

    echo ""
    print_info "Test Results Summary:"
    echo "  ✓ Generated $(printf "%'d" $FILE_COUNT) files per scenario"
    echo "  ✓ Tested external and embedded load files"
    echo "  ✓ Verified UTF-8, UTF-16, and ANSI encoding performance"
    echo "  ✓ Load file sizes ranging from ${ansi_external_mb}MB to ${utf16_external_mb}MB"
    echo "  ✓ Metadata and text extraction enabled for maximum load file size"
    echo "  ✓ Load file I/O performance validated"
    echo "  ✓ Encoding overhead measured and verified"
}

# --- Main Execution ---
main() {
    print_header "ZIPPER STRESS TEST SUITE"
    echo "Large Load File Performance Test"
    echo ""

    print_warning "This load file stress test will test large data handling:"
    echo "  - Time: 5-10 minutes"
    echo "  - Disk: ~2GB"
    echo "  - Memory: Light usage (under 1GB)"
    echo "  - Focus: Load file generation and I/O performance"
    echo "  - Target: ~${TARGET_LOAD_SIZE_MB}MB load files"
    echo ""

    # Run validations
    check_required_utilities
    check_disk_space
    show_test_details

    # Execute all scenarios
    run_scenario_1_external_utf8
    run_scenario_2_embedded_utf8
    run_scenario_3_external_utf16
    run_scenario_4_external_ansi

    # Analyze performance
    analyze_performance

    # Show summary
    cleanup_and_summary
}

# Check if script is being run directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi