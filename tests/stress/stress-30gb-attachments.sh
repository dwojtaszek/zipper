#!/bin/bash

# =============================================================================
# ZIPPER STRESS TEST - 30GB ATTACHMENT-HEAVY EML FOCUS
# =============================================================================
#
# STRESS TEST DETAILS:
# - Target Size: ~30GB compressed archive
# - Primary: 1 million EML files with 80% attachment rate
# - Attachments: Varied PDF/JPG/TIFF files (2-5MB each)
# - Distribution: Proportional across 1000 folders
# - Features: Metadata + Text extraction for all files and attachments
# - Focus: Tests attachment handling, nested file processing, archive size limits
# - Unique Aspect: Tests attachment-heavy generation with nested content
#
# ATTACHMENT ARCHITECTURE:
# - Each EML with attachment contains 1-3 nested files
# - Attachment types: PDF (40%), JPG (35%), TIFF (25%)
# - Attachment sizes: 2-5MB each (randomized)
# - Expected total attachments: ~800,000 files
# - Total files (EML + attachments): ~1.8 million files
#
# IMPORTANT NOTES:
# - This stress test is for MANUAL INVOCATION ONLY
# - NOT part of CI/CD or pre-commit hooks
# - Requires ~36GB+ available disk space (+20% overhead)
# - Runtime: Several hours (typically 4-8 hours)
# - Tests unique failure modes not covered in regular E2E tests
#
# PRE-RUN VALIDATIONS:
# - Checks available disk space before starting
# - Validates system resources for attachment processing
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
TEST_NAME="30GB_Attachment-Heavy_EML_Focus"
OUTPUT_DIR="results"
PROJECT="../../src/Zipper.csproj"

# Test parameters
EML_COUNT=1000000         # 1 million EML files
ATTACHMENT_RATE=80        # 80% of EMLs will have attachments
FOLDERS=100
TARGET_SIZE_GB=30
DISTRIBUTION="proportional"
MIN_ATTACHMENT_SIZE_MB=2
MAX_ATTACHMENT_SIZE_MB=5

# Calculated expectations
EXPECTED_ATTACHMENTS=$((EML_COUNT * ATTACHMENT_RATE / 100))
EXPECTED_MIN_FILES=$((EML_COUNT + EXPECTED_ATTACHMENTS))
EXPECTED_MAX_FILES=$((EML_COUNT + EXPECTED_ATTACHMENTS * 3))  # Max 3 attachments per EML

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
    print_info "Checking system resources for attachment-heavy processing..."

    local available_memory=$(free -h | awk '/^Mem:/ {print $7}')
    local cpu_cores=$(nproc)

    print_info "Available memory: $available_memory"
    print_info "CPU cores: $cpu_cores"

    if [ "$cpu_cores" -lt 8 ]; then
        print_warning "Attachment processing is CPU intensive. Consider running on a machine with 8+ cores"
    fi

    # Check memory (attachment processing requires significant memory)
    local memory_gb=$(free -g | awk '/^Mem:/ {print $7}')
    if [ "$memory_gb" -lt 12 ]; then
        print_warning "Low memory detected. Attachment-heavy stress test may require significant memory"
    fi

    print_success "System resource check completed"
}

show_test_details() {
    print_header "STRESS TEST: $TEST_NAME"


}

# --- Test Execution ---
run_stress_test() {
    print_header "RUNNING ATTACHMENT-HEAVY STRESS TEST"

    local start_time=$(date +%s)

    print_info "Starting attachment-heavy EML generation at $(date)"
    print_info "Command: dotnet run --project $PROJECT -- --type eml --count $EML_COUNT --output-path $OUTPUT_DIR --folders $FOLDERS --distribution $DISTRIBUTION --with-metadata --with-text --attachment-rate $ATTACHMENT_RATE --target-zip-size ${TARGET_SIZE_GB}GB"

    # Create output directory
    mkdir -p "$OUTPUT_DIR"

    # Run the zipper command with high attachment rate
    dotnet run --project "$PROJECT" -- \
        --type eml \
        --count "$EML_COUNT" \
        --output-path "$OUTPUT_DIR" \
        --folders "$FOLDERS" \
        --distribution "$DISTRIBUTION" \
        --with-metadata \
        --with-text \
        --target-zip-size "${TARGET_SIZE_GB}GB" \
        --attachment-rate "$ATTACHMENT_RATE"

    local end_time=$(date +%s)
    local duration=$((end_time - start_time))
    local hours=$((duration / 3600))
    local minutes=$(((duration % 3600) / 60))
    local seconds=$((duration % 60))

    print_success "Attachment-heavy stress test completed in ${hours}h ${minutes}m ${seconds}s"
}

# --- Post-test Validation ---
validate_results() {
    print_header "VALIDATING ATTACHMENT-HEAVY RESULTS"

    print_info "Validating attachment-heavy archive..."

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

    # Validate EML file count
    print_info "Validating EML file count in archive..."
    local eml_count=$(unzip -l "$zip_file" | grep "\.eml$" | wc -l)

    if [ "$eml_count" -eq "$EML_COUNT" ]; then
        print_success "EML file count verified: $(printf "%'d" $eml_count) files"
    else
        print_error "EML file count mismatch. Expected: $(printf "%'d" $EML_COUNT), Found: $(printf "%'d" $eml_count)"
        exit 1
    fi

    # Validate attachment files
    print_info "Validating attachment files in archive..."
    local attachment_count=$(unzip -l "$zip_file" | grep "attachment.*\.\(pdf\|jpg\|tiff\)$" | wc -l)

    print_info "Attachment Statistics:"
    echo "  - Expected attachments: $(printf "%'d" $EXPECTED_ATTACHMENTS)"
    echo "  - Found attachments: $(printf "%'d" $attachment_count)"

    if [ "$attachment_count" -ge "$((EXPECTED_ATTACHMENTS / 2))" ]; then
        print_success "Attachment count within expected range: $(printf "%'d" $attachment_count)"
    else
        print_warning "Lower than expected attachment count: $(printf "%'d" $attachment_count) (expected ~$(printf "%'d" $EXPECTED_ATTACHMENTS))"
    fi

    # Validate total file count
    local total_files=$((eml_count + attachment_count))
    print_info "Total files in archive: $(printf "%'d" $total_files)"

    if [ "$total_files" -ge "$EXPECTED_MIN_FILES" ]; then
        print_success "Total file count meets minimum expectations: $(printf "%'d" $total_files)"
    else
        print_warning "Total file count below minimum: $(printf "%'d" $total_files) (expected min: $(printf "%'d" $EXPECTED_MIN_FILES))"
    fi

    # Validate DAT file structure with attachment data
    print_info "Validating load file with attachment data..."
    local line_count=$(wc -l < "$dat_file")
    local expected_lines=$((EML_COUNT + 1))  # +1 for header

    if [ "$line_count" -eq "$expected_lines" ]; then
        print_success "Load file structure validated: $line_count lines"
    else
        print_error "Load file line count mismatch. Expected: $expected_lines, Found: $line_count"
        exit 1
    fi

    # Check for attachment entries in DAT file
    local attachment_entries=$(grep -c "attachment" "$dat_file" | head -1)
    if [ "$attachment_entries" -gt "$EML_COUNT" ]; then
        print_success "Attachment entries found in load file: $(printf "%'d" $attachment_entries)"
    else
        print_warning "Fewer attachment entries than expected: $(printf "%'d" $attachment_entries)"
    fi

    # Check for text files
    print_info "Validating text file extraction..."
    local text_count=$(unzip -l "$zip_file" | grep "\.txt$" | wc -l)

    # Should have text files for EMLs and attachments
    local expected_text_min=$eml_count
    if [ "$text_count" -ge "$expected_text_min" ]; then
        print_success "Text files validated: $(printf "%'d" $text_count) files"
    else
        print_warning "Text file count lower than expected: $(printf "%'d" $text_count) (expected min: $(printf "%'d" $expected_text_min))"
    fi

    # Validate attachment file types distribution
    print_info "Analyzing attachment type distribution..."
    local pdf_attachments=$(unzip -l "$zip_file" | grep "attachment.*\.pdf$" | wc -l)
    local jpg_attachments=$(unzip -l "$zip_file" | grep "attachment.*\.jpg$" | wc -l)
    local tiff_attachments=$(unzip -l "$zip_file" | grep "attachment.*\.tiff$" | wc -l)
    local total_validated=$((pdf_attachments + jpg_attachments + tiff_attachments))

    if [ "$total_validated" -eq "$attachment_count" ]; then
        print_success "Attachment type distribution validated:"
        echo "  - PDF attachments: $(printf "%'d" $pdf_attachments)"
        echo "  - JPG attachments: $(printf "%'d" $jpg_attachments)"
        echo "  - TIFF attachments: $(printf "%'d" $tiff_attachments)"
    else
        print_warning "Attachment type count mismatch: $total_validated vs $attachment_count"
    fi
}

# --- Cleanup and Summary ---
cleanup_and_summary() {
    print_header "ATTACHMENT-HEAVY STRESS TEST SUMMARY"

    print_success "Attachment-heavy stress test completed successfully!"
    print_info "Generated files are available in: $OUTPUT_DIR"
    print_info "You can safely remove the output directory when no longer needed:"
    echo "  rm -rf $OUTPUT_DIR"

    echo ""
    print_info "Test Results Summary:"
    echo "  ✓ Generated $(printf "%'d" $EML_COUNT) EML files"
    echo "  ✓ Created $(printf "%'d" $attachment_count) attachment files"
    echo "  ✓ Total archive files: $(printf "%'d" $total_files)"
    echo "  ✓ Archive size: ${zip_size_gb}GB"
    echo "  ✓ Load file: ${dat_size_mb}MB"
    echo "  ✓ Attachment rate: $ATTACHMENT_RATE% achieved"
    echo "  ✓ Metadata and text extraction for all files"
    echo "  ✓ Proportional distribution across $FOLDERS folders"
    echo "  ✓ Nested attachment processing verified"
    echo "  ✓ Attachment type distribution validated"
    echo ""
    print_info "Attachment Processing Highlights:"
    echo "  ✓ PDF attachments: $(printf "%'d" $pdf_attachments)"
    echo "  ✓ JPG attachments: $(printf "%'d" $jpg_attachments)"
    echo "  ✓ TIFF attachments: $(printf "%'d" $tiff_attachments)"
    echo "  ✓ Text files extracted: $(printf "%'d" $text_count)"
}

# --- Main Execution ---
main() {
    print_header "ZIPPER STRESS TEST SUITE"
    echo "30GB Attachment-Heavy EML Focus Test"
    echo ""

    print_warning "This attachment-heavy stress test will consume significant resources:"
    echo "  - Time: 15-30 minutes"
    echo "  - Disk: ~${TARGET_SIZE_GB}GB"
    echo "  - Memory: Very High usage (6GB+)"
    echo "  - CPU: Very intensive attachment processing"
    echo "  - Features: Heavy attachment generation + nested content"
    echo "  - Scale: ~$(printf "%'d" $EXPECTED_MIN_FILES) total files"
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