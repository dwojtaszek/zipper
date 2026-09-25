#!/bin/bash
# E2E test: TypeSafe audit runner foundation (#955).
# Runs the Python unit suite, a no-network fixture-mode audit, and workflow
# structure checks (permissions, fork safety, pinning, advisory behavior).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOOL_DIR="$SCRIPT_DIR/../tools/typesafe-audit"
TEMP_DIR="./results/test-typesafe-audit-$$"
# Create the shared parent, then claim the per-run directory exclusively: a stale
# directory could otherwise supply previous-run reports.
mkdir -p "$(dirname "$TEMP_DIR")"
if ! mkdir "$TEMP_DIR"; then
    echo "[ ERROR ] Could not create the temporary test directory: $TEMP_DIR" >&2
    exit 1
fi

function print_success() { echo -e "\033[42m[ SUCCESS ]\033[0m $1"; }
function print_error() { echo -e "\033[41m[ ERROR ]\033[0m $1" >&2; }

cleanup() { rm -rf "$TEMP_DIR"; }
trap cleanup EXIT

WORKFLOW_FILE="$SCRIPT_DIR/../.github/workflows/typesafe-audit.yml"

# --- Part 1: Python unit tests (no network) ---

python3 -m unittest discover -s "$TOOL_DIR/tests" 2>&1 || {
    print_error "TypeSafe runner unit tests failed."
    exit 1
}
print_success "TypeSafe runner unit tests passed."

python3 -m unittest discover -s "$SCRIPT_DIR/../.zipper-runner/tests" 2>&1 || {
    print_error "Zipper runner unit tests failed."
    exit 1
}
print_success "Zipper runner unit tests passed."

# --- Part 2: fixture-mode audit end to end (no credential, no network) ---

# Record a deterministic fixture for the current sample inputs, then replay it.
FIXTURE_DIR="$TEMP_DIR/fixtures"
mkdir -p "$FIXTURE_DIR"

python3 "$TOOL_DIR/record_sample_fixture.py" "$FIXTURE_DIR"

JSON_OUT="$TEMP_DIR/report.json"
MD_OUT="$TEMP_DIR/report.md"
RUNNER_LOG="$TEMP_DIR/runner1.log"

python3 "$TOOL_DIR/runner.py" \
    --mode fixture \
    --fixture-dir "$FIXTURE_DIR" \
    --files "$TOOL_DIR/questions/files-sample.list" \
    --questions "$TOOL_DIR/questions/example.json" \
    --json-out "$JSON_OUT" \
    --md-out "$MD_OUT" > "$RUNNER_LOG" 2>&1 || {
    print_error "Fixture-mode audit run failed."
    cat "$RUNNER_LOG" >&2
    exit 1
}

[[ -s "$JSON_OUT" ]] || { print_error "JSON report missing or empty."; exit 1; }
[[ -s "$MD_OUT" ]] || { print_error "Markdown report missing or empty."; exit 1; }

# Deterministic: a second identical run produces byte-identical JSON and Markdown.
JSON_OUT2="$TEMP_DIR/report2.json"
MD_OUT2="$TEMP_DIR/report2.md"
RUNNER_LOG2="$TEMP_DIR/runner2.log"
python3 "$TOOL_DIR/runner.py" \
    --mode fixture \
    --fixture-dir "$FIXTURE_DIR" \
    --files "$TOOL_DIR/questions/files-sample.list" \
    --questions "$TOOL_DIR/questions/example.json" \
    --json-out "$JSON_OUT2" \
    --md-out "$MD_OUT2" > "$RUNNER_LOG2" 2>&1 || {
    print_error "Fixture-mode deterministic rerun failed."
    cat "$RUNNER_LOG2" >&2
    exit 1
}
cmp -s "$JSON_OUT" "$JSON_OUT2" || { print_error "Fixture-mode output is not deterministic."; exit 1; }
cmp -s "$MD_OUT" "$MD_OUT2" || { print_error "Fixture-mode Markdown output is not deterministic."; exit 1; }

# Reports must not contain secrets. grep exits 0 on a match, 1 on no match, and
# >1 on error (e.g. an unreadable report) — an error must not read as "clean".
if grep -qi "TYPESAFE_API_KEY\|Bearer " "$JSON_OUT" "$MD_OUT"; then
    print_error "Report files must not contain secrets."
    exit 1
elif [[ $? -gt 1 ]]; then
    print_error "Report secret check could not be completed."
    exit 1
fi

print_success "Fixture-mode audit is deterministic and secret-free."

# --- Part 3: workflow structure checks ---

[[ -f "$WORKFLOW_FILE" ]] || { print_error "Missing $WORKFLOW_FILE"; exit 1; }

check_contains() {
    if grep -qE "$2" "$WORKFLOW_FILE"; then
        print_success "Workflow check: $1"
    else
        print_error "Workflow check failed: $1"
        exit 1
    fi
}

check_absent() {
    # grep exits 0 on a match, 1 on no match, >1 on error — an error must not
    # read as "the forbidden construct is absent".
    if grep -qE "$2" "$WORKFLOW_FILE"; then
        print_error "Workflow check failed: $1 must be absent"
        exit 1
    elif [[ $? -gt 1 ]]; then
        print_error "Workflow check failed: $1 could not be checked"
        exit 1
    fi
    print_success "Workflow check: $1"
}

check_contains "least-privilege permissions" "^permissions:"
check_contains "contents: read" "contents: read"
check_absent "no pull_request_target (fork safety)" "pull_request_target"
check_contains "concurrency cancellation" "^concurrency:"
check_contains "explicit job timeout" "timeout-minutes:"
check_contains "weekly schedule trigger" "^  schedule:"
check_contains "manual dispatch trigger" "workflow_dispatch"
check_contains "path-filtered PR trigger" "^    paths:"
check_contains "TYPESAFE_API_KEY secret wiring" "TYPESAFE_API_KEY"
check_contains "fork/secret-availability guard" "secret"
# Every action reference must be pinned to a full 40-char commit SHA. Keys may be
# quoted ('uses': / "uses":), comments are stripped so a SHA inside a comment
# cannot pose as a pin, and the result is read in full (no `grep -q`, which under
# `pipefail` can turn a violation into a SIGPIPE success). A workflow with no
# action lines at all must fail rather than pass vacuously.
if [[ "$(grep -cE "['\"]?uses['\"]?:" "$WORKFLOW_FILE" || true)" -eq 0 ]]; then
    print_error "Workflow check failed: no action uses lines found"
    exit 1
fi
unpinned_actions=$(grep -E "['\"]?uses['\"]?:" "$WORKFLOW_FILE" | grep -vE '^[[:space:]]*#' | sed 's/#.*//' | grep -vE '@[0-9a-f]{40}[[:space:]]*$' || true)
if [[ -n "$unpinned_actions" ]]; then
    print_error "Workflow check failed: every action must be pinned to a full commit SHA"
    exit 1
fi
print_success "Workflow check: all actions pinned to full commit SHAs"
# Advisory: the runner must not run in --strict (blocking) mode in CI. Matched
# case-insensitively and without `grep -q` for the same pipefail reason above.
if strict_lines=$(grep -E "runner\.py" "$WORKFLOW_FILE" | grep -i -- "--strict"); then
    print_error "Workflow check failed: audit must stay advisory (no --strict)"
    exit 1
elif [[ $? -gt 1 ]]; then
    print_error "Workflow check failed: advisory mode could not be checked"
    exit 1
fi
print_success "Workflow check: advisory (no --strict)"

print_success "All TypeSafe audit E2E tests passed!"
