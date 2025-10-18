#!/bin/bash

# =============================================================================
# STRESS E2E TEST SUITE - MANUAL INVOCATION ONLY
# =============================================================================
#
# IMPORTANT NOTES:
# - This stress suite is NOT part of regular CI/CD or pre-commit hooks
# - Developer must manually invoke stress tests
# - Requires significant disk space (+20% overhead) and time (several hours)
# - Includes pre-run validation for available disk space
# - Each scenario tests unique failure modes not covered in regular E2E tests
#
# Usage: ./tests/run-stress-tests.sh [scenario]
#   scenario: "10gb", "20gb", "30gb", or "all" (default: "all")
#
# =============================================================================

set -e  # Exit on any error

# --- Configuration ---
PROJECT="Zipper/Zipper.csproj"
TEST_OUTPUT_DIR="./stress_test_output"
AVAILABLE_SPACE_GB=$(df -BG . | awk 'NR==2 {print $4}' | sed 's/G//')

# --- Color Output ---
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# --- Helper Functions ---
print_warning() {
    echo -e "${YELLOW}[ WARNING ]${NC} $1"
}

print_error() {
    echo -e "${RED}[ ERROR ]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[ SUCCESS ]${NC} $1"
}

print_info() {
    echo -e "${BLUE}[ INFO ]${NC} $1"
}

# --- Disk Space Validation ---
validate_disk_space() {
    local required_gb=$1
    local scenario_name=$2

    print_info "Validating disk space for $scenario_name scenario..."
    print_info "Available space: ${AVAILABLE_SPACE_GB}GB, Required: ${required_gb}GB"

    if [ "$AVAILABLE_SPACE_GB" -lt "$required_gb" ]; then
        print_error "Insufficient disk space for $scenario_name scenario"
        print_error "Available: ${AVAILABLE_SPACE_GB}GB, Required: ${required_gb}GB"
        print_error "Please free up disk space and try again"
        exit 1
    fi

    print_success "Disk space validation passed for $scenario_name scenario"
}

# --- Stress Test Scenarios ---

# 10GB Stress Test - Maximum File Count Challenge
# Generate 5 million PDF files with minimal individual size
# Tests absolute limits of file count vs size
run_10gb_stress_test() {
    print_info "Starting 10GB Stress Test - Maximum File Count Challenge"
    print_info "Scenario: 5 million PDF files with exponential distribution across 100 folders"
    print_info "Features: Metadata + Text extraction enabled"
    print_info "Target: Test maximum file count handling and Zip64 functionality"

    local output_dir="$TEST_OUTPUT_DIR/10gb_file_count_challenge"

    # Pre-run validation
    validate_disk_space 15 "10GB"

    # Create output directory
    mkdir -p "$output_dir"

    local start_time=$(date +%s)

    # Run the stress test
    print_info "Generating 5,000,000 PDF files..."
    dotnet run --project "$PROJECT" -- \
        --type pdf \
        --count 5000000 \
        --output-path "$output_dir" \
        --folders 100 \
        --distribution exponential \
        --with-metadata \
        --with-text

    local end_time=$(date +%s)
    local duration=$((end_time - start_time))

    # Verify output
    print_info "Verifying stress test output..."
    local zip_file=$(find "$output_dir" -name "*.zip" | head -1)
    local dat_file=$(find "$output_dir" -name "*.dat" | head -1)

    if [ -z "$zip_file" ] || [ -z "$dat_file" ]; then
        print_error "Output files not found"
        exit 1
    fi

    # Check file counts
    local dat_lines=$(wc -l < "$dat_file")
    local expected_lines=5000001  # 5M files + 1 header

    if [ "$dat_lines" -eq "$expected_lines" ]; then
        print_success "File count verification passed: $((dat_lines - 1)) files"
    else
        print_error "File count verification failed: expected $((expected_lines - 1)), got $((dat_lines - 1))"
        exit 1
    fi

    # Check zip file size
    local zip_size_bytes=$(stat -c%s "$zip_file")
    local zip_size_gb=$((zip_size_bytes / 1024 / 1024 / 1024))

    print_success "10GB Stress Test completed successfully!"
    print_info "Duration: ${duration} seconds ($((duration / 60)) minutes)"
    print_info "Archive size: ${zip_size_gb}GB"
    print_info "Throughput: $((5000000 / duration)) files/second"
}

