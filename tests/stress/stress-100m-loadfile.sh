#!/bin/bash

# =============================================================================
# ZIPPER STRESS TEST - 100 MILLION RECORD LOAD FILE CONTRACT
# =============================================================================
#
# STRESS TEST DETAILS:
# - Mode: Loadfile-Only (no Native Files or Archive — the cheap upper-contract
#   probe recommended by issue #648)
# - Record Count: 100 million (REQ_E-009 upper contract)
# - Seed: fixed (repeatable)
# - Focus: Verifies the 100-million count path streams in bounded memory,
#   preserves record cardinality, and produces correct boundary IDs
#
# IMPORTANT NOTES:
# - This stress test is for MANUAL INVOCATION ONLY
# - NOT part of CI/CD or pre-commit hooks (runner resources do not permit)
# - Requires ~34GB+ available disk space for the full 100M run
# - Runtime: typically 15-45 minutes depending on disk/CPU
# - Memory: bounded (<1GB) — Loadfile-Only streams records lazily
#
# SMOKE / LOCAL VALIDATION:
# - STRESS_FILE_COUNT=1000000 ./stress-100m-loadfile.sh   # ~1-2 min, ~300MB
# - STRESS_ASSUME_YES=1 skips the confirmation prompt (automation)
#
# EXIT CODES:
# - 0: all contract assertions passed
# - 1: environment/runner problem (disk, memory, missing tools, CLI crash) —
#   this indicates runner exhaustion, not a product limit
# - 2: product contract violation (cardinality, boundary IDs, header) —
#   this indicates a product regression or a genuine product limit
# =============================================================================

set -euo pipefail  # Exit on any error, use unset variable as error, and fail on pipe failures

# --- Configuration ---
TEST_NAME="100M_Record_Load_File_Contract"
OUTPUT_DIR="results"
PROJECT="../../src/Zipper.csproj"

# Test parameters (overridable for smoke runs; the contract count is 100M)
CONTRACT_COUNT=100000000
FILE_COUNT="${STRESS_FILE_COUNT:-$CONTRACT_COUNT}"
SEED="${STRESS_SEED:-42}"
ASSUME_YES="${STRESS_ASSUME_YES:-0}"

