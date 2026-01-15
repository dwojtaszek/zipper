#!/bin/bash

# =============================================================================
# ZIPPER STRESS TEST SUITE RUNNER
# =============================================================================
#
# This script runs the Zipper stress test suite.
# It can run all tests or a specific test if a name is provided.
#
# Usage: ./run-stress-tests.sh [test_name]
#   test_name: Optional. A substring of the test to run (e.g., "10gb", "multi-format").
#              If omitted, all stress tests will be run.
#
# =============================================================================

set -e

# --- Configuration ---
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Color codes
RED='\033[0;41m'
GREEN='\033[0;42m'
YELLOW='\033[1;43m'
BLUE='\033[0;44m'
BOLD='\033[1m'
NC='\033[0m' # No Color

# --- Helper Functions ---
print_header() {
    echo -e "${BLUE}==============================================================================${NC}"
    echo -e "${BLUE}${NC}"
    echo -e "${BLUE}==============================================================================${NC}"
}

print_warning() {
    echo -e "${YELLOW}[ WARNING ]${NC} "
}

print_info() {
    echo -e "${BLUE}[ INFO ]${NC} "
}

print_success() {
    echo -e "${GREEN}[ SUCCESS ]${NC} "
}

print_error() {
    echo -e "${RED}[ ERROR ]${NC} "
}

# --- Stress Test Definitions ---
declare -A STRESS_TESTS=(
    ["1"]="10GB Maximum File Count Challenge|stress-10gb-filecount.sh|~12GB|5-10 minutes|Up to 2GB|Tests file count limits and Zip64"
    ["3"]="30GB Attachment-Heavy EML Focus|stress-30gb-attachments.sh|~36GB|15-30 minutes|Up to 6GB|Tests attachment processing"
    ["4"]="Large Load File Performance|stress-large-loadfile.sh|~2GB|5-10 minutes|Under 1GB|Tests load file bottlenecks"
)

# --- System Check ---
check_system_requirements() {
    print_header "SYSTEM REQUIREMENTS CHECK"

    local available_kb=$(df --output=avail . | tail -1 | tr -d ' ')
    local available_bytes=$((available_kb * 1024))
    local available_gb_scaled=$(echo "scale=1; $available_bytes / 1024^3" | bc)
    local memory_gb=$(free -g | awk '/^Mem:/ {print $7}')
    local cpu_cores=$(nproc)

    print_info "System Resources:"
    echo "  - Available Disk Space: ${available_gb_scaled}GB"
    echo "  - Available Memory: ${memory_gb}GB"
    echo "  - CPU Cores: $cpu_cores"
    echo ""

    # Check for required utilities
    local missing_utils=()
    for util in bc unzip file stat grep wc find; do
        if ! command -v "$util" &> /dev/null; then
            missing_utils+=("$util")
        fi
    done

    if [ ${#missing_utils[@]} -gt 0 ]; then
        print_error "Missing required utilities: ${missing_utils[*]}"
        print_info "Install missing utilities:"
        echo "  Ubuntu/Debian: sudo apt-get install bc unzip"
        echo "  macOS: brew install bc"
        echo ""
        return 1
    fi

    # Check if application is built
    if [ ! -f "$PROJECT_ROOT/src/bin/Release/net8.0/Zipper" ] && [ ! -f "$PROJECT_ROOT/src/bin/Debug/net8.0/Zipper" ]; then
        print_error "Zipper application not built"
        print_info "Build the application first:"
        echo "  cd $PROJECT_ROOT"
        echo "  dotnet build -c Release"
        echo ""
        exit 1
    fi

    print_success "System requirements check passed"
    echo ""
}

# --- Main Execution ---
main() {
    local specific_test=""

    # Change to script directory
    cd "$SCRIPT_DIR"

    # Run system requirements check
    if ! check_system_requirements; then
        exit 1
    fi

    print_header "ZIPPER STRESS TEST SUITE"
    print_warning "These are manual stress tests that consume significant resources and time."

    if [ -n "$specific_test" ]; then
        # Run a specific test
        local found_test=false
        for key in "${!STRESS_TESTS[@]}"; do
            IFS='|' read -r description script disk time memory focus <<< "${STRESS_TESTS[$key]}"
            if [[ "$description" == *"$specific_test"* ]] || [[ "$script" == *"$specific_test"* ]]; then
                found_test=true
                print_info "Running specific stress test: $description"
                if [ -f "./$script" ]; then
                    print_info "Executing: $script"
                    ./"$script"
                else
                    print_error "Script not found: $script"
                    exit 1
                fi
                break
            fi
        done
        if [ "$found_test" = false ]; then
            print_error "No stress test found matching '$specific_test'"
            exit 1
        fi
    else
        # Run all tests
        print_info "Running all stress tests..."
        for key in $(echo "${!STRESS_TESTS[@]}" | tr ' ' '\n' | sort -n); do
            IFS='|' read -r description script disk time memory focus <<< "${STRESS_TESTS[$key]}"
            print_header "Starting Test: $description"
            if [ -f "./$script" ]; then
                print_info "Executing: $script"
                ./"$script"
                echo
            else
                print_error "Script not found: $script"
            fi
        done
    fi

    echo
    print_success "All specified stress tests completed successfully!"
}

# Check if script is being run directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi