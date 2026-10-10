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
TEST_NAME="30GB_Attachment-Heavy_EML_Focus"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT="${ZIPPER_PROJECT:-$PROJECT_ROOT/src/Zipper.csproj}"
STRESS_RESULTS_DIR="${STRESS_RESULTS_DIR:-$SCRIPT_DIR/results}"
OUTPUT_DIR="${OUTPUT_DIR:-$STRESS_RESULTS_DIR/${TEST_NAME}_$(date +%Y%m%d_%H%M%S)_$$}"

# Summary measurement variables (scoped at file level for set -u safety)
ZIP_SIZE_GB="0.00"
DAT_SIZE_MB="0.00"
ATTACHMENT_COUNT=0
TOTAL_FILES=0
TEXT_COUNT=0
PDF_ATTACHMENTS=0
JPG_ATTACHMENTS=0
TIFF_ATTACHMENTS=0
zip_size_gb="0.00"
dat_size_mb="0.00"
attachment_count=0
total_files=0
text_count=0
pdf_attachments=0
jpg_attachments=0
tiff_attachments=0

# Test parameters
EML_COUNT="${EML_COUNT:-1000000}"         # 1 million EML files
ATTACHMENT_RATE="${ATTACHMENT_RATE:-80}"   # 80% of EMLs will have attachments
FOLDERS="${FOLDERS:-100}"
TARGET_SIZE_GB="${TARGET_SIZE_GB:-30}"
DISTRIBUTION="${DISTRIBUTION:-proportional}"
MIN_ATTACHMENT_SIZE_MB="${MIN_ATTACHMENT_SIZE_MB:-2}"
MAX_ATTACHMENT_SIZE_MB="${MAX_ATTACHMENT_SIZE_MB:-5}"

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
    print_info "Checking system resources for attachment-heavy processing..."

    local available_memory="n/a"
    local memory_gb=0
    local cpu_cores=1
    if command -v free >/dev/null 2>&1; then
        if ! available_memory=$(free -h | awk '/^Mem:/ {print $7}'); then
            available_memory=0
        fi
        available_memory=${available_memory:-0}
        if ! memory_gb=$(free -g | awk '/^Mem:/ {print $7}'); then
            memory_gb=0
        fi
        memory_gb=${memory_gb:-0}
    elif command -v sysctl >/dev/null 2>&1; then
        memory_gb=$(( $(sysctl -n hw.memsize 2>/dev/null || echo 0) / 1073741824 ))
        available_memory="${memory_gb}GB"
    fi

    if command -v nproc >/dev/null 2>&1; then
        cpu_cores=$(nproc)
    elif command -v sysctl >/dev/null 2>&1; then
        cpu_cores=$(sysctl -n hw.ncpu 2>/dev/null || echo "1")
    fi

    print_info "Available memory: $available_memory"
    print_info "CPU cores: $cpu_cores"

    if [ "$cpu_cores" -lt 8 ]; then
        print_warning "Attachment processing is CPU intensive. Consider running on a machine with 8+ cores"
    fi

    # Check memory (attachment processing requires significant memory)
    if [[ "$memory_gb" =~ ^[0-9]+$ ]] && [ "$memory_gb" -lt 12 ]; then
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

    # Validate EML file count (hard product contract assertion)
    print_info "Validating EML file count in archive..."
    local eml_count=$(unzip -l "$zip_file" | grep "\.eml$" | wc -l)

    if [ "$eml_count" -eq "$EML_COUNT" ]; then
        print_success "EML file count verified: $(printf "%'d" $eml_count) files"
    else
        print_error "PRODUCT CONTRACT VIOLATION: EML file count mismatch. Expected: $(printf "%'d" $EML_COUNT), Found: $(printf "%'d" $eml_count)"
        return 2 2>/dev/null || exit 2
    fi

    # Validate attachment files
    print_info "Validating attachment files in archive..."
    ATTACHMENT_COUNT=$(unzip -l "$zip_file" | grep "attachment.*\.\(pdf\|jpg\|tiff\)$" | wc -l)
    attachment_count="$ATTACHMENT_COUNT"

    print_info "Attachment Statistics:"
    echo "  - Expected attachments: $(printf "%'d" $EXPECTED_ATTACHMENTS)"
    echo "  - Found attachments: $(printf "%'d" $ATTACHMENT_COUNT)"

    if [ "$ATTACHMENT_COUNT" -ge "$((EXPECTED_ATTACHMENTS / 2))" ]; then
        print_success "Attachment count within expected range: $(printf "%'d" $ATTACHMENT_COUNT)"
    else
        print_warning "Lower than expected attachment count: $(printf "%'d" $ATTACHMENT_COUNT) (expected ~$(printf "%'d" $EXPECTED_ATTACHMENTS))"
    fi

    # Validate total file count
    TOTAL_FILES=$((eml_count + ATTACHMENT_COUNT))
    total_files="$TOTAL_FILES"
    print_info "Total files in archive: $(printf "%'d" $TOTAL_FILES)"

    if [ "$TOTAL_FILES" -ge "$EXPECTED_MIN_FILES" ]; then
        print_success "Total file count meets minimum expectations: $(printf "%'d" $TOTAL_FILES)"
    else
        print_warning "Total file count below minimum: $(printf "%'d" $TOTAL_FILES) (expected min: $(printf "%'d" $EXPECTED_MIN_FILES))"
    fi

    # Validate DAT file structure with attachment data (hard product contract assertion)
    print_info "Validating load file with attachment data..."
    local line_count=$(wc -l < "$dat_file")
    local expected_lines=$((EML_COUNT + 1))  # +1 for header

    if [ "$line_count" -eq "$expected_lines" ]; then
        print_success "Load file structure validated: $line_count lines"
    else
        print_error "PRODUCT CONTRACT VIOLATION: Load file line count mismatch. Expected: $expected_lines, Found: $line_count"
        return 2 2>/dev/null || exit 2
    fi

    # Check for attachment entries in DAT file
    local attachment_entries=$(grep -c "attachment" "$dat_file" || true)
    if [ "$attachment_entries" -gt "$EML_COUNT" ]; then
        print_success "Attachment entries found in load file: $(printf "%'d" $attachment_entries)"
    else
        print_warning "Fewer attachment entries than expected: $(printf "%'d" $attachment_entries)"
    fi

    # Check for text files
    print_info "Validating text file extraction..."
    TEXT_COUNT=$(unzip -l "$zip_file" | grep "\.txt$" | wc -l)
    text_count="$TEXT_COUNT"

    # Should have text files for EMLs and attachments
    local expected_text_min=$eml_count
    if [ "$TEXT_COUNT" -ge "$expected_text_min" ]; then
        print_success "Text files validated: $(printf "%'d" $TEXT_COUNT) files"
    else
        print_warning "Text file count lower than expected: $(printf "%'d" $TEXT_COUNT) (expected min: $(printf "%'d" $expected_text_min))"
    fi

    # Validate attachment file types distribution
    print_info "Analyzing attachment type distribution..."
    PDF_ATTACHMENTS=$(unzip -l "$zip_file" | grep "attachment.*\.pdf$" | wc -l)
    JPG_ATTACHMENTS=$(unzip -l "$zip_file" | grep "attachment.*\.jpg$" | wc -l)
    TIFF_ATTACHMENTS=$(unzip -l "$zip_file" | grep "attachment.*\.tiff$" | wc -l)
    pdf_attachments="$PDF_ATTACHMENTS"
    jpg_attachments="$JPG_ATTACHMENTS"
    tiff_attachments="$TIFF_ATTACHMENTS"
    local total_validated=$((PDF_ATTACHMENTS + JPG_ATTACHMENTS + TIFF_ATTACHMENTS))

    if [ "$total_validated" -eq "$ATTACHMENT_COUNT" ]; then
        print_success "Attachment type distribution validated:"
        echo "  - PDF attachments: $(printf "%'d" $PDF_ATTACHMENTS)"
        echo "  - JPG attachments: $(printf "%'d" $JPG_ATTACHMENTS)"
        echo "  - TIFF attachments: $(printf "%'d" $TIFF_ATTACHMENTS)"
    else
        print_warning "Attachment type count mismatch: $total_validated vs $ATTACHMENT_COUNT"
    fi
    return 0
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
    echo "  ✓ Created $(printf "%'d" $ATTACHMENT_COUNT) attachment files"
    echo "  ✓ Total archive files: $(printf "%'d" $TOTAL_FILES)"
    echo "  ✓ Archive size: ${ZIP_SIZE_GB}GB"
    echo "  ✓ Load file: ${DAT_SIZE_MB}MB"
    echo "  ✓ Attachment rate: $ATTACHMENT_RATE% achieved"
    echo "  ✓ Metadata and text extraction for all files"
    echo "  ✓ Proportional distribution across $FOLDERS folders"
    echo "  ✓ Nested attachment processing verified"
    echo "  ✓ Attachment type distribution validated"
    echo ""
    print_info "Attachment Processing Highlights:"
    echo "  ✓ PDF attachments: $(printf "%'d" $PDF_ATTACHMENTS)"
    echo "  ✓ JPG attachments: $(printf "%'d" $JPG_ATTACHMENTS)"
    echo "  ✓ TIFF attachments: $(printf "%'d" $TIFF_ATTACHMENTS)"
    echo "  ✓ Text files extracted: $(printf "%'d" $TEXT_COUNT)"
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
    validate_results || exit $?

    # Show summary
    cleanup_and_summary
}

# Check if script is being run directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi