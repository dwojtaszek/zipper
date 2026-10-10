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

set -euo pipefail

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
    local message
    message=$(printf '%s' "$1" | LC_ALL=C tr -d '[:cntrl:]')
    printf '%b%s%b\n' "$BLUE" "==============================================================================" "$NC"
    printf '%b%s%b\n' "$BLUE" "$message" "$NC"
    printf '%b%s%b\n' "$BLUE" "==============================================================================" "$NC"
}

print_message() {
    local color="$1"
    local message
    message=$(printf '%s' "$2" | LC_ALL=C tr -d '[:cntrl:]')
    printf '%b%s\n' "$color" "$message"
}

print_warning() {
    print_message "${YELLOW}[ WARNING ]${NC} " "$1"
}

print_info() {
    print_message "${BLUE}[ INFO ]${NC} " "$1"
}

print_success() {
    print_message "${GREEN}[ SUCCESS ]${NC} " "$1"
}

print_error() {
    print_message "${RED}[ ERROR ]${NC} " "$1"
}

# --- Stress Test Definitions ---
# Each record is "key|description|script|disk|time|memory|focus". A plain
# indexed array (records carry their own key) instead of declare -A keeps the
# runner working on bash 3.2, which is the stock macOS /bin/bash.
STRESS_TESTS=(
    "1|10GB Maximum File Count Challenge|stress-10gb-filecount.sh|~12GB|5-10 minutes|Up to 2GB|Tests file count limits and Zip64"
    "3|30GB Attachment-Heavy EML Focus|stress-30gb-attachments.sh|~36GB|15-30 minutes|Up to 6GB|Tests attachment processing"
    "4|Large Load File Performance|stress-large-loadfile.sh|~2GB|5-10 minutes|Under 1GB|Tests load file bottlenecks"
    "5|100M Record Load File Contract|stress-100m-loadfile.sh|~34GB|15-45 minutes|Under 1GB|Verifies REQ_E-009 100M upper contract"
)

