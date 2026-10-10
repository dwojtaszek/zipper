#!/bin/bash
# =============================================================================
# ZIPPER STRESS TEST HARNESS REGRESSION TESTS (Issue #1114)
# =============================================================================
# Verifies selector dispatch, child failure propagation, contract validation,
# run-scoped isolation, set -u summary execution, and bounded-memory checks.
# Cheap regressions suitable for CI (no large workloads executed).
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
STRESS_DIR="$REPO_ROOT/tests/stress"

PASSED=0
FAILED=0
SANDBOX="$(mktemp -d)"

cleanup() {
    rm -rf "$SANDBOX"
}
trap cleanup EXIT

function print_info() { local msg="$1"; echo -e "\033[44m[ INFO ]\033[0m $msg"; }
function print_success() { local msg="$1"; echo -e "\033[42m[ SUCCESS ]\033[0m $msg"; }
function print_error() { local msg="$1"; echo -e "\033[41m[ ERROR ]\033[0m $msg" >&2; }

pass() { local msg="$1"; print_info "PASS: $msg"; PASSED=$((PASSED + 1)); }
fail() { local msg="$1"; print_error "FAIL: $msg"; FAILED=$((FAILED + 1)); }

print_info "=== Stress Test Harness Regressions (Issue #1114) ==="

# -----------------------------------------------------------------------------
# Test 1: Selector dispatch - missing selection must fail nonzero
# -----------------------------------------------------------------------------
print_info "Test 1: run-stress-tests.sh fails nonzero when selection is missing"
t1_out="$SANDBOX/t1.out"
t1_exit=0
bash "$STRESS_DIR/run-stress-tests.sh" > "$t1_out" 2>&1 || t1_exit=$?

if [[ $t1_exit -ne 0 ]] && grep -Fq "No stress test selector specified. Provide a test selector" "$t1_out"; then
    pass "missing selector failed with a diagnostic and nonzero exit ($t1_exit)"
else
    fail "missing selector did not emit its diagnostic and fail nonzero (exit: $t1_exit, out: $(cat "$t1_out"))"
fi

# -----------------------------------------------------------------------------
# Test 2: Selector dispatch - unknown selection must fail nonzero
# -----------------------------------------------------------------------------
print_info "Test 2: run-stress-tests.sh fails nonzero for unknown test name"
t2_out="$SANDBOX/t2.out"
t2_exit=0
STRESS_SKIP_SYSCHECK=1 bash "$STRESS_DIR/run-stress-tests.sh" "nonexistent_test_xyz" > "$t2_out" 2>&1 || t2_exit=$?

if [[ $t2_exit -ne 0 ]] && grep -Fq "No stress test found matching 'nonexistent_test_xyz'" "$t2_out"; then
    pass "unknown selector failed with a diagnostic and nonzero exit ($t2_exit)"
else
    fail "unknown selector did not emit its diagnostic and fail nonzero (exit: $t2_exit, out: $(cat "$t2_out"))"
fi

# -----------------------------------------------------------------------------
# Test 3: Selector dispatch - selecting 100m dispatches only that script
# -----------------------------------------------------------------------------
print_info "Test 3: Selecting '100m' dispatches only stress-100m-loadfile.sh"
t3_box="$SANDBOX/dispatch_test"
mkdir -p "$t3_box"
cp "$STRESS_DIR/run-stress-tests.sh" "$t3_box/run-stress-tests.sh"

# Mock all 4 stress child scripts to record their execution into a log file
cat <<'EOF' > "$t3_box/stress-10gb-filecount.sh"
#!/bin/bash
echo "RAN:10gb" >> "$DISPATCH_LOG"
exit 0
EOF
chmod +x "$t3_box/stress-10gb-filecount.sh"

cat <<'EOF' > "$t3_box/stress-30gb-attachments.sh"
#!/bin/bash
echo "RAN:30gb" >> "$DISPATCH_LOG"
exit 0
EOF
chmod +x "$t3_box/stress-30gb-attachments.sh"

cat <<'EOF' > "$t3_box/stress-large-loadfile.sh"
#!/bin/bash
echo "RAN:large" >> "$DISPATCH_LOG"
exit 0
EOF
chmod +x "$t3_box/stress-large-loadfile.sh"

cat <<'EOF' > "$t3_box/stress-100m-loadfile.sh"
#!/bin/bash
echo "RAN:100m" >> "$DISPATCH_LOG"
exit 0
EOF
chmod +x "$t3_box/stress-100m-loadfile.sh"

t3_log="$SANDBOX/dispatch.log"
touch "$t3_log"

t3_out="$SANDBOX/t3.out"
t3_exit=0
(cd "$t3_box" && DISPATCH_LOG="$t3_log" STRESS_SKIP_SYSCHECK=1 bash "./run-stress-tests.sh" 100m > "$t3_out" 2>&1) || t3_exit=$?

if [[ $t3_exit -eq 0 ]] && grep -q "RAN:100m" "$t3_log" && ! grep -q -E "RAN:(10gb|30gb|large)" "$t3_log"; then
    pass "selecting '100m' dispatched only stress-100m-loadfile.sh without fan-out"
else
    fail "selecting '100m' failed or dispatched unexpected scripts (exit: $t3_exit, log: $(cat "$t3_log"))"
fi

# 3b: Selecting exact key '1' dispatches only stress-10gb-filecount.sh without ambiguity
t3b_log="$SANDBOX/dispatch_3b.log"
touch "$t3b_log"
t3b_out="$SANDBOX/t3b.out"
t3b_exit=0
(cd "$t3_box" && DISPATCH_LOG="$t3b_log" STRESS_SKIP_SYSCHECK=1 bash "./run-stress-tests.sh" 1 > "$t3b_out" 2>&1) || t3b_exit=$?

if [[ $t3b_exit -eq 0 ]] && grep -Fqx "RAN:10gb" "$t3b_log" && [[ "$(wc -l < "$t3b_log")" -eq 1 ]]; then
    pass "selecting exact key '1' dispatched only stress-10gb-filecount.sh without ambiguity"
else
    fail "selecting exact key '1' failed or dispatched unexpected scripts (exit: $t3b_exit, log: $(cat "$t3b_log"), out: $(cat "$t3b_out"))"
fi

# 3c: Every other exact numeric key dispatches only its associated script
for selector_case in "3:30gb" "4:large" "5:100m"; do
    key="${selector_case%%:*}"
    expected="${selector_case#*:}"
    key_log="$SANDBOX/dispatch_key_${key}.log"
    key_out="$SANDBOX/dispatch_key_${key}.out"
    : > "$key_log"
    key_exit=0
    (cd "$t3_box" && DISPATCH_LOG="$key_log" STRESS_SKIP_SYSCHECK=1 bash "./run-stress-tests.sh" "$key" > "$key_out" 2>&1) || key_exit=$?

    if [[ $key_exit -eq 0 ]] && grep -Fqx "RAN:$expected" "$key_log" && [[ "$(wc -l < "$key_log")" -eq 1 ]]; then
        pass "selecting exact key '$key' dispatched only its associated workload"
    else
        fail "selecting exact key '$key' failed or dispatched unexpected scripts (exit: $key_exit, log: $(cat "$key_log"), out: $(cat "$key_out"))"
    fi
done

# 3d: The all selector dispatches every workload in table order
t3all_log="$SANDBOX/dispatch_all.log"
t3all_out="$SANDBOX/t3all.out"
: > "$t3all_log"
t3all_exit=0
(cd "$t3_box" && DISPATCH_LOG="$t3all_log" STRESS_SKIP_SYSCHECK=1 bash "./run-stress-tests.sh" all > "$t3all_out" 2>&1) || t3all_exit=$?
t3all_actual=$(<"$t3all_log")
t3all_expected=$'RAN:10gb\nRAN:30gb\nRAN:large\nRAN:100m'

if [[ $t3all_exit -eq 0 && "$t3all_actual" == "$t3all_expected" ]]; then
    pass "selecting 'all' dispatched every workload in table order"
else
    fail "selecting 'all' failed or dispatched workloads out of table order (exit: $t3all_exit, log: $t3all_actual, out: $(cat "$t3all_out"))"
fi

# -----------------------------------------------------------------------------
# Test 4: Missing child script fails nonzero
# -----------------------------------------------------------------------------
print_info "Test 4: run-stress-tests.sh fails nonzero when child script is missing"
t4_box="$SANDBOX/missing_child_test"
mkdir -p "$t4_box"
cp "$STRESS_DIR/run-stress-tests.sh" "$t4_box/run-stress-tests.sh"
# Do not create stress-100m-loadfile.sh

t4_out="$SANDBOX/t4.out"
t4_exit=0
(cd "$t4_box" && STRESS_SKIP_SYSCHECK=1 bash "./run-stress-tests.sh" 100m > "$t4_out" 2>&1) || t4_exit=$?

if [[ $t4_exit -ne 0 ]]; then
    pass "missing child script failed nonzero ($t4_exit)"
else
    fail "missing child script unexpectedly returned 0"
fi

# -----------------------------------------------------------------------------
# Test 5: Child failure propagation
# -----------------------------------------------------------------------------
print_info "Test 5: run-stress-tests.sh propagates child failure exit code"
t5_box="$SANDBOX/child_fail_test"
mkdir -p "$t5_box"
cp "$STRESS_DIR/run-stress-tests.sh" "$t5_box/run-stress-tests.sh"
cat <<'EOF' > "$t5_box/stress-100m-loadfile.sh"
#!/bin/bash
exit 2
EOF
chmod +x "$t5_box/stress-100m-loadfile.sh"

t5_out="$SANDBOX/t5.out"
t5_exit=0
(cd "$t5_box" && STRESS_SKIP_SYSCHECK=1 bash "./run-stress-tests.sh" 100m > "$t5_out" 2>&1) || t5_exit=$?

if [[ $t5_exit -eq 2 ]]; then
    pass "child failure exit code 2 was propagated directly"
elif [[ $t5_exit -ne 0 ]]; then
    pass "child failure failed nonzero ($t5_exit)"
else
    fail "child failure unexpectedly returned 0"
fi

# -----------------------------------------------------------------------------
# Test 6: Reproduced wrong-count output fails in stress-large-loadfile.sh
# -----------------------------------------------------------------------------
print_info "Test 6: stress-large-loadfile.sh fails on incorrect line count (reproduction)"
t6_dir="$SANDBOX/large_wrong_count"
mkdir -p "$t6_dir/scenario1_external_utf8"
mkdir -p "$t6_dir/scenario3_external_utf16"
mkdir -p "$t6_dir/scenario4_external_ansi"

# Expected: FILE_COUNT + 1 = 6 lines. Create files with 1 line.
printf "DOC00000001\n" > "$t6_dir/scenario1_external_utf8/loadfile.dat"
printf "DOC00000001\r\n" | iconv -f UTF-8 -t UTF-16LE > "$t6_dir/scenario3_external_utf16/loadfile.dat"
printf "DOC00000001\r\n" | iconv -f UTF-8 -t WINDOWS-1252 > "$t6_dir/scenario4_external_ansi/loadfile.dat"

t6_exit=0
t6_out="$SANDBOX/t6.out"
bash -c "
    OUTPUT_DIR='$t6_dir'
    FILE_COUNT=5
    source '$STRESS_DIR/stress-large-loadfile.sh'
    analyze_performance
" > "$t6_out" 2>&1 || t6_exit=$?

if [[ $t6_exit -ne 0 ]]; then
    pass "wrong-count reproduction failed nonzero as expected (exit: $t6_exit)"
else
    fail "wrong-count reproduction returned 0 (contract failure was swallowed)"
fi

# -----------------------------------------------------------------------------
# Test 7: Missing output fails in stress-large-loadfile.sh
# -----------------------------------------------------------------------------
print_info "Test 7: stress-large-loadfile.sh fails on missing output file"
t7_dir="$SANDBOX/large_missing_output"
mkdir -p "$t7_dir/scenario1_external_utf8"
# scenario 3 and 4 missing

printf "DOC00000001\n" > "$t7_dir/scenario1_external_utf8/loadfile.dat"

t7_exit=0
t7_out="$SANDBOX/t7.out"
bash -c "
    OUTPUT_DIR='$t7_dir'
    FILE_COUNT=5
    source '$STRESS_DIR/stress-large-loadfile.sh'
    analyze_performance
" > "$t7_out" 2>&1 || t7_exit=$?

if [[ $t7_exit -ne 0 ]]; then
    pass "missing scenario output failed nonzero as expected (exit: $t7_exit)"
else
    fail "missing scenario output unexpectedly returned 0"
fi

# -----------------------------------------------------------------------------
# Test 8: Valid decoded counts pass in stress-large-loadfile.sh
# -----------------------------------------------------------------------------
print_info "Test 8: stress-large-loadfile.sh passes on valid decoded line counts"
t8_dir="$SANDBOX/large_valid_count"
mkdir -p "$t8_dir/scenario1_external_utf8"
mkdir -p "$t8_dir/scenario3_external_utf16"
mkdir -p "$t8_dir/scenario4_external_ansi"

# 6 lines each: header + 5 records
for i in {1..6}; do echo "Line $i"; done > "$t8_dir/scenario1_external_utf8/loadfile.dat"
(for i in {1..6}; do echo "Line $i"; done) | iconv -f UTF-8 -t UTF-16LE > "$t8_dir/scenario3_external_utf16/loadfile.dat"
(for i in {1..6}; do echo "Line $i"; done) | iconv -f UTF-8 -t WINDOWS-1252 > "$t8_dir/scenario4_external_ansi/loadfile.dat"

t8_exit=0
t8_out="$SANDBOX/t8.out"
bash -c "
    OUTPUT_DIR='$t8_dir'
    FILE_COUNT=5
    source '$STRESS_DIR/stress-large-loadfile.sh'
    analyze_performance
" > "$t8_out" 2>&1 || t8_exit=$?

if [[ $t8_exit -eq 0 ]]; then
    pass "valid decoded line counts passed with exit 0"
else
    fail "valid line count failed unexpectedly (exit: $t8_exit, out: $(cat "$t8_out"))"
fi

# -----------------------------------------------------------------------------
# Test 9: All summaries execute under set -u without unbound variable errors
# -----------------------------------------------------------------------------
print_info "Test 9: All summaries execute under set -u"

# 9a: stress-large-loadfile.sh
t9a_exit=0
bash -u -c "
    source '$STRESS_DIR/stress-large-loadfile.sh'
    cleanup_and_summary > /dev/null
" 2>&1 || t9a_exit=$?

if [[ $t9a_exit -eq 0 ]]; then
    pass "stress-large-loadfile.sh cleanup_and_summary executed under set -u"
else
    fail "stress-large-loadfile.sh cleanup_and_summary failed under set -u ($t9a_exit)"
fi

# 9b: stress-10gb-filecount.sh
t9b_exit=0
bash -u -c "
    source '$STRESS_DIR/stress-10gb-filecount.sh'
    cleanup_and_summary > /dev/null
" 2>&1 || t9b_exit=$?

if [[ $t9b_exit -eq 0 ]]; then
    pass "stress-10gb-filecount.sh cleanup_and_summary executed under set -u"
else
    fail "stress-10gb-filecount.sh cleanup_and_summary failed under set -u ($t9b_exit)"
fi

# 9c: stress-30gb-attachments.sh
t9c_exit=0
bash -u -c "
    source '$STRESS_DIR/stress-30gb-attachments.sh'
    cleanup_and_summary > /dev/null
" 2>&1 || t9c_exit=$?

if [[ $t9c_exit -eq 0 ]]; then
    pass "stress-30gb-attachments.sh cleanup_and_summary executed under set -u"
else
    fail "stress-30gb-attachments.sh cleanup_and_summary failed under set -u ($t9c_exit)"
fi

# 9d: stress-100m-loadfile.sh
t9d_exit=0
bash -u -c "
    DURATION=1
    source '$STRESS_DIR/stress-100m-loadfile.sh'
    print_summary > /dev/null
" 2>&1 || t9d_exit=$?

if [[ $t9d_exit -eq 0 ]]; then
    pass "stress-100m-loadfile.sh print_summary executed under set -u"
else
    fail "stress-100m-loadfile.sh print_summary failed under set -u ($t9d_exit)"
fi

# -----------------------------------------------------------------------------
# Test 10: Run-scoped output isolation (no stale artifact inspection)
# -----------------------------------------------------------------------------
print_info "Test 10: Run-scoped output isolation between invocations"

# Verify that two distinct invocations get distinct default OUTPUT_DIR paths
dir1=$(bash -c "source '$STRESS_DIR/stress-100m-loadfile.sh'; echo \"\$OUTPUT_DIR\"")
sleep 1
dir2=$(bash -c "source '$STRESS_DIR/stress-100m-loadfile.sh'; echo \"\$OUTPUT_DIR\"")

if [[ -n "$dir1" && -n "$dir2" && "$dir1" != "$dir2" ]]; then
    pass "consecutive invocations generated distinct run-scoped output directories"
else
    fail "consecutive invocations shared output directory: dir1='$dir1', dir2='$dir2'"
fi

# -----------------------------------------------------------------------------
# Test 11: Memory limit validation in stress-100m-loadfile.sh
# -----------------------------------------------------------------------------
print_info "Test 11: Memory limit budget enforcement"
t11_dir="$SANDBOX/mem_test"
mkdir -p "$t11_dir"

# Create a valid fixture DAT file (Control Number header + DOC00000001)
cat <<'EOF' > "$t11_dir/loadfile_test.dat"
"Control Number"þ"Doc ID"
"DOC00000001"þ"DOC00000001"
EOF

# 11a: Peak RSS within budget (250 MB = 256000 KB <= 1048576 KB)
cat <<'EOF' > "$t11_dir/.time-report.txt"
Command being timed: "zipper"
Maximum resident set size (kbytes): 256000
EOF

t11a_out="$SANDBOX/t11a.out"
t11a_exit=0
bash -c "
    OUTPUT_DIR='$t11_dir'
    FILE_COUNT=1
    source '$STRESS_DIR/stress-100m-loadfile.sh'
    validate_results
" > "$t11a_out" 2>&1 || t11a_exit=$?

if [[ $t11a_exit -eq 0 ]]; then
    pass "peak RSS within budget passed validation"
else
    fail "peak RSS within budget failed validation ($t11a_exit, out: $(cat "$t11a_out"))"
fi

# 11b: Peak RSS exceeding budget (1500 MB = 1536000 KB > 1048576 KB)
cat <<'EOF' > "$t11_dir/.time-report.txt"
Command being timed: "zipper"
Maximum resident set size (kbytes): 1536000
EOF

t11b_out="$SANDBOX/t11b.out"
t11b_exit=0
bash -c "
    OUTPUT_DIR='$t11_dir'
    FILE_COUNT=1
    source '$STRESS_DIR/stress-100m-loadfile.sh'
    validate_results
" > "$t11b_out" 2>&1 || t11b_exit=$?

if [[ $t11b_exit -eq 2 ]]; then
    pass "peak RSS exceeding budget failed with contract violation (exit: 2)"
elif [[ $t11b_exit -ne 0 ]]; then
    pass "peak RSS exceeding budget failed nonzero (exit: $t11b_exit)"
else
    fail "peak RSS exceeding budget unexpectedly passed with exit 0 (out: $(cat "$t11b_out"))"
fi

# 11c: Unmeasured memory states explicitly in summary
rm -f "$t11_dir/.time-report.txt"
t11c_out="$SANDBOX/t11c.out"
t11c_exit=0
bash -c "
    OUTPUT_DIR='$t11_dir'
    FILE_COUNT=1
    DURATION=1
    TIME_CMD=''
    source '$STRESS_DIR/stress-100m-loadfile.sh'
    print_summary
" > "$t11c_out" 2>&1 || t11c_exit=$?

if grep -q -i "not measured" "$t11c_out" || grep -q -i "not checked" "$t11c_out" || grep -q -i "unavailable" "$t11c_out"; then
    pass "summary explicitly stated memory was not measured"
else
    fail "summary failed to state memory was unmeasured: $(cat "$t11c_out")"
fi

# 11d: Missing memory measurement on full contract run fails contract validation
t11d_dir="$SANDBOX/t11d_mem"
mkdir -p "$t11d_dir"
cat <<'EOF' > "$t11d_dir/loadfile_test.dat"
"Control Number"þ"Doc ID"
"DOC00000001"þ"DOC00000001"
EOF
t11d_out="$SANDBOX/t11d.out"
t11d_exit=0
bash -c "
    OUTPUT_DIR='$t11d_dir'
    CONTRACT_COUNT=1
    FILE_COUNT=1
    rm -f '$t11d_dir/.time-report.txt'
    source '$STRESS_DIR/stress-100m-loadfile.sh'
    validate_results
" > "$t11d_out" 2>&1 || t11d_exit=$?

if [[ $t11d_exit -eq 2 ]]; then
    pass "missing memory measurement on full contract run failed with contract violation (exit: 2)"
else
    fail "missing memory measurement on full contract run did not fail with exit 2 (exit: $t11d_exit, out: $(cat "$t11d_out"))"
fi

# -----------------------------------------------------------------------------
# Test 12: Consecutive smoke runs isolate artifacts (no stale output inspected)
# -----------------------------------------------------------------------------
print_info "Test 12: Stale output is not inspected across consecutive runs"
t12_base="$SANDBOX/stale_test"
mkdir -p "$t12_base"

# Run 1 writes its own output
t12_run1="$t12_base/run1"
mkdir -p "$t12_run1"
cat <<'EOF' > "$t12_run1/loadfile_run1.dat"
"Control Number"þ"Doc ID"
"DOC00000001"þ"DOC00000001"
EOF

# Run 1 validates successfully on its own artifacts
t12_r1_out="$SANDBOX/t12_r1.out"
t12_r1_exit=0
bash -c "
    OUTPUT_DIR='$t12_run1'
    FILE_COUNT=1
    source '$STRESS_DIR/stress-100m-loadfile.sh'
    validate_results
" > "$t12_r1_out" 2>&1 || t12_r1_exit=$?

# Run 2 is in an empty directory (generation failed or incomplete)
t12_run2="$t12_base/run2"
mkdir -p "$t12_run2"

t12_r2_out="$SANDBOX/t12_r2.out"
t12_r2_exit=0
bash -c "
    OUTPUT_DIR='$t12_run2'
    FILE_COUNT=1
    source '$STRESS_DIR/stress-100m-loadfile.sh'
    validate_results
" > "$t12_r2_out" 2>&1 || t12_r2_exit=$?

if [[ $t12_r1_exit -eq 0 && $t12_r2_exit -ne 0 ]]; then
    pass "run 2 validated only its own artifacts and failed without inspecting run 1's stale files"
else
    fail "stale artifact isolation failed: run1_exit=$t12_r1_exit (out: $(cat "$t12_r1_out")), run2_exit=$t12_r2_exit (out: $(cat "$t12_r2_out"))"
fi

# -----------------------------------------------------------------------------
# Test 13: Built CLI binary measurement (no dotnet run overhead under time)
# -----------------------------------------------------------------------------
print_info "Test 13: Built CLI binary is executed directly under timing command"
t13_dir="$SANDBOX/cli_test"
mkdir -p "$t13_dir"

fake_bin="$t13_dir/fake_zipper"
cat <<'EOF' > "$fake_bin"
#!/bin/bash
echo "RAN_CLI:$*" > "$CLI_LOG"
# Write dummy loadfile output
out_dir=""
while [[ $# -gt 0 ]]; do
    case "$1" in
        --output-path)
            out_dir="$2"
            shift 2
            ;;
        *)
            shift
            ;;
    esac
done
if [ -n "$out_dir" ]; then
    mkdir -p "$out_dir"
    echo -e '"Control Number"\n"DOC00000001"' > "$out_dir/loadfile_2026.dat"
fi
exit 0
EOF
chmod +x "$fake_bin"

t13_log="$t13_dir/cli.log"
t13_exit=0
(
    export ZIPPER_BIN="$fake_bin"
    export CLI_LOG="$t13_log"
    export OUTPUT_DIR="$t13_dir/out"
    export FILE_COUNT=1
    export TIME_CMD=""
    source "$STRESS_DIR/stress-100m-loadfile.sh"
    run_stress_test > /dev/null 2>&1
) || t13_exit=$?

if [[ $t13_exit -eq 0 ]] && grep -q "RAN_CLI:--loadfile-only" "$t13_log"; then
    pass "run_stress_test invoked the built CLI binary directly"
else
    fail "run_stress_test did not invoke built CLI binary (exit: $t13_exit, log: $(cat "$t13_log" 2>/dev/null || true))"
fi

# -----------------------------------------------------------------------------
# Test 14: Ambiguous selector rejection without fan-out
# -----------------------------------------------------------------------------
print_info "Test 14: Ambiguous selector is rejected without fan-out"
t14_box="$SANDBOX/ambig_box"
mkdir -p "$t14_box"
cp "$STRESS_DIR/run-stress-tests.sh" "$t14_box/run-stress-tests.sh"
t14_log="$SANDBOX/ambig.log"
touch "$t14_log"

cat <<'EOF' > "$t14_box/stress-10gb-filecount.sh"
#!/bin/bash
echo "RAN:10gb" >> "$AMBIG_LOG"
exit 0
EOF
chmod +x "$t14_box/stress-10gb-filecount.sh"

cat <<'EOF' > "$t14_box/stress-large-loadfile.sh"
#!/bin/bash
echo "RAN:large" >> "$AMBIG_LOG"
exit 0
EOF
chmod +x "$t14_box/stress-large-loadfile.sh"

cat <<'EOF' > "$t14_box/stress-100m-loadfile.sh"
#!/bin/bash
echo "RAN:100m" >> "$AMBIG_LOG"
exit 0
EOF
chmod +x "$t14_box/stress-100m-loadfile.sh"

# 'loadfile' matches both stress-large-loadfile.sh and stress-100m-loadfile.sh
t14_exit=0
t14_out="$SANDBOX/t14.out"
(cd "$t14_box" && AMBIG_LOG="$t14_log" STRESS_SKIP_SYSCHECK=1 bash "./run-stress-tests.sh" "loadfile" > "$t14_out" 2>&1) || t14_exit=$?

if [[ $t14_exit -ne 0 ]] && [ ! -s "$t14_log" ]; then
    pass "ambiguous selector 'loadfile' failed nonzero without running any workload"
else
    fail "ambiguous selector did not fail or ran workload: exit=$t14_exit, log=$(cat "$t14_log")"
fi

# -----------------------------------------------------------------------------
# Test 15: Selector matching is case-insensitive
# -----------------------------------------------------------------------------
echo ""
print_info "Test 15: Uppercase selector dispatches case-insensitively"

t15_box="$SANDBOX/t15-box"
mkdir -p "$t15_box"
cp "$STRESS_DIR/run-stress-tests.sh" "$t15_box/run-stress-tests.sh"

cat <<'EOF' > "$t15_box/stress-10gb-filecount.sh"
#!/bin/bash
echo "RAN:10gb" >> "$CASE_LOG"
exit 0
EOF
chmod +x "$t15_box/stress-10gb-filecount.sh"

cat <<'EOF' > "$t15_box/stress-30gb-attachments.sh"
#!/bin/bash
echo "RAN:30gb" >> "$CASE_LOG"
exit 0
EOF
chmod +x "$t15_box/stress-30gb-attachments.sh"

cat <<'EOF' > "$t15_box/stress-large-loadfile.sh"
#!/bin/bash
echo "RAN:large" >> "$CASE_LOG"
exit 0
EOF
chmod +x "$t15_box/stress-large-loadfile.sh"

cat <<'EOF' > "$t15_box/stress-100m-loadfile.sh"
#!/bin/bash
echo "RAN:100m" >> "$CASE_LOG"
exit 0
EOF
chmod +x "$t15_box/stress-100m-loadfile.sh"

t15_log="$SANDBOX/case.log"
touch "$t15_log"

t15_out="$SANDBOX/t15.out"
t15_exit=0
(cd "$t15_box" && CASE_LOG="$t15_log" STRESS_SKIP_SYSCHECK=1 bash "./run-stress-tests.sh" "100M" > "$t15_out" 2>&1) || t15_exit=$?

if [[ $t15_exit -eq 0 ]] && grep -q "RAN:100m" "$t15_log" && ! grep -q -E "RAN:(10gb|30gb|large)" "$t15_log"; then
    pass "uppercase selector '100M' dispatched only stress-100m-loadfile.sh"
else
    fail "uppercase selector '100M' failed or dispatched unexpected scripts (exit: $t15_exit, log: $(cat "$t15_log"))"
fi

# -----------------------------------------------------------------------------
# Test 16: Selector text is printed literally
# -----------------------------------------------------------------------------
print_info "Test 16: Unknown selector text is printed without escape interpretation"
t16_selector='literal\cselector'
t16_display=$(printf '%q' "$t16_selector")
t16_out="$SANDBOX/t16.out"
t16_exit=0
STRESS_SKIP_SYSCHECK=1 bash "$STRESS_DIR/run-stress-tests.sh" "$t16_selector" > "$t16_out" 2>&1 || t16_exit=$?

if [[ $t16_exit -ne 0 ]] && grep -Fq "No stress test found matching '$t16_display'" "$t16_out"; then
    pass "unknown selector containing an escape sequence was printed safely"
else
    fail "unknown selector diagnostic was truncated or missing (exit: $t16_exit, out: $(cat "$t16_out"))"
fi

# -----------------------------------------------------------------------------
# Test 17: Failed free queries use explicit zero fallbacks
# -----------------------------------------------------------------------------
print_info "Test 17: Failed free queries use explicit zero fallbacks"
t17_bin="$SANDBOX/free_failure_bin"
mkdir -p "$t17_bin"
cat <<'EOF' > "$t17_bin/free"
#!/bin/bash
printf '%s\n' "$*" >> "$FREE_LOG"
printf 'Mem: 8192 4096 1024 0 3072 2048\n'
exit 1
EOF
chmod +x "$t17_bin/free"

assert_free_failure_fallback() {
    local name="$1" script="$2" function="$3" expected_exit="$4"
    local expected_message="$5" expected_calls="$6" expected_second_message="${7:-}"
    local free_log="$SANDBOX/free_${name}.log"
    local free_out="$SANDBOX/free_${name}.out"
    local free_exit=0

    FREE_LOG="$free_log" PATH="$t17_bin:$PATH" ZIPPER_BIN=/bin/bash STRESS_SKIP_SYSCHECK=0 \
        bash -c 'source "$1"; "$2"' _ "$script" "$function" > "$free_out" 2>&1 || free_exit=$?

    if [[ $free_exit -eq $expected_exit ]] &&
        grep -Fq "$expected_message" "$free_out" &&
        { [[ -z "$expected_second_message" ]] || grep -Fq "$expected_second_message" "$free_out"; } &&
        [[ "$(wc -l < "$free_log")" -eq $expected_calls ]]; then
        pass "$name used the fallback after free failed"
    else
        fail "$name did not use the expected fallback (exit: $free_exit, calls: $(wc -l < "$free_log"), out: $(cat "$free_out"))"
    fi
}

assert_free_failure_fallback "runner" "$STRESS_DIR/run-stress-tests.sh" check_system_requirements 0 "Available Memory: 0GB" 1
assert_free_failure_fallback "100m" "$STRESS_DIR/stress-100m-loadfile.sh" check_system_resources 1 "Less than 1GB available memory" 1
assert_free_failure_fallback "10gb" "$STRESS_DIR/stress-10gb-filecount.sh" check_system_resources 0 "Available memory: 0" 1
assert_free_failure_fallback "30gb" "$STRESS_DIR/stress-30gb-attachments.sh" check_system_resources 0 "Available memory: 0" 2 "Low memory detected"
t17_quoted_script_dir="$SANDBOX/path's"
mkdir -p "$t17_quoted_script_dir"
cp "$STRESS_DIR/stress-100m-loadfile.sh" "$t17_quoted_script_dir/stress-100m-loadfile.sh"
assert_free_failure_fallback "100m_quoted_path" "$t17_quoted_script_dir/stress-100m-loadfile.sh" check_system_resources 1 "Less than 1GB available memory" 1

# -----------------------------------------------------------------------------
# Test 18: All print helpers preserve messages
# -----------------------------------------------------------------------------
print_info "Test 18: Print helpers preserve their message arguments"
t18_out="$SANDBOX/t18.out"
bash -c 'source "$1"; print_warning "$2"; print_info "$3"; print_success "$4"; print_error "$5"; print_header "$6"' \
    _ "$STRESS_DIR/run-stress-tests.sh" warning-sentinel info-sentinel success-sentinel error-sentinel header-sentinel > "$t18_out" 2>&1

if grep -Fq "warning-sentinel" "$t18_out" &&
    grep -Fq "info-sentinel" "$t18_out" &&
    grep -Fq "success-sentinel" "$t18_out" &&
    grep -Fq "error-sentinel" "$t18_out" &&
    grep -Fq "header-sentinel" "$t18_out"; then
    pass "all print helpers included their message arguments"
else
    fail "one or more print helpers omitted their message argument: $(cat "$t18_out")"
fi

# -----------------------------------------------------------------------------
# Test 19: Selector messages cannot send terminal control sequences
# -----------------------------------------------------------------------------
print_info "Test 19: Selector control characters are removed from diagnostics"
t19_selector=$'\033[2J\033[H\007\177\2350;attacker-title\234'
t19_display=$(printf '%q' "$t19_selector")
t19_out="$SANDBOX/t19.out"
t19_exit=0
STRESS_SKIP_SYSCHECK=1 bash "$STRESS_DIR/run-stress-tests.sh" "$t19_selector" > "$t19_out" 2>&1 || t19_exit=$?

if [[ $t19_exit -ne 0 ]] &&
    grep -Fq "No stress test found matching '$t19_display'" "$t19_out" &&
    ! grep -Fq $'\033[2J' "$t19_out" &&
    ! grep -Fq $'\033[H' "$t19_out" &&
    ! grep -Fq $'\007' "$t19_out" &&
    ! grep -Fq $'\177' "$t19_out" &&
    ! grep -Fq $'\235' "$t19_out" &&
    ! grep -Fq $'\234' "$t19_out"; then
    pass "selector diagnostic omitted terminal control characters"
else
    fail "selector diagnostic contained terminal controls or was missing (exit: $t19_exit, out: $(cat "$t19_out"))"
fi

# -----------------------------------------------------------------------------
# Summary
# -----------------------------------------------------------------------------
echo ""
TOTAL=$((PASSED + FAILED))
if [[ "$FAILED" -eq 0 ]]; then
    print_success "All stress harness regression tests passed! ($PASSED/$TOTAL)"
    exit 0
else
    print_error "Stress harness regression tests: $FAILED/$TOTAL FAILED"
    exit 1
fi
