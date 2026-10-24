#!/bin/bash
# E2E / regression tests for tests/perf/measure.sh and perf-guard / baseline-refresh integrations.
# Enforces Issue #826: Fail performance measurement when the generation command exits nonzero.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
MEASURE_SCRIPT="$REPO_ROOT/tests/perf/measure.sh"

PASSED=0
FAILED=0
TEMP_DIR="./results/test-perf-measure-$$"
mkdir -p "$TEMP_DIR"

function print_info() { local msg="$1"; echo -e "\033[44m[ INFO ]\033[0m $msg"; }
function print_success() { local msg="$1"; echo -e "\033[42m[ SUCCESS ]\033[0m $msg"; }
function print_error() { local msg="$1"; echo -e "\033[41m[ ERROR ]\033[0m $msg" >&2; }

cleanup() {
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

# --- Setup Mock Time and Fake CLIs ---

# Mock GNU time: handles -o <file> -f '%e %M' <cmd> <args...>
# Invokes <cmd> <args...>, writes '%e %M' to <file>, exits with <cmd>'s exit code.
MOCK_TIME="$TEMP_DIR/mock_time"
cat <<'EOF' > "$MOCK_TIME"
#!/bin/bash
time_file=""
format=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        -o|--output)
            time_file="$2"
            shift 2
            ;;
        -f|--format)
            format="$2"
            shift 2
            ;;
        -q|--quiet)
            shift
            ;;
        --)
            shift
            break
            ;;
        *)
            break
            ;;
    esac
done

if [[ $# -eq 0 ]]; then
    echo "mock_time: missing command" >&2
    exit 1
fi

exit_code=0
"$@" || exit_code=$?

if [[ -n "$time_file" ]]; then
    echo "0.05 10240" > "$time_file"
fi

exit $exit_code
EOF
chmod +x "$MOCK_TIME"

# Mock GNU time with malformed output (for testing timing parsing failure)
MOCK_TIME_MALFORMED="$TEMP_DIR/mock_time_malformed"
cat <<'EOF' > "$MOCK_TIME_MALFORMED"
#!/bin/bash
time_file=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        -o|--output)
            time_file="$2"
            shift 2
            ;;
        -f|--format)
            shift 2
            ;;
        -q|--quiet)
            shift
            ;;
        --)
            shift
            break
            ;;
        *)
            break
            ;;
    esac
done

exit_code=0
"$@" || exit_code=$?

if [[ -n "$time_file" ]]; then
    echo "invalid_output_no_rss" > "$time_file"
fi

exit $exit_code
EOF
chmod +x "$MOCK_TIME_MALFORMED"

# Mock GNU time with missing/empty output
MOCK_TIME_EMPTY="$TEMP_DIR/mock_time_empty"
cat <<'EOF' > "$MOCK_TIME_EMPTY"
#!/bin/bash
time_file=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        -o|--output)
            time_file="$2"
            shift 2
            ;;
        -f|--format)
            shift 2
            ;;
        -q|--quiet)
            shift
            ;;
        --)
            shift
            break
            ;;
        *)
            break
            ;;
    esac
done

exit_code=0
"$@" || exit_code=$?

# Deliberately do not write to time_file
exit $exit_code
EOF
chmod +x "$MOCK_TIME_EMPTY"

# Fake CLI exiting 7 with stderr diagnostics
FAKE_CLI_EXIT7="$TEMP_DIR/fake_cli_exit7"
cat <<'EOF' > "$FAKE_CLI_EXIT7"
#!/bin/bash
echo "Fatal: storage failure during generation" >&2
exit 7
EOF
chmod +x "$FAKE_CLI_EXIT7"

# Fake CLI succeeding with 0
FAKE_CLI_SUCCESS="$TEMP_DIR/fake_cli_success"
cat <<'EOF' > "$FAKE_CLI_SUCCESS"
#!/bin/bash
# Mock generation: write dummy files if --output-path provided
while [[ $# -gt 0 ]]; do
    case "$1" in
        --output-path)
            out_path="$2"
            mkdir -p "$out_path"
            touch "$out_path/archive.zip"
            shift 2
            ;;
        *)
            shift
            ;;
    esac
done
exit 0
EOF
chmod +x "$FAKE_CLI_SUCCESS"

print_info "=== Performance Measurement Regression Tests (Issue #826) ==="

# --- Test Case 1: /bin/false must fail nonzero and produce no JSON report ---
print_info "Test Case 1: /bin/false exits nonzero and emits no JSON report"
t1_out="$TEMP_DIR/t1.out"
t1_err="$TEMP_DIR/t1.err"
t1_exit=0
TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" /bin/false > "$t1_out" 2> "$t1_err" || t1_exit=$?

if [[ $t1_exit -eq 0 ]]; then
    print_error "Test 1 FAILED: Expected nonzero exit code for /bin/false, got 0"
    FAILED=$((FAILED + 1))
elif grep -q "pdf_50k" "$t1_out" 2>/dev/null; then
    print_error "Test 1 FAILED: /bin/false produced measurement JSON on stdout:"
    cat "$t1_out"
    FAILED=$((FAILED + 1))
else
    print_info "PASS: /bin/false exited with status $t1_exit and produced no JSON"
    PASSED=$((PASSED + 1))
fi

# --- Test Case 2: CLI emitting stderr and exiting 7 must fail nonzero, preserve stderr, produce no JSON ---
print_info "Test Case 2: CLI exiting 7 preserves stderr diagnostics and emits no JSON"
t2_out="$TEMP_DIR/t2.out"
t2_err="$TEMP_DIR/t2.err"
t2_exit=0
TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" "$FAKE_CLI_EXIT7" > "$t2_out" 2> "$t2_err" || t2_exit=$?

if [[ $t2_exit -eq 0 ]]; then
    print_error "Test 2 FAILED: Expected nonzero exit code for exit-7 CLI, got 0"
    FAILED=$((FAILED + 1))
elif grep -q "pdf_50k" "$t2_out" 2>/dev/null; then
    print_error "Test 2 FAILED: exit-7 CLI produced measurement JSON on stdout:"
    cat "$t2_out"
    FAILED=$((FAILED + 1))
elif ! grep -q "Fatal: storage failure during generation" "$t2_err"; then
    print_error "Test 2 FAILED: stderr diagnostics were not preserved in stderr. Stderr was:"
    cat "$t2_err"
    FAILED=$((FAILED + 1))
else
    print_info "PASS: exit-7 CLI exited with status $t2_exit, preserved stderr diagnostics, and produced no JSON"
    PASSED=$((PASSED + 1))
fi

# --- Test Case 3: Malformed/missing timing output must fail nonzero and distinguish timing error ---
print_info "Test Case 3: Malformed or missing timing output fails with diagnostic"
t3_out="$TEMP_DIR/t3.out"
t3_err="$TEMP_DIR/t3.err"
t3_exit=0
TIME_CMD="$MOCK_TIME_MALFORMED" bash "$MEASURE_SCRIPT" "$FAKE_CLI_SUCCESS" > "$t3_out" 2> "$t3_err" || t3_exit=$?

if [[ $t3_exit -eq 0 ]]; then
    print_error "Test 3 FAILED: Expected nonzero exit code for malformed timing, got 0"
    FAILED=$((FAILED + 1))
elif grep -q "pdf_50k" "$t3_out" 2>/dev/null; then
    print_error "Test 3 FAILED: malformed timing produced measurement JSON on stdout:"
    cat "$t3_out"
    FAILED=$((FAILED + 1))
elif ! grep -qiE "timing|malformed" "$t3_err"; then
    print_error "Test 3 FAILED: stderr did not distinguish timing failure. Stderr was:"
    cat "$t3_err"
    FAILED=$((FAILED + 1))
else
    print_info "PASS: Malformed timing exited with status $t3_exit and distinguished timing failure"
    PASSED=$((PASSED + 1))
fi

# --- Test Case 3b: Empty timing output must fail nonzero ---
print_info "Test Case 3b: Missing/empty timing output fails nonzero"
t3b_out="$TEMP_DIR/t3b.out"
t3b_err="$TEMP_DIR/t3b.err"
t3b_exit=0
TIME_CMD="$MOCK_TIME_EMPTY" bash "$MEASURE_SCRIPT" "$FAKE_CLI_SUCCESS" > "$t3b_out" 2> "$t3b_err" || t3b_exit=$?

if [[ $t3b_exit -eq 0 ]]; then
    print_error "Test 3b FAILED: Expected nonzero exit code for empty timing, got 0"
    FAILED=$((FAILED + 1))
elif grep -q "pdf_50k" "$t3b_out" 2>/dev/null; then
    print_error "Test 3b FAILED: empty timing produced measurement JSON on stdout"
    FAILED=$((FAILED + 1))
else
    print_info "PASS: Empty timing exited with status $t3b_exit"
    PASSED=$((PASSED + 1))
fi

# --- Test Case 4: Cleanup of scenario directory on success and failure ---
print_info "Test Case 4: Temporary scenario directory cleanup on failure"
# Count /tmp/tmp.* dirs before and after running failing command
before_tmp_count=$(find /tmp -maxdepth 1 -name "tmp.*" 2>/dev/null | wc -l)
TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" "$FAKE_CLI_EXIT7" >/dev/null 2>&1 || true
after_tmp_count=$(find /tmp -maxdepth 1 -name "tmp.*" 2>/dev/null | wc -l)

if [[ "$after_tmp_count" -gt "$before_tmp_count" ]]; then
    print_error "Test 4 FAILED: Temporary scenario directories leaked on failure (before: $before_tmp_count, after: $after_tmp_count)"
    FAILED=$((FAILED + 1))
else
    print_info "PASS: No temporary directories leaked on scenario failure"
    PASSED=$((PASSED + 1))
fi

# --- Test Case 5: Successful tiny fake CLI yields valid numeric JSON for all scenarios ---
print_info "Test Case 5: Successful fake CLI produces valid numeric JSON for all scenarios"
t5_out="$TEMP_DIR/t5.out"
t5_err="$TEMP_DIR/t5.err"
t5_exit=0
TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" "$FAKE_CLI_SUCCESS" > "$t5_out" 2> "$t5_err" || t5_exit=$?

if [[ $t5_exit -ne 0 ]]; then
    print_error "Test 5 FAILED: Expected 0 exit code for fake_cli_success, got $t5_exit. Stderr:"
    cat "$t5_err"
    FAILED=$((FAILED + 1))
else
    # Validate JSON structure and values
    valid_json=0
    python3 - <<'PYCHECK' "$t5_out" || valid_json=$?
import json, sys

path = sys.argv[1]
with open(path) as f:
    data = json.load(f)

for sc in ["pdf_50k", "eml_20k", "loadfile_200k"]:
    if sc not in data:
        raise ValueError(f"Missing scenario {sc}")
    w = data[sc]["wall_s"]
    r = data[sc]["rss_kb"]
    if not isinstance(w, (int, float)) or not isinstance(r, int):
        raise TypeError(f"Invalid types for {sc}: wall_s={w} ({type(w)}), rss_kb={r} ({type(r)})")

PYCHECK
    if [[ $valid_json -ne 0 ]]; then
        print_error "Test 5 FAILED: Emitted JSON invalid or non-numeric"
        cat "$t5_out"
        FAILED=$((FAILED + 1))
    else
        print_info "PASS: Successful fake CLI produced valid numeric JSON for all scenarios"
        PASSED=$((PASSED + 1))
    fi
fi

# --- Test Case 6: Perf Guard integration test with broken binary ---
print_info "Test Case 6: Perf Guard integration test rejects broken binary"
pg_dir="$TEMP_DIR/perf_guard_sim"
mkdir -p "$pg_dir/results"

