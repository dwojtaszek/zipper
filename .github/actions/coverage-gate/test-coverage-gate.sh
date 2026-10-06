#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ALLOWLIST="${SCRIPT_DIR}/../../coverage-file-allowlist.txt"

# Helper to assert expected failure and diagnostic message in stderr
run_expect_failure() {
  local test_name="$1"
  local expected_diag="$2"
  shift 2
  local stdout_file
  local stderr_file
  stdout_file=$(mktemp)
  stderr_file=$(mktemp)
  local exit_code=0
  python3 "${SCRIPT_DIR}/check-per-file-coverage.py" "$@" > "$stdout_file" 2> "$stderr_file" || exit_code=$?
  if [[ "$exit_code" -eq 0 ]]; then
    echo "ERROR [$test_name]: Expected non-zero exit code, got 0" >&2
    echo "--- stdout ---" >&2
    cat "$stdout_file" >&2
    rm -f "$stdout_file" "$stderr_file"
    exit 1
  fi
  if [[ -n "$expected_diag" ]] && ! grep -Eq "$expected_diag" "$stderr_file"; then
    echo "ERROR [$test_name]: Expected diagnostic pattern '$expected_diag' not found in stderr:" >&2
    echo "--- stderr ---" >&2
    cat "$stderr_file" >&2
    rm -f "$stdout_file" "$stderr_file"
    exit 1
  fi
  rm -f "$stdout_file" "$stderr_file"
  echo "  [PASS] $test_name"
}

# Helper to assert expected success and output pattern in stdout
run_expect_success() {
  local test_name="$1"
  local expected_text="$2"
  shift 2
  local stdout_file
  local stderr_file
  stdout_file=$(mktemp)
  stderr_file=$(mktemp)
  local exit_code=0
  python3 "${SCRIPT_DIR}/check-per-file-coverage.py" "$@" > "$stdout_file" 2> "$stderr_file" || exit_code=$?
  if [[ "$exit_code" -ne 0 ]]; then
    echo "ERROR [$test_name]: Expected exit code 0, got $exit_code" >&2
    echo "--- stderr ---" >&2
    cat "$stderr_file" >&2
    rm -f "$stdout_file" "$stderr_file"
    exit 1
  fi
  if [[ -n "$expected_text" ]] && ! grep -Eq "$expected_text" "$stdout_file"; then
    echo "ERROR [$test_name]: Expected text pattern '$expected_text' not found in stdout:" >&2
    echo "--- stdout ---" >&2
    cat "$stdout_file" >&2
    rm -f "$stdout_file" "$stderr_file"
    exit 1
  fi
  rm -f "$stdout_file" "$stderr_file"
  echo "  [PASS] $test_name"
}

echo "==> Testing check-per-file-coverage.py against passing fixture..."
run_expect_success "Passing fixture" "All 1 evaluated files passed" \
  --reports "${SCRIPT_DIR}/testdata/passing_coverage.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing check-per-file-coverage.py against failing fixture..."
run_expect_failure "Failing fixture" "1 file\(s\) failed the per-file minimum coverage threshold" \
  --reports "${SCRIPT_DIR}/testdata/failing_coverage.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing check-per-file-coverage.py with allowlist fixture..."
run_expect_success "Allowlist fixture" "ALLOWLIST" \
  --reports "${SCRIPT_DIR}/testdata/allowlist_coverage.cobertura.xml" \
  --allowlist "${ALLOWLIST}" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing missing reports (glob matches nothing)..."
run_expect_failure "Missing reports" "No Cobertura XML report files found matching pattern:" \
  --reports "${SCRIPT_DIR}/testdata/nonexistent_*.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing missing reports does not fall back to unrelated reports in working directory..."
TEMP_FALLBACK_DIR=$(mktemp -d)
cp "${SCRIPT_DIR}/testdata/passing_coverage.cobertura.xml" "${TEMP_FALLBACK_DIR}/coverage.cobertura.xml"
(
  cd "${TEMP_FALLBACK_DIR}"
  run_expect_failure "Missing reports with unrelated fallback present" \
    "No Cobertura XML report files found matching pattern:" \
    --reports "nonexistent_dir/*.xml" \
    --min-coverage 50 \
    --min-lines 20
)
rm -rf "${TEMP_FALLBACK_DIR}"

echo "==> Testing malformed-only report: broken XML syntax..."
run_expect_failure "Malformed XML syntax" "malformed_syntax.cobertura.xml" \
  --reports "${SCRIPT_DIR}/testdata/malformed_syntax.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing malformed-only report: invalid root element..."
run_expect_failure "Malformed root element" "malformed_root.cobertura.xml" \
  --reports "${SCRIPT_DIR}/testdata/malformed_root.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing invalid line attribute: non-integer number..."
run_expect_failure "Invalid line number" "invalid_line_num.cobertura.xml" \
  --reports "${SCRIPT_DIR}/testdata/invalid_line_num.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing invalid line attribute: non-integer hits..."
run_expect_failure "Invalid line hits" "invalid_line_hits.cobertura.xml" \
  --reports "${SCRIPT_DIR}/testdata/invalid_line_hits.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing invalid line attribute: negative number or hits..."
run_expect_failure "Invalid negative line/hits" "invalid_line_negative.cobertura.xml" \
  --reports "${SCRIPT_DIR}/testdata/invalid_line_negative.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing invalid line attribute: missing hits attribute..."
run_expect_failure "Invalid missing line attribute" "invalid_line_missing.cobertura.xml" \
  --reports "${SCRIPT_DIR}/testdata/invalid_line_missing.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing mixed malformed and healthy reports fails closed..."
run_expect_failure "Mixed malformed and healthy reports" "malformed.cobertura.xml" \
  --reports "${SCRIPT_DIR}/testdata/mixed_scenario/*.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing disjoint-half coverage multi-report merge..."
run_expect_success "Disjoint coverage union (20/20 = 100%)" "20 / 20 +100\.0% +PASS" \
  --reports "${SCRIPT_DIR}/testdata/disjoint_part*.cobertura.xml" \
  --min-coverage 80 \
  --min-lines 20

echo "==> Testing repeated classes and async state machine deduplication..."
run_expect_success "Repeated classes deduplication (10/20 = 50%)" "10 / 20 +50\.0% +PASS" \
  --reports "${SCRIPT_DIR}/testdata/repeated_classes.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing Windows and Linux path normalization..."
run_expect_success "Windows and relative path normalization" "WindowsModule.cs" \
  --reports "${SCRIPT_DIR}/testdata/windows_paths.cobertura.xml" \
  --min-coverage 50 \
  --min-lines 20

echo "==> Testing exact threshold boundaries..."
# ExactFifty has 10/20 (50.0%). With floor 50.0% it passes; with floor 50.1% it fails.
run_expect_success "Exact threshold pass (50.0% >= 50.0%)" "PASS" \
  --reports "${SCRIPT_DIR}/testdata/boundary_coverage.cobertura.xml" \
  --min-coverage 50.0 \
  --min-lines 20
run_expect_failure "Exact threshold fail (50.0% < 50.1%)" "ExactFifty.cs" \
  --reports "${SCRIPT_DIR}/testdata/boundary_coverage.cobertura.xml" \
  --min-coverage 50.1 \
  --min-lines 20

echo "==> Testing exact min-lines boundaries..."
# NineteenLines has 19 valid lines. With min-lines 20 it is SKIP (<20); with min-lines 19 it is FAIL (0.0%).
run_expect_success "Min-lines skip boundary (<20)" "SKIP \(<20\)" \
  --reports "${SCRIPT_DIR}/testdata/boundary_coverage.cobertura.xml" \
  --min-coverage 50.0 \
  --min-lines 20
run_expect_failure "Min-lines evaluated boundary (19 lines with min-lines 19 fails)" "NineteenLines.cs" \
  --reports "${SCRIPT_DIR}/testdata/boundary_coverage.cobertura.xml" \
  --min-coverage 50.0 \
  --min-lines 19

echo "==> Testing allowlist path normalization (Windows backslash and ./src/ prefixes)..."
run_expect_success "Allowlist path normalization" "All 0 evaluated files passed" \
  --reports "${SCRIPT_DIR}/testdata/allowlist_test.cobertura.xml" \
  --allowlist "${SCRIPT_DIR}/testdata/test_allowlist.txt" \
  --min-coverage 50.0 \
  --min-lines 20

echo "[SUCCESS] All coverage gate script tests passed!"