# --- System Check ---
check_system_requirements() {
    if [ "${STRESS_SKIP_SYSCHECK:-0}" = "1" ]; then
        return 0
    fi

    print_header "SYSTEM REQUIREMENTS CHECK"

    local available_kb=$(df -k -P . | awk 'NR>1 {print $4; exit}')
    available_kb=${available_kb:-0}
    local available_bytes=$((available_kb * 1024))
    local available_gb_scaled
    if command -v bc >/dev/null 2>&1; then
        available_gb_scaled=$(echo "scale=1; $available_bytes / 1024^3" | bc)
    else
        available_gb_scaled=$(awk "BEGIN {printf \"%.1f\", $available_bytes / 1073741824}")
    fi
    local memory_gb="n/a"
    local cpu_cores=1
    if command -v free >/dev/null 2>&1; then
        if ! memory_gb=$(free -g | awk '/^Mem:/ {print $7}'); then
            memory_gb=0
        fi
        memory_gb=${memory_gb:-0}
    elif command -v sysctl >/dev/null 2>&1; then
        memory_gb=$(( $(sysctl -n hw.memsize 2>/dev/null || echo 0) / 1073741824 ))
    fi

    if command -v nproc >/dev/null 2>&1; then
        cpu_cores=$(nproc)
    elif command -v sysctl >/dev/null 2>&1; then
        cpu_cores=$(sysctl -n hw.ncpu 2>/dev/null || echo "1")
    fi

    print_info "System Resources:"
    echo "  - Available Disk Space: ${available_gb_scaled}GB"
    echo "  - Available Memory: ${memory_gb}GB"
    echo "  - CPU Cores: $cpu_cores"
    echo ""

    # Check for required utilities
    local missing_utils=()
    for util in unzip file stat grep wc find; do
        if ! command -v "$util" &> /dev/null; then
            missing_utils+=("$util")
        fi
    done

    if [ ${#missing_utils[@]} -gt 0 ]; then
        print_error "Missing required utilities: ${missing_utils[*]}"
        print_info "Install missing utilities:"
        echo "  Ubuntu/Debian: sudo apt-get install unzip"
        echo ""
        return 1
    fi

    # Check if application is built
    local has_bin=false
    if [ -n "${ZIPPER_BIN:-}" ] && [ -x "$ZIPPER_BIN" ]; then
        has_bin=true
    elif [ -d "$PROJECT_ROOT/src/bin" ]; then
        local found_bin
        while IFS= read -r -d '' found_bin; do
            if [ -n "$found_bin" ] && [ -x "$found_bin" ]; then
                has_bin=true
                break
            fi
        done < <(find "$PROJECT_ROOT/src/bin" -type f \( -name "Zipper" -o -name "Zipper.exe" \) -print0 2>/dev/null || true)
    fi

    if [ "$has_bin" = false ]; then
        print_error "Zipper application not built"
        print_info "Build the application first:"
        echo "  cd $PROJECT_ROOT"
        echo "  dotnet build -c Release"
        echo ""
        return 1
    fi

    print_success "System requirements check passed"
    echo ""
}

# --- Main Execution ---
main() {
    local specific_test="${1:-}"

    if [ -z "$specific_test" ]; then
        print_error "No stress test selector specified. Provide a test selector (e.g., '100m', '10gb', 'attachments', 'large', or 'all')."
        return 1 2>/dev/null || exit 1
    fi

    # Change to script directory
    cd "$SCRIPT_DIR"

    # Run system requirements check
    if ! check_system_requirements; then
        return 1 2>/dev/null || exit 1
    fi

    print_header "ZIPPER STRESS TEST SUITE"
    print_warning "These are manual stress tests that consume significant resources and time."

    local specific_test_lower
    specific_test_lower=$(printf '%s' "$specific_test" | tr '[:upper:]' '[:lower:]')
    local specific_test_display
    printf -v specific_test_display '%q' "$specific_test"

    if [[ "$specific_test_lower" == "all" ]]; then
        # Run all tests
        print_info "Running all stress tests..."
        for record in "${STRESS_TESTS[@]}"; do
            IFS='|' read -r key description script disk time memory focus <<< "$record"
            print_header "Starting Test: $description"
            if [ -f "./$script" ]; then
                print_info "Executing: $script"
                local exit_code=0
                ./"$script" || exit_code=$?
                if [ $exit_code -ne 0 ]; then
                    print_error "Stress test failed with exit code $exit_code: $script"
                    return $exit_code 2>/dev/null || exit $exit_code
                fi
                echo
            else
                print_error "Script not found: $script"
                return 1 2>/dev/null || exit 1
            fi
        done
    else
        # Run a specific test: check for exact key match first
        local match_count=0
        local matched_record=""
        for record in "${STRESS_TESTS[@]}"; do
            IFS='|' read -r key description script disk time memory focus <<< "$record"
            if [[ "$key" == "$specific_test" ]]; then
                matched_record="$record"
                match_count=1
                break
            fi
        done

        if [ "$match_count" -eq 0 ]; then
            for record in "${STRESS_TESTS[@]}"; do
                IFS='|' read -r key description script disk time memory focus <<< "$record"
                local desc_lower script_lower
                desc_lower=$(printf '%s' "$description" | tr '[:upper:]' '[:lower:]')
                script_lower=$(printf '%s' "$script" | tr '[:upper:]' '[:lower:]')
                if [[ "$desc_lower" == *"$specific_test_lower"* || "$script_lower" == *"$specific_test_lower"* ]]; then
                    match_count=$((match_count + 1))
                    matched_record="$record"
                fi
            done
        fi

        if [ "$match_count" -eq 0 ]; then
            print_error "No stress test found matching '$specific_test_display'"
            return 1 2>/dev/null || exit 1
        fi

        if [ "$match_count" -gt 1 ]; then
            print_error "Ambiguous stress test selector '$specific_test_display' matches multiple tests. Specify a unique test name."
            return 1 2>/dev/null || exit 1
        fi

        IFS='|' read -r key description script disk time memory focus <<< "$matched_record"
        print_info "Running specific stress test: $description"
        if [ -f "./$script" ]; then
            print_info "Executing: $script"
            local exit_code=0
            ./"$script" || exit_code=$?
            if [ $exit_code -ne 0 ]; then
                print_error "Stress test failed with exit code $exit_code: $script"
                return $exit_code 2>/dev/null || exit $exit_code
            fi
        else
            print_error "Script not found: $script"
            return 1 2>/dev/null || exit 1
        fi
    fi

    echo
    print_success "All specified stress tests completed successfully!"
}

# Check if script is being run directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi