#!/bin/bash
# test-mutation-smoke.sh — Bounded integration smoke for pinned Stryker CLI (#1110)
#
# Validates:
# 1. Pinned Stryker CLI accepts the workflow options (--concurrency 4, --output dir, --mutate, etc.)
# 2. Bounded mutation run on a small production file produces >0 mutants
# 3. JSON report is located beneath the output dir and normalized to the exact downstream path
# 4. quality/mutation.py successfully parses the generated report into deterministic categories
# 5. Missing report and zero selected mutants fail explicitly

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
TEMP_DIR="$REPO_ROOT/results/mutation-smoke-$$"

TARGET_FILE="ContentTypeHelper.cs"
TIMEOUT_MINS="15"
PREBUILT_REPORT=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --prebuilt-report)
            PREBUILT_REPORT="$2"
            shift 2
            ;;
        --file)
            TARGET_FILE="$2"
            shift 2
            ;;
        --timeout)
            TIMEOUT_MINS="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 1
            ;;
    esac
done

function print_success() { echo -e "\033[42m[ SUCCESS ]\033[0m $1"; }
function print_error() { echo -e "\033[41m[ ERROR ]\033[0m $1" >&2; }
function print_info() { echo -e "\033[44m[ INFO ]\033[0m $1"; }

cleanup() {
    rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

mkdir -p "$TEMP_DIR"

print_info "Starting bounded Stryker mutation integration smoke..."

# Step 1: Verify pinned tools
print_info "1. Verifying pinned dotnet-stryker CLI options..."
if ! dotnet stryker --help > /dev/null 2>&1; then
    print_info "Restoring pinned tools..."
    dotnet tool restore > /dev/null
fi

# Verify CLI rejects old unsupported option
REJECT_OUTPUT=$(dotnet stryker --max-concurrent-test-runs 4 2>&1 || true)
if echo "$REJECT_OUTPUT" | grep -q "Unrecognized option"; then
    print_success "Verified CLI rejects unsupported --max-concurrent-test-runs"
else
    print_error "Expected CLI to reject --max-concurrent-test-runs"
    exit 1
fi

# Verify CLI accepts pinned options
HELP_OUTPUT=$(dotnet stryker --help)
for opt in "--concurrency" "--mutate" "--project" "--test-project" "--reporter" "--output" "--break-at" "--skip-version-check"; do
    if ! echo "$HELP_OUTPUT" | grep -q -- "$opt"; then
        print_error "Stryker CLI help missing expected option: $opt"
        exit 1
    fi
done
print_success "Stryker CLI accepts all workflow options"

# Step 2: Run bounded mutation on target file (or use prebuilt report)
RAW_OUTPUT_DIR="$TEMP_DIR/stryker-raw"
mkdir -p "$RAW_OUTPUT_DIR"

if [[ -n "$PREBUILT_REPORT" && -f "$PREBUILT_REPORT" ]]; then
    print_info "2. Using provided prebuilt report: $PREBUILT_REPORT"
    mkdir -p "$RAW_OUTPUT_DIR/reports"
    cp "$PREBUILT_REPORT" "$RAW_OUTPUT_DIR/reports/mutation-report.json"
else
    print_info "2. Executing pinned Stryker on small production file: $TARGET_FILE..."
    (cd "$REPO_ROOT/src" && timeout "${TIMEOUT_MINS}m" dotnet stryker \
        --project Zipper.csproj \
        --test-project Zipper.Tests/Zipper.Tests.csproj \
        --mutate "$TARGET_FILE" \
        --reporter json \
        --output "$RAW_OUTPUT_DIR" \
        --break-at 0 \
        --concurrency 4 \
        --skip-version-check) || {
        rc=$?
        print_error "dotnet stryker failed with exit code $rc"
        exit "$rc"
    }
fi

# Step 3: Locate report beneath output directory
print_info "3. Locating generated JSON report beneath output directory..."
FOUND_REPORT=$(find "$RAW_OUTPUT_DIR" -name 'mutation-report.json' -type f | head -1)
if [[ -z "$FOUND_REPORT" || ! -s "$FOUND_REPORT" ]]; then
    print_error "mutation-report.json was not generated or is empty"
    exit 1
fi
print_success "Located Stryker report at: $FOUND_REPORT"

# Step 4: Normalize and validate report using normalize_report.py
DOWNSTREAM_REPORT="$TEMP_DIR/results/mutation/mutation-report.json"
print_info "4. Normalizing report to exact downstream path: $DOWNSTREAM_REPORT..."
python3 "$REPO_ROOT/tools/typesafe-audit/quality/normalize_report.py" \
    --input "$RAW_OUTPUT_DIR" \
    --output "$DOWNSTREAM_REPORT" \
    --scope "$TARGET_FILE" \
    --repo-root "$REPO_ROOT" || {
    print_error "normalize_report.py failed on real Stryker output"
    exit 1
}

if [[ ! -s "$DOWNSTREAM_REPORT" ]]; then
    print_error "Downstream normalized report missing or empty at $DOWNSTREAM_REPORT"
    exit 1
fi
print_success "Downstream normalized report created successfully"

# Step 5: Verify report contents via quality/mutation.py
print_info "5. Verifying schema and categories via quality/mutation.py..."
python3 -c "
from pathlib import Path
import sys
sys.path.insert(0, '$REPO_ROOT/tools/typesafe-audit/quality')
import mutation
cats = mutation.parse_report(Path('$DOWNSTREAM_REPORT'), Path('$REPO_ROOT'))
total_mutants = sum(len(v) for v in cats.values())
if total_mutants <= 0:
    print('ERROR: mutation.py extracted 0 candidates from report', file=sys.stderr)
    sys.exit(1)
print(f'Extracted {total_mutants} candidate mutants across categories: {list(cats.keys())}')
for cat, entries in cats.items():
    for e in entries:
        assert not e['file'].startswith('src//'), f'Double src/ prefix in {e[\"file\"]}'
        assert e['file'].startswith('src/'), f'Expected src/ prefix in {e[\"file\"]}'
" || {
    print_error "quality/mutation.py verification failed"
    exit 1
}
print_success "quality/mutation.py parsed positive mutants with clean category schema"

# Step 6: Test failure modes (missing report & zero mutants)
print_info "6. Testing explicit failure modes..."

# Failure mode A: Missing report
EMPTY_DIR="$TEMP_DIR/empty-dir"
mkdir -p "$EMPTY_DIR"
if python3 "$REPO_ROOT/tools/typesafe-audit/quality/normalize_report.py" \
    --input "$EMPTY_DIR" \
    --output "$TEMP_DIR/dummy.json" > /dev/null 2>&1; then
    print_error "Expected normalize_report.py to fail on missing report, but it succeeded"
    exit 1
fi
print_success "Verified missing report fails explicitly"

# Failure mode B: Zero selected mutants
ZERO_REPORT="$TEMP_DIR/zero-report.json"
python3 -c "
import json
with open('$ZERO_REPORT', 'w') as f:
    json.dump({'files': {'Foo.cs': {'mutants': [{'status': 'Ignored'}]}}}, f)
"
if python3 "$REPO_ROOT/tools/typesafe-audit/quality/normalize_report.py" \
    --input "$ZERO_REPORT" \
    --output "$TEMP_DIR/dummy2.json" \
    --scope "$TARGET_FILE" > /dev/null 2>&1; then
    print_error "Expected normalize_report.py to fail on 0 selected mutants, but it succeeded"
    exit 1
fi
print_success "Verified zero selected mutants fails explicitly"

print_success "All Stryker mutation smoke contract checks passed!"
exit 0
