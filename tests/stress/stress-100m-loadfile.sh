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
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PROJECT="${ZIPPER_PROJECT:-$PROJECT_ROOT/src/Zipper.csproj}"
STRESS_RESULTS_DIR="${STRESS_RESULTS_DIR:-$SCRIPT_DIR/results}"
OUTPUT_DIR="${OUTPUT_DIR:-$STRESS_RESULTS_DIR/${TEST_NAME}_$(date +%Y%m%d_%H%M%S)_$$}"

# Test parameters (overridable for smoke runs; the contract count is 100M)
CONTRACT_COUNT="${CONTRACT_COUNT:-100000000}"
FILE_COUNT="${STRESS_FILE_COUNT:-${FILE_COUNT:-$CONTRACT_COUNT}}"
SEED="${STRESS_SEED:-42}"
ASSUME_YES="${STRESS_ASSUME_YES:-0}"

# Maximum RSS budget in KB (1GB = 1048576 KB = 1024 MB)
MAX_RSS_KB=$((1024 * 1024))
PEAK_RSS_KB=""
PEAK_RSS_MB=""
DURATION=0
ZIPPER_BIN="${ZIPPER_BIN:-}"

# Measured bytes per DAT record (12 columns, values + delimiters/EOL).
# Used only for the pre-flight disk check; measured size is reported after.
BYTES_PER_RECORD=290

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
    for util in df stat grep wc sed tail awk dotnet; do
        if ! command -v "$util" &> /dev/null; then
            missing_utils+=("$util")
        fi
    done

    if [ ${#missing_utils[@]} -gt 0 ]; then
        print_error "Missing required utilities: ${missing_utils[*]}"
        print_info "Install missing utilities (Ubuntu/Debian: sudo apt-get install coreutils; macOS: brew install coreutils)"
        exit 1
    fi

    if ! [[ "$FILE_COUNT" =~ ^[0-9]+$ ]] || [ "$FILE_COUNT" -lt 1 ]; then
        print_error "STRESS_FILE_COUNT must be a positive integer, got: '$FILE_COUNT'"
        exit 1
    fi
    if ! [[ "$SEED" =~ ^-?[0-9]+$ ]]; then
        print_error "STRESS_SEED must be an integer, got: '$SEED'"
        exit 1
    fi

    if [ -z "${TIME_CMD+x}" ]; then
        if command -v /usr/bin/time >/dev/null 2>&1 && /usr/bin/time --version 2>&1 | grep -qi "GNU"; then
            TIME_CMD="/usr/bin/time -v"
        elif command -v gtime >/dev/null 2>&1 && gtime --version 2>&1 | grep -qi "GNU"; then
            TIME_CMD="gtime -v"
        else
            TIME_CMD=""
            if [ "$FILE_COUNT" -eq "$CONTRACT_COUNT" ]; then
                print_error "GNU time (/usr/bin/time or gtime) not found — memory measurement required for full 100M contract run"
                exit 1
            else
                print_warning "GNU time (/usr/bin/time or gtime) not found — peak RSS will not be measured"
            fi
        fi
    fi
}

check_disk_space() {
    print_info "Checking available disk space..."

    local required_bytes
    if command -v bc >/dev/null 2>&1; then
        required_bytes=$(printf "%.0f" "$(echo "$FILE_COUNT * $BYTES_PER_RECORD * 1.15" | bc)")
    else
        required_bytes=$(awk "BEGIN {printf \"%.0f\", $FILE_COUNT * $BYTES_PER_RECORD * 1.15}")
    fi
    mkdir -p "$OUTPUT_DIR"
    local available_kb available_bytes available_gb required_gb
    available_kb=$(df -k -P "$OUTPUT_DIR" | awk 'NR>1 {print $4; exit}')
    available_kb=${available_kb:-0}
    available_bytes=$((available_kb * 1024))
    available_gb=$(to_gb "$available_bytes")
    required_gb=$(to_gb "$required_bytes")

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
    if command -v free >/dev/null 2>&1; then
        if ! available_memory_mb=$(free -m | awk '/^Mem:/ {print $7}'); then
            available_memory_mb=0
        fi
        available_memory_mb=${available_memory_mb:-0}
    elif command -v sysctl >/dev/null 2>&1; then
        available_memory_mb=$(( $(sysctl -n hw.memsize 2>/dev/null || echo 0) / 1048576 ))
    else
        available_memory_mb=2048
    fi

    if command -v nproc >/dev/null 2>&1; then
        cpu_cores=$(nproc)
    elif command -v sysctl >/dev/null 2>&1; then
        cpu_cores=$(sysctl -n hw.ncpu 2>/dev/null || echo "1")
    else
        cpu_cores=1
    fi

    print_info "Available memory: ${available_memory_mb}MB"
    print_info "CPU cores: $cpu_cores"

    if [[ "$available_memory_mb" =~ ^[0-9]+$ ]] && [ "$available_memory_mb" -lt 1024 ]; then
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

resolve_zipper_bin() {
    if [ -n "${ZIPPER_BIN:-}" ] && [ -x "$ZIPPER_BIN" ]; then
        return 0
    fi

    local tfm
    tfm=$(grep -oE '<TargetFramework>[^<]+</TargetFramework>' "$PROJECT" 2>/dev/null | sed -E 's/<[^>]+>//g' || true)
    [[ -z "$tfm" ]] && tfm="net10.0"
    local build_dir="$PROJECT_ROOT/src/bin/Release/$tfm"

    if [ ! -f "$build_dir/Zipper" ] && [ ! -f "$build_dir/Zipper.exe" ]; then
        print_info "Building Release binary ($tfm) for accurate CLI memory measurement..."
        dotnet build "$PROJECT" -c Release --nologo -v quiet || {
            print_error "Failed to build Zipper Release binary"
            return 1
        }
    fi

    if [ -f "$build_dir/Zipper" ]; then
        ZIPPER_BIN="$build_dir/Zipper"
    elif [ -f "$build_dir/Zipper.exe" ]; then
        ZIPPER_BIN="$build_dir/Zipper.exe"
    fi

    if [ -z "${ZIPPER_BIN:-}" ] || [ ! -x "$ZIPPER_BIN" ]; then
        print_error "Could not find built Zipper binary in $build_dir"
        return 1
    fi
    return 0
}

# --- Test Execution ---
run_stress_test() {
    print_header "RUNNING STRESS TEST"

    mkdir -p "$OUTPUT_DIR"

    resolve_zipper_bin || exit 1

    local cmd=()
    if [ -n "${ZIPPER_BIN:-}" ] && [ -x "$ZIPPER_BIN" ]; then
        print_info "Executing built CLI: $ZIPPER_BIN"
        cmd=("$ZIPPER_BIN" --loadfile-only --count "$FILE_COUNT" --loadfile-format dat --seed "$SEED" --output-path "$OUTPUT_DIR")
    else
        print_error "Zipper binary not available"
        exit 1
    fi

    print_info "Command: ${cmd[*]}"

    local start_time end_time
    start_time=$(date +%s)

    if [ -n "$TIME_CMD" ]; then
        # GNU time writes its report (incl. peak RSS) to stderr; keep it for the summary.
        $TIME_CMD -o "$OUTPUT_DIR/.time-report.txt" "${cmd[@]}" || {
            print_error "Zipper exited non-zero. Inspect output above: an OutOfMemoryException or"
            print_error "disk-full error indicates runner exhaustion; anything else may be a product limit."
            exit 1
        }
    else
        "${cmd[@]}" || {
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

    local dat_file=""
    if [ -d "$OUTPUT_DIR" ]; then
        dat_file=$(find "$OUTPUT_DIR" -name "loadfile_*.dat" -print -quit 2>/dev/null || true)
    fi

    if [ -z "$dat_file" ]; then
        print_error "PRODUCT CONTRACT VIOLATION: No loadfile_*.dat file found — generation failed before writing output"
        return 2 2>/dev/null || exit 2
    fi

    local dat_size dat_size_mb
    dat_size=$(get_file_size "$dat_file")
    dat_size_mb=$(to_mb "$dat_size")
    print_info "Load file: $(basename "$dat_file") (${dat_size_mb}MB)"

    # Cardinality: exactly FILE_COUNT records + 1 header line
    print_info "Validating record cardinality..."
    local line_count expected_lines
    line_count=$(wc -l < "$dat_file")
    expected_lines=$((FILE_COUNT + 1))

    if [ "$line_count" -ne "$expected_lines" ]; then
        print_error "PRODUCT CONTRACT VIOLATION: record count mismatch. Expected: $expected_lines lines (incl. header), Found: $line_count"
        return 2 2>/dev/null || exit 2
    fi
    print_success "Cardinality verified: $(printf "%'d" "$FILE_COUNT") records + header"

    # Boundary IDs: first record DOC00000001, last record DOC<count> (D8 width
    # overflow is expected past 99,999,999 — the 100Mth ID is DOC100000000).
    print_info "Validating boundary IDs..."
    local first_id last_id expected_last_id
    first_id=$(head -n 2 "$dat_file" | tail -n 1 | grep -oE 'DOC[0-9]+' | head -n 1)
    last_id=$(tail -n 1 "$dat_file" | grep -oE 'DOC[0-9]+' | head -n 1)
    expected_last_id=$(printf "DOC%08d" "$FILE_COUNT")

    if [ "$first_id" != "DOC00000001" ]; then
        print_error "PRODUCT CONTRACT VIOLATION: first record ID is '$first_id', expected 'DOC00000001'"
        return 2 2>/dev/null || exit 2
    fi
    if [ "$last_id" != "$expected_last_id" ]; then
        print_error "PRODUCT CONTRACT VIOLATION: last record ID is '$last_id', expected '$expected_last_id'"
        return 2 2>/dev/null || exit 2
    fi
    print_success "Boundary IDs verified: first=$first_id last=$last_id"

    # Header sanity
    if ! head -n 1 "$dat_file" | grep -q "Control Number"; then
        print_error "PRODUCT CONTRACT VIOLATION: header row missing 'Control Number'"
        return 2 2>/dev/null || exit 2
    fi
    print_success "Header row verified"

    # Bounded-memory budget enforcement (< 1GB / 1024MB)
    print_info "Validating memory budget..."
    PEAK_RSS_KB=""
    PEAK_RSS_MB=""
    if [ -f "$OUTPUT_DIR/.time-report.txt" ]; then
        PEAK_RSS_KB=$(grep -i "Maximum resident set size" "$OUTPUT_DIR/.time-report.txt" | awk -F': ' '{print $2}' | tr -d ' ' || true)
    fi

    if [ -n "$PEAK_RSS_KB" ] && [[ "$PEAK_RSS_KB" =~ ^[0-9]+$ ]]; then
        PEAK_RSS_MB=$(to_mb $((PEAK_RSS_KB * 1024)))
        if [ "$PEAK_RSS_KB" -gt "$MAX_RSS_KB" ]; then
            print_error "PRODUCT CONTRACT VIOLATION: Peak RSS (${PEAK_RSS_MB} MB) exceeded budget (1024 MB)"
            return 2 2>/dev/null || exit 2
        fi
        print_success "Peak RSS verified within budget: ${PEAK_RSS_MB} MB (< 1024 MB)"
    else
        if [ "$FILE_COUNT" -eq "$CONTRACT_COUNT" ]; then
            print_error "PRODUCT CONTRACT VIOLATION: Peak RSS was not measured on full 100M contract run (budget: < 1024 MB)"
            return 2 2>/dev/null || exit 2
        else
            print_warning "Memory was not checked: GNU time measurement unavailable (budget: < 1024 MB)"
        fi
    fi
    return 0
}

# --- Summary ---
print_summary() {
    print_header "STRESS TEST SUMMARY"

    local throughput="n/a"
    if [ "${DURATION:-0}" -gt 0 ]; then
        throughput=$((FILE_COUNT / DURATION))
    fi

    echo "  Records generated: $(printf "%'d" "$FILE_COUNT")"
    echo "  Elapsed:           $((DURATION / 3600))h $(((DURATION % 3600) / 60))m $((DURATION % 60))s"
    if [ "$throughput" != "n/a" ]; then
        echo "  Throughput:        $(printf "%'d" "$throughput") records/second"
    else
        echo "  Throughput:        n/a"
    fi
    if [ -n "$PEAK_RSS_MB" ]; then
        echo "  Peak RSS:          ${PEAK_RSS_MB} MB (budget: < 1024 MB)"
    elif [ -f "$OUTPUT_DIR/.time-report.txt" ]; then
        local raw_rss=$(grep -i "Maximum resident set size" "$OUTPUT_DIR/.time-report.txt" | awk -F': ' '{print $2}' | tr -d ' ' || true)
        if [ -n "$raw_rss" ] && [[ "$raw_rss" =~ ^[0-9]+$ ]]; then
            local mb=$(to_mb $((raw_rss * 1024)))
            echo "  Peak RSS:          ${mb} MB (budget: < 1024 MB)"
        else
            echo "  Peak RSS:          not measured (timing tool output format unrecognized; budget: < 1024 MB)"
        fi
    else
        echo "  Peak RSS:          not measured (GNU time unavailable; contract budget: < 1024 MB)"
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
    local estimated_gb
    if command -v bc >/dev/null 2>&1; then
        estimated_gb=$(echo "scale=1; $FILE_COUNT * $BYTES_PER_RECORD / 1024^3" | bc)
    else
        estimated_gb=$(awk "BEGIN {printf \"%.1f\", $FILE_COUNT * $BYTES_PER_RECORD / 1073741824}")
    fi

    print_warning "This stress test will consume significant resources:"
    echo "  - Records: $(printf "%'d" "$FILE_COUNT") (contract: 100,000,000)"
    echo "  - Disk:    ~${estimated_gb}GB estimated"
    echo "  - Time:    15-45 minutes at full contract count (~10-20s per 1M smoke)"
    echo "  - Memory:  bounded (<1GB) — Loadfile-Only streams records lazily"
    echo "  - Seed:    $SEED (repeatable)"
    echo ""

    check_required_utilities
    check_disk_space
    check_system_resources
    confirm_execution

    run_stress_test
    validate_results || exit $?
    print_summary
}

# Check if script is being run directly
if [[ "${BASH_SOURCE[0]}" == "${0}" ]]; then
    main "$@"
fi