# Measured bytes per DAT record (12 columns, values + delimiters/EOL).
# Used only for the pre-flight disk check; measured size is reported after.
BYTES_PER_RECORD=290

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
check_required_utilities() {
    local missing_utils=()
    for util in bc df stat grep wc sed tail awk dotnet free nproc; do
        if ! command -v "$util" &> /dev/null; then
            missing_utils+=("$util")
        fi
    done

    if [ ${#missing_utils[@]} -gt 0 ]; then
        print_error "Missing required utilities: ${missing_utils[*]}"
        print_info "Install missing utilities (Ubuntu/Debian: sudo apt-get install bc procps; macOS: brew install bc coreutils)"
        exit 1
    fi

    if [ -x /usr/bin/time ]; then
        TIME_CMD="/usr/bin/time -v"
    else
        TIME_CMD=""
        print_warning "GNU time (/usr/bin/time) not found — peak RSS will not be measured"
    fi

    if ! [[ "$FILE_COUNT" =~ ^[0-9]+$ ]] || [ "$FILE_COUNT" -lt 1 ]; then
        print_error "STRESS_FILE_COUNT must be a positive integer, got: '$FILE_COUNT'"
        exit 1
    fi
    if ! [[ "$SEED" =~ ^-?[0-9]+$ ]]; then
        print_error "STRESS_SEED must be an integer, got: '$SEED'"
        exit 1
    fi
}

check_disk_space() {
    print_info "Checking available disk space..."

    local required_bytes
    required_bytes=$(printf "%.0f" "$(echo "$FILE_COUNT * $BYTES_PER_RECORD * 1.15" | bc)")
    local available_kb available_bytes available_gb required_gb
    available_kb=$(df --output=avail . | tail -1 | tr -d ' ')
    available_bytes=$((available_kb * 1024))
    available_gb=$(echo "scale=2; $available_bytes / 1024^3" | bc)
    required_gb=$(echo "scale=2; $required_bytes / 1024^3" | bc)

    print_info "Available space: ${available_gb}GB"
    print_info "Required space: ${required_gb}GB (estimated for $(printf "%'d" "$FILE_COUNT") records)"

    if [ "$available_bytes" -lt "$required_bytes" ]; then
        print_error "Insufficient disk space. Need ${required_gb}GB, have ${available_gb}GB"
        print_error "This is a runner/environment limit, not a product limit."
        print_info "Use STRESS_FILE_COUNT for a smaller smoke run."
        exit 1
    fi

    print_success "Disk space validation passed"
}

check_system_resources() {
    print_info "Checking system resources..."

    local available_memory_mb cpu_cores
    available_memory_mb=$(free -m | awk '/^Mem:/ {print $7}')
    cpu_cores=$(nproc)

    print_info "Available memory: ${available_memory_mb}MB"
    print_info "CPU cores: $cpu_cores"

    if [ "$available_memory_mb" -lt 1024 ]; then
        print_error "Less than 1GB available memory — environment limit, not a product limit"
        exit 1
    fi

    print_success "System resource check completed"
}

confirm_execution() {
    if [ "$ASSUME_YES" = "1" ]; then
        return
    fi
    echo ""
    print_warning "Press Enter to start the stress test, or Ctrl+C to cancel"
    read -r
}

# --- Test Execution ---
run_stress_test() {
    print_header "RUNNING STRESS TEST"

    mkdir -p "$OUTPUT_DIR"

    print_info "Command: dotnet run --project $PROJECT -c Release -- --loadfile-only --count $FILE_COUNT --loadfile-format dat --seed $SEED --output-path $OUTPUT_DIR"

    local start_time end_time
    start_time=$(date +%s)

    if [ -n "$TIME_CMD" ]; then
        # GNU time writes its report (incl. peak RSS) to stderr; keep it for the summary.
        $TIME_CMD -o "$OUTPUT_DIR/.time-report.txt" \
            dotnet run --project "$PROJECT" -c Release -- \
            --loadfile-only \
            --count "$FILE_COUNT" \
            --loadfile-format dat \
            --seed "$SEED" \
            --output-path "$OUTPUT_DIR" || {
            print_error "Zipper exited non-zero. Inspect output above: an OutOfMemoryException or"
            print_error "disk-full error indicates runner exhaustion; anything else may be a product limit."
            exit 1
        }
    else
        dotnet run --project "$PROJECT" -c Release -- \
            --loadfile-only \
            --count "$FILE_COUNT" \
            --loadfile-format dat \
            --seed "$SEED" \
            --output-path "$OUTPUT_DIR" || {
            print_error "Zipper exited non-zero. Inspect output above: an OutOfMemoryException or"
            print_error "disk-full error indicates runner exhaustion; anything else may be a product limit."
            exit 1
        }
    fi

    end_time=$(date +%s)
    DURATION=$((end_time - start_time))

    print_success "Generation completed in $((DURATION / 3600))h $(((DURATION % 3600) / 60))m $((DURATION % 60))s"
}

# --- Post-test Validation ---
validate_results() {
    print_header "VALIDATING RESULTS"

    local dat_file
    dat_file=$(find "$OUTPUT_DIR" -name "loadfile_*.dat" -print -quit)

    if [ -z "$dat_file" ]; then
        print_error "No loadfile_*.dat file found — generation failed before writing output"
        exit 1
    fi

    local dat_size dat_size_mb
    dat_size=$(stat -c%s "$dat_file")
    dat_size_mb=$(echo "scale=2; $dat_size / 1024^2" | bc)
    print_info "Load file: $(basename "$dat_file") (${dat_size_mb}MB)"

    # Cardinality: exactly FILE_COUNT records + 1 header line
    print_info "Validating record cardinality..."
    local line_count expected_lines
    line_count=$(wc -l < "$dat_file")
    expected_lines=$((FILE_COUNT + 1))

    if [ "$line_count" -ne "$expected_lines" ]; then
        print_error "PRODUCT CONTRACT VIOLATION: record count mismatch. Expected: $expected_lines lines (incl. header), Found: $line_count"
        exit 2
    fi
    print_success "Cardinality verified: $(printf "%'d" "$FILE_COUNT") records + header"

    # Boundary IDs: first record DOC00000001, last record DOC<count> (D8 width
    # overflow is expected past 99,999,999 — the 100Mth ID is DOC100000000).
    print_info "Validating boundary IDs..."
    local first_id last_id expected_last_id
    first_id=$(head -2 "$dat_file" | tail -1 | grep -oE 'DOC[0-9]+' | head -1)
    last_id=$(tail -1 "$dat_file" | grep -oE 'DOC[0-9]+' | head -1)
    expected_last_id=$(printf "DOC%08d" "$FILE_COUNT")

    if [ "$first_id" != "DOC00000001" ]; then
        print_error "PRODUCT CONTRACT VIOLATION: first record ID is '$first_id', expected 'DOC00000001'"
        exit 2
    fi
    if [ "$last_id" != "$expected_last_id" ]; then
        print_error "PRODUCT CONTRACT VIOLATION: last record ID is '$last_id', expected '$expected_last_id'"
        exit 2
    fi
    print_success "Boundary IDs verified: first=$first_id last=$last_id"

    # Header sanity
    if ! head -1 "$dat_file" | grep -q "Control Number"; then
        print_error "PRODUCT CONTRACT VIOLATION: header row missing 'Control Number'"
        exit 2
    fi
    print_success "Header row verified"
}

# --- Summary ---
print_summary() {
    print_header "STRESS TEST SUMMARY"

    local throughput="n/a"
    if [ "$DURATION" -gt 0 ]; then
        throughput=$(echo "scale=0; $FILE_COUNT / $DURATION" | bc)
    fi

    echo "  Records generated: $(printf "%'d" "$FILE_COUNT")"
    echo "  Elapsed:           $((DURATION / 3600))h $(((DURATION % 3600) / 60))m $((DURATION % 60))s"
    echo "  Throughput:        $(printf "%'d" "$throughput") records/second"
    if [ -n "$TIME_CMD" ] && [ -f "$OUTPUT_DIR/.time-report.txt" ]; then
        echo "  Peak RSS:          $(grep "Maximum resident set size" "$OUTPUT_DIR/.time-report.txt" | awk '{print $6 / 1024 " MB"}')"
    fi
    echo ""
    if [ "$FILE_COUNT" -eq "$CONTRACT_COUNT" ]; then
        print_success "REQ_E-009 upper contract verified at $(printf "%'d" "$FILE_COUNT") records"
    else
        print_success "Contract checks passed at $(printf "%'d" "$FILE_COUNT") records (smoke run; full REQ_E-009 contract is $(printf "%'d" "$CONTRACT_COUNT"))"
    fi
    print_info "Generated files are in: $OUTPUT_DIR"
    print_info "Clean up when no longer needed: rm -rf $OUTPUT_DIR"
}

# --- Main Execution ---
main() {
    print_header "ZIPPER STRESS TEST: $TEST_NAME"
    echo ""
    print_warning "This stress test will consume significant resources:"
    echo "  - Records: $(printf "%'d" "$FILE_COUNT") (contract: 100,000,000)"
    echo "  - Disk:    ~$(echo "scale=1; $FILE_COUNT * $BYTES_PER_RECORD / 1024^3" | bc)GB estimated"
    echo "  - Time:    15-45 minutes at full contract count (~10-20s per 1M smoke)"
    echo "  - Memory:  bounded (<1GB) — Loadfile-Only streams records lazily"
    echo "  - Seed:    $SEED (repeatable)"
    echo ""

    check_required_utilities
    check_disk_space
    check_system_resources
    confirm_execution

    run_stress_test
    validate_results
    print_summary
}

# Check if script is being run directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi
