#!/bin/bash
# E2E test: tests/validate-e2e-parity.sh must catch an unguarded .bat child call.
#
# Guards #1040. The Windows E2E wrapper (run-tests.bat) used to call child scripts without
# checking errorlevel, so a child that incremented its own failure counter and returned 1 was
# swallowed and the wrapper still reported success. The runtime proof of that needs cmd.exe,
# which only the Windows CI leg has, so this test pins the validator that keeps the guards in
# place — and it runs on every leg, including Linux and macOS.

set -euo pipefail

PASSED=0
FAILED=0

function print_info() { local msg="$1"; echo -e "\033[44m[ INFO ]\033[0m $msg"; }
function print_success() { local msg="$1"; echo -e "\033[42m[ SUCCESS ]\033[0m $msg"; }
function print_error() { local msg="$1"; echo -e "\033[41m[ ERROR ]\033[0m $msg" >&2; }

pass() { local msg="$1"; print_info "PASS: $msg"; PASSED=$((PASSED + 1)); }
fail() { local msg="$1"; print_error "FAIL: $msg"; FAILED=$((FAILED + 1)); }

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
VALIDATOR="$REPO_ROOT/tests/validate-e2e-parity.sh"
RUNNER_BAT="$REPO_ROOT/tests/run-tests.bat"
SANDBOX="$(mktemp -d)"

cleanup() { rm -rf "$SANDBOX"; }
trap cleanup EXIT

print_info "=== E2E Validator Guard-Parity Tests (#1040) ==="

# --- Preconditions ---

if [[ ! -f "$VALIDATOR" ]]; then
    fail "tests/validate-e2e-parity.sh not found"
elif [[ ! -f "$RUNNER_BAT" ]]; then
    fail "tests/run-tests.bat not found"
else
    pass "validator and runner are present"
fi

# The committed state must be clean: every child call is guarded.
if bash "$VALIDATOR" --strict > "$SANDBOX/clean.out" 2>&1; then
    pass "committed run-tests.bat has every .bat child call guarded"
else
    fail "committed run-tests.bat is unguarded per the validator"
    cat "$SANDBOX/clean.out"
fi

if grep -q "every .bat child call guarded" "$SANDBOX/clean.out"; then
    pass "validator reports the guard check in its summary line"
else
    fail "validator summary does not mention the guard check"
fi

# --- Negative: a child call with no guard must be caught ---

SANDBOX_REPO="$SANDBOX/repo"
mkdir -p "$SANDBOX_REPO/tests"
cp "$VALIDATOR" "$SANDBOX_REPO/tests/validate-e2e-parity.sh"
cp "$RUNNER_BAT" "$SANDBOX_REPO/tests/run-tests.bat"
cp "$REPO_ROOT/tests/run-tests.sh" "$SANDBOX_REPO/tests/run-tests.sh"

python3 - "$SANDBOX_REPO/tests/run-tests.bat" <<'PY'
import sys

path = sys.argv[1]
with open(path, encoding="utf-8-sig") as handle:
    text = handle.read()

target = "call .\\tests\\test-multipage-tiff.bat"
guard = """if errorlevel 1 (
    echo [ ERROR ] Multipage TIFF tests failed.
    exit /b 1
)
"""
if target + "\n" + guard not in text:
    sys.exit("could not find the expected guard block to remove")

text = text.replace(target + "\n" + guard, target + "\n", 1)
with open(path, "w", encoding="utf-8") as handle:
    handle.write(text)
PY

if bash "$SANDBOX_REPO/tests/validate-e2e-parity.sh" --strict > "$SANDBOX/unguarded.out" 2>&1; then
    fail "validator passed a run-tests.bat with an unguarded child call"
else
    pass "validator fails when a child call loses its guard"
fi

if grep -q "test-multipage-tiff.bat" "$SANDBOX/unguarded.out"; then
    pass "validator names the offending child script"
else
    fail "validator does not name the offending child script"
    cat "$SANDBOX/unguarded.out"
fi

# --- Negative: a REM comment quoting a call is not a call ---

cp "$RUNNER_BAT" "$SANDBOX_REPO/tests/run-tests.bat"

python3 - "$SANDBOX_REPO/tests/run-tests.bat" <<'PY'
import sys

path = sys.argv[1]
with open(path, encoding="utf-8-sig") as handle:
    text = handle.read()

target = "call .\\tests\\test-bates-numbering.bat"
# Padding comments push the real, correctly guarded call beyond any fixed line window, so a
# validator that scanned a window instead of the next significant statement would either
# match the comment or read the wrong lines.
padding = "\n".join(["REM  call .\\tests\\test-bates-numbering.bat"] + ["REM  pad"] * 6)
if target not in text:
    sys.exit("could not find the bates-numbering call to shadow")
text = text.replace(target, padding + "\n" + target, 1)
with open(path, "w", encoding="utf-8") as handle:
    handle.write(text)
PY

if bash "$SANDBOX_REPO/tests/validate-e2e-parity.sh" --strict > "$SANDBOX/comment.out" 2>&1; then
    pass "validator ignores a REM comment that quotes a child call"
else
    fail "validator treated a REM comment as an unguarded child call"
    cat "$SANDBOX/comment.out"
fi

# --- Negative: a guard that reports but does not abort must be caught ---

python3 - "$SANDBOX_REPO/tests/run-tests.bat" <<'PY'
import sys

path = sys.argv[1]
with open(path, encoding="utf-8-sig") as handle:
    text = handle.read()

old = """if errorlevel 1 (
    echo [ ERROR ] Office formats tests failed.
    exit /b 1
)
"""
new = """if errorlevel 1 (
    echo [ ERROR ] Office formats tests failed.
)
"""
if old not in text:
    sys.exit("could not find the expected office-formats guard block")

text = text.replace(old, new, 1)
with open(path, "w", encoding="utf-8") as handle:
    handle.write(text)
PY

if bash "$SANDBOX_REPO/tests/validate-e2e-parity.sh" --strict > "$SANDBOX/noexit.out" 2>&1; then
    fail "validator passed a guard that prints but never exits non-zero"
else
    pass "validator fails when a guard does not 'exit /b 1'"
fi

if grep -q "does not 'exit /b 1'" "$SANDBOX/noexit.out"; then
    pass "validator distinguishes a non-aborting guard from a missing one"
else
    fail "validator does not report the non-aborting guard case"
    cat "$SANDBOX/noexit.out"
fi

# --- Negative: a second, unguarded duplicate of an already-guarded child must be caught ---

cp "$RUNNER_BAT" "$SANDBOX_REPO/tests/run-tests.bat"
printf '\ncall .\\tests\\test-archive-test-suites.bat\n' >> "$SANDBOX_REPO/tests/run-tests.bat"

if bash "$SANDBOX_REPO/tests/validate-e2e-parity.sh" --strict > "$SANDBOX/duplicate.out" 2>&1; then
    fail "validator passed a runner with a second unguarded call to an already-guarded child"
    cat "$SANDBOX/duplicate.out"
else
    pass "validator fails on a second unguarded duplicate call site"
fi

# --- Summary ---

echo ""
TOTAL=$((PASSED + FAILED))
if [[ "$FAILED" -eq 0 ]]; then
    print_success "All E2E validator guard-parity tests passed! ($PASSED/$TOTAL)"
else
    print_error "E2E validator guard-parity tests: $FAILED/$TOTAL FAILED"
    exit 1
fi