# 20GB Stress Test - Multi-Format Complexity
# Generate mix of PDF (60%), JPG (30%), EML (10%) file types
# Tests complex mixed-type generation at scale
run_20gb_stress_test() {
    print_info "Starting 20GB Stress Test - Multi-Format Complexity"
    print_info "Scenario: Mixed file types - PDF (60%), JPG (30%), EML (10%)"
    print_info "Distribution: Gaussian across 500 folders"
    print_info "Features: All options enabled - metadata, text, attachments (50% for EML)"
    print_info "Encoding: UTF-16 for increased complexity"
    print_info "Target: Test mixed format processing and memory management"

    local output_dir="$TEST_OUTPUT_DIR/20gb_multi_format"

    # Pre-run validation
    validate_disk_space 25 "20GB"

    # Create output directory
    mkdir -p "$output_dir"

    local start_time=$(date +%s)

    # Run PDF portion (60% = 1.2M files)
    print_info "Generating 1,200,000 PDF files..."
    dotnet run --project "$PROJECT" -- \
        --type pdf \
        --count 1200000 \
        --output-path "$output_dir/pdf_portion" \
        --folders 200 \
        --distribution gaussian \
        --with-metadata \
        --with-text \
        --encoding UTF-16

    # Run JPG portion (30% = 600K files)
    print_info "Generating 600,000 JPG files..."
    dotnet run --project "$PROJECT" -- \
        --type jpg \
        --count 600000 \
        --output-path "$output_dir/jpg_portion" \
        --folders 150 \
        --distribution gaussian \
        --with-metadata \
        --with-text \
        --encoding UTF-16

    # Run EML portion (10% = 200K files)
    print_info "Generating 200,000 EML files with 50% attachment rate..."
    dotnet run --project "$PROJECT" -- \
        --type eml \
        --count 200000 \
        --output-path "$output_dir/eml_portion" \
        --folders 150 \
        --distribution gaussian \
        --with-metadata \
        --with-text \
        --attachment-rate 50 \
        --encoding UTF-16

    local end_time=$(date +%s)
    local duration=$((end_time - start_time))

    # Calculate total size
    local total_size=0
    for portion in pdf_portion jpg_portion eml_portion; do
        local zip_file=$(find "$output_dir/$portion" -name "*.zip" | head -1)
        if [ -n "$zip_file" ]; then
            local size=$(stat -c%s "$zip_file")
            total_size=$((total_size + size))
        fi
    done

    local total_size_gb=$((total_size / 1024 / 1024 / 1024))
    local total_files=2000000  # 1.2M + 600K + 200K

    print_success "20GB Stress Test completed successfully!"
    print_info "Duration: ${duration} seconds ($((duration / 60)) minutes)"
    print_info "Total archive size: ${total_size_gb}GB"
    print_info "Total files: $total_files"
    print_info "Throughput: $((total_files / duration)) files/second"
}

# 30GB Stress Test - Attachment-Heavy EML Focus
# Generate 1 million EML files with 80% attachment rate
# Tests attachment-heavy generation with nested content
run_30gb_stress_test() {
    print_info "Starting 30GB Stress Test - Attachment-Heavy EML Focus"
    print_info "Scenario: 1 million EML files with 80% attachment rate"
    print_info "Attachments: Varied PDF/JPG/TIFF files (2-5MB each)"
    print_info "Distribution: Proportional across 1000 folders"
    print_info "Features: Metadata + Text extraction for all files and attachments"
    print_info "Target: Test attachment handling, nested file processing, and archive size limits"

    local output_dir="$TEST_OUTPUT_DIR/30gb_attachment_heavy"

    # Pre-run validation
    validate_disk_space 40 "30GB"

    # Create output directory
    mkdir -p "$output_dir"

    local start_time=$(date +%s)

    # Run the stress test
    print_info "Generating 1,000,000 EML files with 80% attachment rate..."
    dotnet run --project "$PROJECT" -- \
        --type eml \
        --count 1000000 \
        --output-path "$output_dir" \
        --folders 1000 \
        --distribution proportional \
        --with-metadata \
        --with-text \
        --attachment-rate 80

    local end_time=$(date +%s)
    local duration=$((end_time - start_time))

    # Verify output
    print_info "Verifying stress test output..."
    local zip_file=$(find "$output_dir" -name "*.zip" | head -1)
    local dat_file=$(find "$output_dir" -name "*.dat" | head -1)

    if [ -z "$zip_file" ] || [ -z "$dat_file" ]; then
        print_error "Output files not found"
        exit 1
    fi

    # Check file counts
    local dat_lines=$(wc -l < "$dat_file")
    local expected_lines=1000001  # 1M files + 1 header

    if [ "$dat_lines" -eq "$expected_lines" ]; then
        print_success "File count verification passed: $((dat_lines - 1)) files"
    else
        print_error "File count verification failed: expected $((expected_lines - 1)), got $((dat_lines - 1))"
        exit 1
    fi

    # Check attachment rate (simple verification - look for "Attachment" column)
    if grep -q "Attachment" "$dat_file"; then
        local attachment_count=$(grep -c ",1," "$dat_file" || echo "0")
        local attachment_rate=$((attachment_count * 100 / 1000000))
        print_info "Attachment rate detected: ${attachment_rate}% (approximately)"
    fi

    # Check zip file size
    local zip_size_bytes=$(stat -c%s "$zip_file")
    local zip_size_gb=$((zip_size_bytes / 1024 / 1024 / 1024))

    print_success "30GB Stress Test completed successfully!"
    print_info "Duration: ${duration} seconds ($((duration / 3600)) hours)"
    print_info "Archive size: ${zip_size_gb}GB"
    print_info "Throughput: $((1000000 / duration)) files/second"
}

# --- Main Execution ---

main() {
    local scenario="${1:-all}"

    echo "=============================================================================="
    echo "                    STRESS E2E TEST SUITE"
    echo "=============================================================================="
    print_warning "This stress suite is for manual invocation only"
    print_warning "Requires significant disk space and time (several hours)"
    print_warning "Each scenario tests unique failure modes not covered in regular E2E tests"
    echo "=============================================================================="
    echo

    # Check if project exists
    if [ ! -f "$PROJECT" ]; then
        print_error "Project file not found: $PROJECT"
        exit 1
    fi

    # Create main output directory
    mkdir -p "$TEST_OUTPUT_DIR"

    case "$scenario" in
        "10gb")
            run_10gb_stress_test
            ;;
        "20gb")
            run_20gb_stress_test
            ;;
        "30gb")
            run_30gb_stress_test
            ;;
        "all")
            run_10gb_stress_test
            echo
            run_20gb_stress_test
            echo
            run_30gb_stress_test
            ;;
        *)
            print_error "Invalid scenario: $scenario"
            echo "Usage: $0 [10gb|20gb|30gb|all]"
            exit 1
            ;;
    esac

    echo
    print_success "All stress test scenarios completed successfully!"
    print_info "Output files are available in: $TEST_OUTPUT_DIR"
    print_warning "Remember to clean up the test output directory when done"
    echo "=============================================================================="
}

# Run main function with all arguments
main "$@"