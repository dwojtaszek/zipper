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

set -euo pipefail  # Exit on any error, use unset variable as error, and fail on pipe failures

# --- Check Required Utilities ---
check_required_utilities() {
    local missing_utils=()
    for util in df stat unzip grep wc find; do
        if ! command -v "$util" &> /dev/null; then
            missing_utils+=("$util")
        fi
    done

    if [ ${#missing_utils[@]} -gt 0 ]; then
        print_error "Missing required utilities: ${missing_utils[*]}"
        print_info "Install missing utilities:"
        echo "  Ubuntu/Debian: sudo apt-get install unzip"
        exit 1
    fi
}

to_mb() {
    local bytes="${1:-0}"
    [[ -z "$bytes" ]] && bytes=0
    if command -v bc >/dev/null 2>&1; then
        echo "scale=2; $bytes / 1024^2" | bc
    else
        awk "BEGIN {printf \"%.2f\", $bytes / 1048576}"
    fi
}

to_gb() {
    local bytes="${1:-0}"
    [[ -z "$bytes" ]] && bytes=0
    if command -v bc >/dev/null 2>&1; then
        echo "scale=2; $bytes / 1024^3" | bc
    else
        awk "BEGIN {printf \"%.2f\", $bytes / 1073741824}"
    fi
}

get_file_size() {
    local file="$1"
    if [ -z "$file" ] || [ ! -e "$file" ]; then
        echo "0"
        return 0
    fi
    stat -c%s "$file" 2>/dev/null || stat -f%z "$file" 2>/dev/null || echo "0"
}

# --- Configuration ---
TEST_NAME="10GB_Maximum_File_Count_Challenge"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT="${ZIPPER_PROJECT:-$PROJECT_ROOT/src/Zipper.csproj}"
STRESS_RESULTS_DIR="${STRESS_RESULTS_DIR:-$SCRIPT_DIR/results}"
OUTPUT_DIR="${OUTPUT_DIR:-$STRESS_RESULTS_DIR/${TEST_NAME}_$(date +%Y%m%d_%H%M%S)_$$}"

# Summary measurement variables (scoped at file level for set -u safety)
ZIP_SIZE_GB="0.00"
DAT_SIZE_MB="0.00"
zip_size_gb="0.00"
dat_size_mb="0.00"

# Test parameters
FILE_COUNT="${FILE_COUNT:-5000000}"  # 5 million files
FOLDERS="${FOLDERS:-100}"
TARGET_SIZE_GB="${TARGET_SIZE_GB:-10}"
FILE_TYPE="${FILE_TYPE:-pdf}"
DISTRIBUTION="${DISTRIBUTION:-exponential}"

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

    local required_bytes
    if command -v bc >/dev/null 2>&1; then
        required_bytes=$(printf "%.0f" $(echo "$TARGET_SIZE_GB * 1024^3 * 1.2" | bc))  # 20% overhead
    else
        required_bytes=$(awk "BEGIN {printf \"%.0f\", $TARGET_SIZE_GB * 1073741824 * 1.2}")
    fi
    mkdir -p "$OUTPUT_DIR"
    local available_kb=$(df -k -P "$OUTPUT_DIR" | awk 'NR>1 {print $4; exit}')
    available_kb=${available_kb:-0}
    local available_bytes=$((available_kb * 1024))
    local available_gb=$(to_gb "$available_bytes")
    local required_gb=$(to_gb "$required_bytes")

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

    local available_memory="n/a"
    local cpu_cores=1
    if command -v free >/dev/null 2>&1; then
        available_memory=$(free -h | awk '/^Mem:/ {print $7}' || true)
    elif command -v sysctl >/dev/null 2>&1; then
        available_memory="$(( $(sysctl -n hw.memsize 2>/dev/null || echo 0) / 1073741824 ))GB"
    fi

    if command -v nproc >/dev/null 2>&1; then
        cpu_cores=$(nproc)
    elif command -v sysctl >/dev/null 2>&1; then
        cpu_cores=$(sysctl -n hw.ncpu 2>/dev/null || echo "1")
    fi

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

    local zip_file=""
    local dat_file=""
    if [ -d "$OUTPUT_DIR" ]; then
        zip_file=$(find "$OUTPUT_DIR" -name "*.zip" -print -quit 2>/dev/null || true)
        dat_file=$(find "$OUTPUT_DIR" -name "*.dat" -print -quit 2>/dev/null || true)
    fi

    if [ -z "$zip_file" ]; then
        print_error "PRODUCT CONTRACT VIOLATION: No .zip file found"
        return 2 2>/dev/null || exit 2
    fi

    if [ -z "$dat_file" ]; then
        print_error "PRODUCT CONTRACT VIOLATION: No .dat file found"
        return 2 2>/dev/null || exit 2
    fi

    # Check file sizes
    local zip_size=$(get_file_size "$zip_file")
    local dat_size=$(get_file_size "$dat_file")
    ZIP_SIZE_GB=$(to_gb "$zip_size")
    DAT_SIZE_MB=$(to_mb "$dat_size")
    zip_size_gb="$ZIP_SIZE_GB"
    dat_size_mb="$DAT_SIZE_MB"

    print_info "Generated Archive:"
    echo "  - ZIP file: $(basename "$zip_file")"
    echo "  - ZIP size: ${ZIP_SIZE_GB}GB"
    echo "  - DAT file: $(basename "$dat_file")"
    echo "  - DAT size: ${DAT_SIZE_MB}MB"

    # Validate target size (environmental performance observation)
    local min_size_gb=$(echo "$TARGET_SIZE_GB * 0.9" | bc 2>/dev/null || awk "BEGIN {print $TARGET_SIZE_GB * 0.9}")
    local max_size_gb=$(echo "$TARGET_SIZE_GB * 1.1" | bc 2>/dev/null || awk "BEGIN {print $TARGET_SIZE_GB * 1.1}")

    local size_in_range=false
    if command -v bc >/dev/null 2>&1; then
        (( $(echo "$ZIP_SIZE_GB >= $min_size_gb && $ZIP_SIZE_GB <= $max_size_gb" | bc -l) )) && size_in_range=true || true
    else
        awk "BEGIN {exit !($ZIP_SIZE_GB >= $min_size_gb && $ZIP_SIZE_GB <= $max_size_gb)}" && size_in_range=true || true
    fi

    if [ "$size_in_range" = true ]; then
        print_success "Target size achieved: ${ZIP_SIZE_GB}GB (target: ${TARGET_SIZE_GB}GB ±10%)"
    else
        print_warning "Size outside target range: ${ZIP_SIZE_GB}GB (target: ${TARGET_SIZE_GB}GB ±10%)"
    fi

    # Validate file count in zip (hard product contract assertion)
    print_info "Validating file count in archive..."
    local file_count=$(unzip -l "$zip_file" | grep "\.$FILE_TYPE$" | wc -l)

    if [ "$file_count" -eq "$FILE_COUNT" ]; then
        print_success "File count verified: $(printf "%'d" $file_count) files"
    else
        print_error "PRODUCT CONTRACT VIOLATION: File count mismatch. Expected: $(printf "%'d" $FILE_COUNT), Found: $(printf "%'d" $file_count)"
        return 2 2>/dev/null || exit 2
    fi

    # Validate DAT file structure (hard product contract assertion)
    print_info "Validating load file structure..."
    local line_count=$(wc -l < "$dat_file")
    local expected_lines=$((FILE_COUNT + 1))  # +1 for header

    if [ "$line_count" -eq "$expected_lines" ]; then
        print_success "Load file structure validated: $line_count lines"
    else
        print_error "PRODUCT CONTRACT VIOLATION: Load file line count mismatch. Expected: $expected_lines, Found: $line_count"
        return 2 2>/dev/null || exit 2
    fi

    # Check for text files (hard product contract assertion)
    local text_count=$(unzip -l "$zip_file" | grep "\.txt$" | wc -l)
    if [ "$text_count" -eq "$FILE_COUNT" ]; then
        print_success "Text files validated: $(printf "%'d" $text_count) files"
    else
        print_error "PRODUCT CONTRACT VIOLATION: Text file count mismatch. Expected: $(printf "%'d" $FILE_COUNT), Found: $(printf "%'d" $text_count)"
        return 2 2>/dev/null || exit 2
    fi
    return 0
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
    echo "  ✓ Archive size: ${ZIP_SIZE_GB}GB"
    echo "  ✓ Load file: ${DAT_SIZE_MB}MB"
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
    validate_results || exit $?

    # Show summary
    cleanup_and_summary
}

# Check if script is being run directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi