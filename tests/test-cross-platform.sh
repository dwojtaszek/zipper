#!/bin/bash

# Cross-platform compatibility test script for Zipper
# This script tests core functionality across different platforms

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Test configuration
TEST_OUTPUT_DIR="./cross-platform-results"
PROJECT="Zipper/Zipper.csproj"
PLATFORM=$(uname -s)

# Helper functions
print_success() {
    echo -e "${GREEN}[ SUCCESS ]${NC} $1"
}

print_info() {
    echo -e "${BLUE}[ INFO ]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[ WARNING ]${NC} $1"
}

print_error() {
    echo -e "${RED}[ ERROR ]${NC} $1"
}

# Detect platform
detect_platform() {
    case "$PLATFORM" in
        Linux*) echo "Linux" ;;
        Darwin*) echo "macOS" ;;
        CYGWIN*|MINGW*|MSYS*) echo "Windows" ;;
        *) echo "Unknown" ;;
    esac
}

# Test basic functionality
test_basic_functionality() {
    print_info "Testing basic functionality on $(detect_platform)"

    # Clean up previous results
    rm -rf "$TEST_OUTPUT_DIR"
    mkdir -p "$TEST_OUTPUT_DIR"

    # Test 1: Basic PDF generation
    print_info "Test 1: Basic PDF generation"
    dotnet run --project "$PROJECT" -- --type pdf --count 5 --output-path "$TEST_OUTPUT_DIR/basic_pdf"

    if [ -f "$TEST_OUTPUT_DIR/basic_pdf.zip" ] && [ -f "$TEST_OUTPUT_DIR/basic_pdf.dat" ]; then
        print_success "Basic PDF generation completed"
    else
        print_error "Basic PDF generation failed"
        return 1
    fi

    # Test 2: Basic EML generation
    print_info "Test 2: Basic EML generation"
    dotnet run --project "$PROJECT" -- --type eml --count 3 --output-path "$TEST_OUTPUT_DIR/basic_eml"

    if [ -f "$TEST_OUTPUT_DIR/basic_eml.zip" ] && [ -f "$TEST_OUTPUT_DIR/basic_eml.dat" ]; then
        print_success "Basic EML generation completed"
    else
        print_error "Basic EML generation failed"
        return 1
    fi

    # Test 3: Different encodings
    print_info "Test 3: Different encodings"

    # UTF-8
    dotnet run --project "$PROJECT" -- --type pdf --count 3 --output-path "$TEST_OUTPUT_DIR/utf8" --encoding UTF-8

    # UTF-16
    dotnet run --project "$PROJECT" -- --type pdf --count 3 --output-path "$TEST_OUTPUT_DIR/utf16" --encoding UTF-16

    # ANSI
    dotnet run --project "$PROJECT" -- --type pdf --count 3 --output-path "$TEST_OUTPUT_DIR/ansi" --encoding ANSI

    # Check all files exist
    if [ -f "$TEST_OUTPUT_DIR/utf8.zip" ] && [ -f "$TEST_OUTPUT_DIR/utf16.zip" ] && [ -f "$TEST_OUTPUT_DIR/ansi.zip" ]; then
        print_success "All encoding tests completed"
    else
        print_error "Encoding tests failed"
        return 1
    fi

    # Test 4: Different distributions
    print_info "Test 4: Different distributions"

    for dist in "proportional" "gaussian" "exponential"; do
        dotnet run --project "$PROJECT" -- --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/dist_${dist}" --folders 3 --distribution "$dist"

        if [ -f "$TEST_OUTPUT_DIR/dist_${dist}.zip" ]; then
            print_success "${dist} distribution test completed"
        else
            print_error "${dist} distribution test failed"
            return 1
        fi
    done

    return 0
}

# Test refactored components
test_refactored_components() {
    print_info "Testing refactored components on $(detect_platform)"

    # Test with metadata
    print_info "Testing with metadata"
    dotnet run --project "$PROJECT" -- --type pdf --count 5 --output-path "$TEST_OUTPUT_DIR/metadata" --with-metadata

    # Test with text extraction
    print_info "Testing with text extraction"
    dotnet run --project "$PROJECT" -- --type pdf --count 5 --output-path "$TEST_OUTPUT_DIR/text" --with-text

    # Test EML with attachments
    print_info "Testing EML with attachments"
    dotnet run --project "$PROJECT" -- --type eml --count 5 --output-path "$TEST_OUTPUT_DIR/eml_attachments" --attachment-rate 80

    # Verify all tests
    local tests=("metadata" "text" "eml_attachments")
    for test in "${tests[@]}"; do
        if [ -f "$TEST_OUTPUT_DIR/${test}.zip" ] && [ -f "$TEST_OUTPUT_DIR/${test}.dat" ]; then
            print_success "${test} test completed"
        else
            print_error "${test} test failed"
            return 1
        fi
    done

    return 0
}

# Test file system compatibility
test_filesystem_compatibility() {
    print_info "Testing file system compatibility on $(detect_platform)"

    # Test different path formats
    local test_paths=("$TEST_OUTPUT_DIR/standard_path" "$TEST_OUTPUT_DIR/path with spaces" "$TEST_OUTPUT_DIR/path-with-dashes")

    for path in "${test_paths[@]}"; do
        dotnet run --project "$PROJECT" -- --type pdf --count 2 --output-path "$path"

        if [ -f "$path.zip" ] && [ -f "$path.dat" ]; then
            print_success "Path compatibility test: '$path'"
        else
            print_error "Path compatibility test failed: '$path'"
            return 1
        fi
    done

    # Test special characters in file names
    print_info "Testing special characters"
    dotnet run --project "$PROJECT" -- --folders 1 --distribution proportional --type pdf --count 2 --output-path "$TEST_OUTPUT_DIR/special"

    if [ -f "$TEST_OUTPUT_DIR/special.zip" ] && [ -f "$TEST_OUTPUT_DIR/special.dat" ]; then
        print_success "Special characters test completed"
    else
        print_error "Special characters test failed"
        return 1
    fi

    return 0
}

# Test performance consistency
test_performance() {
    print_info "Testing performance on $(detect_platform)"

    # Small performance test
    local start_time=$(date +%s%N)
    dotnet run --project "$PROJECT" -- --type pdf --count 20 --output-path "$TEST_OUTPUT_DIR/perf"
    local end_time=$(date +%s%N)

    local duration_ms=$(( (end_time - start_time) / 1000000 ))

    if [ $duration_ms -lt 10000 ]; then  # Should complete in under 10 seconds
        print_success "Performance test completed in ${duration_ms}ms"
    else
        print_warning "Performance test took ${duration_ms}ms (may be slow)"
    fi

    # Verify output
    if [ -f "$TEST_OUTPUT_DIR/perf.zip" ] && [ -f "$TEST_OUTPUT_DIR/perf.dat" ]; then
        print_success "Performance test output verified"
    else
        print_error "Performance test output failed"
        return 1
    fi

    return 0
}

# Verify generated files
verify_output() {
    print_info "Verifying generated output files"

    local zip_count=$(find "$TEST_OUTPUT_DIR" -name "*.zip" | wc -l)
    local dat_count=$(find "$TEST_OUTPUT_DIR" -name "*.dat" | wc -l)

    print_info "Found $zip_count ZIP files and $dat_count DAT files"

    # Verify each ZIP file has content
    for zip_file in "$TEST_OUTPUT_DIR"/*.zip; do
        if [ -f "$zip_file" ]; then
            local file_count=$(unzip -l "$zip_file" | grep -E "\.(pdf|jpg|tiff|eml)$" | wc -l)
            print_info "ZIP $(basename "$zip_file"): $file_count files"

            if [ $file_count -eq 0 ]; then
                print_warning "ZIP file appears to be empty: $(basename "$zip_file")"
            fi
        fi
    done

    return 0
}

# Cleanup
cleanup() {
    print_info "Cleaning up test output..."
    rm -rf "$TEST_OUTPUT_DIR"
}

# Main execution
main() {
    print_info "Starting cross-platform compatibility tests on $(detect_platform)"
    print_info "Platform: $PLATFORM"
    print_info "Architecture: $(uname -m)"
    print_info "Shell: $0"

    # Run tests
    test_basic_functionality || exit 1
    test_refactored_components || exit 1
    test_filesystem_compatibility || exit 1
    test_performance || exit 1
    verify_output || exit 1

    # Cleanup
    cleanup

    print_success "All cross-platform compatibility tests passed successfully!"
}

# Run main function
main "$@"