pg_failed=0
for i in $(seq 1 5); do
    if ! ( TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" "$FAKE_CLI_EXIT7" > "$pg_dir/results/run_${i}.json" 2>/dev/null ); then
        pg_failed=1
        break
    fi
done

if [[ $pg_failed -eq 1 ]]; then
    print_info "PASS: Perf Guard measurement loop halts on failing CLI"
    PASSED=$((PASSED + 1))
else
    print_error "Test 6 FAILED: Perf Guard loop did not fail on broken CLI"
    FAILED=$((FAILED + 1))
fi

# --- Test Case 7: Baseline refresh integration test cannot accept failed runs as baselines ---
print_info "Test Case 7: Baseline refresh integration test rejects broken binary"
br_dir="$TEMP_DIR/baseline_refresh_sim"
mkdir -p "$br_dir/results"
cp "$REPO_ROOT/tests/perf/baselines.json" "$br_dir/baselines_original.json"
cp "$REPO_ROOT/tests/perf/baselines.json" "$br_dir/baselines.json"

br_failed=0
for i in $(seq 1 5); do
    if ! ( TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" /bin/false > "$br_dir/results/run_${i}.json" 2>/dev/null ); then
        br_failed=1
        break
    fi
done

if [[ $br_failed -eq 1 ]]; then
    # Verify baselines.json was NOT modified
    if cmp -s "$br_dir/baselines_original.json" "$br_dir/baselines.json"; then
        print_info "PASS: Baseline refresh halted on failed CLI and baselines.json was untouched"
        PASSED=$((PASSED + 1))
    else
        print_error "Test 7 FAILED: baselines.json was modified despite failure"
        FAILED=$((FAILED + 1))
    fi
else
    print_error "Test 7 FAILED: Baseline refresh loop did not fail on broken CLI"
    FAILED=$((FAILED + 1))
fi

# --- Setup Fake CLI that records arguments ---
FAKE_CLI_LOG_ARGS="$TEMP_DIR/fake_cli_log_args"
cat <<'EOF' > "$FAKE_CLI_LOG_ARGS"
#!/bin/bash
if [[ -n "${ARGS_LOG_FILE:-}" ]]; then
    echo "$@" >> "$ARGS_LOG_FILE"
fi
while [[ $# -gt 0 ]]; do
    case "$1" in
        --output-path)
            out_path="$2"
            mkdir -p "$out_path"
            touch "$out_path/archive.zip"
            shift 2
            ;;
        *)
            shift
            ;;
    esac
done
exit 0
EOF
chmod +x "$FAKE_CLI_LOG_ARGS"

# --- Test Case 8: --seed flag is accepted and forwarded to all scenarios ---
print_info "Test Case 8: --seed flag is accepted and forwarded to all scenarios"
t8_out="$TEMP_DIR/t8.out"
t8_err="$TEMP_DIR/t8.err"
t8_log="$TEMP_DIR/t8.args.log"
t8_exit=0
ARGS_LOG_FILE="$t8_log" TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" "$FAKE_CLI_LOG_ARGS" --seed 123 > "$t8_out" 2> "$t8_err" || t8_exit=$?

if [[ $t8_exit -ne 0 ]]; then
    print_error "Test 8 FAILED: Expected 0 exit code with --seed 123, got $t8_exit. Stderr:"
    cat "$t8_err"
    FAILED=$((FAILED + 1))
else
    seed_count=$(grep -c -- "--seed 123" "$t8_log" 2>/dev/null || true)
    if [[ "$seed_count" -ne 3 ]]; then
        print_error "Test 8 FAILED: Expected --seed 123 forwarded to all 3 scenarios, found $seed_count times. Args log:"
        cat "$t8_log"
        FAILED=$((FAILED + 1))
    else
        print_info "PASS: --seed 123 forwarded to all 3 scenarios"
        PASSED=$((PASSED + 1))
    fi
fi

# --- Test Case 9: Default seed 42 is forwarded when no --seed is specified ---
print_info "Test Case 9: Default seed 42 is forwarded when no --seed is specified"
t9_out="$TEMP_DIR/t9.out"
t9_err="$TEMP_DIR/t9.err"
t9_log="$TEMP_DIR/t9.args.log"
t9_exit=0
env -u PERF_SEED ARGS_LOG_FILE="$t9_log" TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" "$FAKE_CLI_LOG_ARGS" > "$t9_out" 2> "$t9_err" || t9_exit=$?

if [[ $t9_exit -ne 0 ]]; then
    print_error "Test 9 FAILED: Expected 0 exit code without --seed, got $t9_exit. Stderr:"
    cat "$t9_err"
    FAILED=$((FAILED + 1))
else
    seed_count=$(grep -c -- "--seed 42" "$t9_log" 2>/dev/null || true)
    if [[ "$seed_count" -ne 3 ]]; then
        print_error "Test 9 FAILED: Expected default --seed 42 forwarded to all 3 scenarios, found $seed_count times. Args log:"
        cat "$t9_log"
        FAILED=$((FAILED + 1))
    else
        print_info "PASS: Default --seed 42 forwarded to all 3 scenarios"
        PASSED=$((PASSED + 1))
    fi
fi

# --- Test Case 10: Invalid --seed fails nonzero with diagnostic ---
print_info "Test Case 10: Invalid --seed fails nonzero with diagnostic"
t10_out="$TEMP_DIR/t10.out"
t10_err="$TEMP_DIR/t10.err"
t10_exit=0
TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" "$FAKE_CLI_LOG_ARGS" --seed notanumber > "$t10_out" 2> "$t10_err" || t10_exit=$?

if [[ $t10_exit -eq 0 ]]; then
    print_error "Test 10 FAILED: Expected nonzero exit code for --seed notanumber, got 0"
    FAILED=$((FAILED + 1))
elif ! grep -qiE "seed|integer" "$t10_err"; then
    print_error "Test 10 FAILED: Expected error diagnostic for invalid seed. Stderr was:"
    cat "$t10_err"
    FAILED=$((FAILED + 1))
else
    print_info "PASS: Invalid --seed failed with diagnostic"
    PASSED=$((PASSED + 1))
fi

# --- Test Case 11: PERF_SEED environment variable is used when --seed is not passed ---
print_info "Test Case 11: PERF_SEED environment variable is forwarded to all scenarios"
t11_out="$TEMP_DIR/t11.out"
t11_err="$TEMP_DIR/t11.err"
t11_log="$TEMP_DIR/t11.args.log"
t11_exit=0
PERF_SEED=789 ARGS_LOG_FILE="$t11_log" TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" "$FAKE_CLI_LOG_ARGS" > "$t11_out" 2> "$t11_err" || t11_exit=$?

if [[ $t11_exit -ne 0 ]]; then
    print_error "Test 11 FAILED: Expected 0 exit code with PERF_SEED=789, got $t11_exit. Stderr:"
    cat "$t11_err"
    FAILED=$((FAILED + 1))
else
    seed_count=$(grep -c -- "--seed 789" "$t11_log" 2>/dev/null || true)
    if [[ "$seed_count" -ne 3 ]]; then
        print_error "Test 11 FAILED: Expected PERF_SEED=789 forwarded to all 3 scenarios, found $seed_count times. Args log:"
        cat "$t11_log"
        FAILED=$((FAILED + 1))
    else
        print_info "PASS: PERF_SEED=789 forwarded to all 3 scenarios"
        PASSED=$((PASSED + 1))
    fi
fi

# --- Test Case 12: Invalid PERF_SEED fails nonzero with diagnostic ---
print_info "Test Case 12: Invalid PERF_SEED fails nonzero with diagnostic"
t12_out="$TEMP_DIR/t12.out"
t12_err="$TEMP_DIR/t12.err"
t12_exit=0
PERF_SEED="notanumber" TIME_CMD="$MOCK_TIME" bash "$MEASURE_SCRIPT" "$FAKE_CLI_LOG_ARGS" > "$t12_out" 2> "$t12_err" || t12_exit=$?

if [[ $t12_exit -eq 0 ]]; then
    print_error "Test 12 FAILED: Expected nonzero exit code for invalid PERF_SEED, got 0"
    FAILED=$((FAILED + 1))
elif ! grep -qiE "seed|integer" "$t12_err"; then
    print_error "Test 12 FAILED: Expected error diagnostic for invalid PERF_SEED. Stderr was:"
    cat "$t12_err"
    FAILED=$((FAILED + 1))
else
    print_info "PASS: Invalid PERF_SEED failed with diagnostic"
    PASSED=$((PASSED + 1))
fi

# --- Summary ---
echo ""
TOTAL=$((PASSED + FAILED))
if [[ "$FAILED" -eq 0 ]]; then
    print_success "All performance measurement regression tests passed! ($PASSED/$TOTAL)"
    exit 0
else
    print_error "Performance measurement regression tests: $FAILED/$TOTAL FAILED"
    exit 1
fi
