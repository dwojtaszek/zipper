#!/bin/bash
# E2E tests for EmptyPercentage behaviour via chi-square goodness-of-fit test.
#
# Uses: tests/fixtures/profiles/test-empty-pct-20.json
#       tests/goldens/lib/chi-square.sh
#
# Tests:
#   - EmptyPercentage=20 passes chi-square (p >= 0.01) for seeds 1, 42, 99, 1337
#   - Edge cases: 0% (exact equality — zero empties), 100% (all empties), 10% and 50% chi-square

set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

PROFILE_FILE="$(dirname "$0")/fixtures/profiles/test-empty-pct-20.json"
CHI_SQ="$(dirname "$0")/goldens/lib/chi-square.sh"
TEST_OUTPUT_DIR="./results/column-profile-empty-pct"
COUNT=10000
SIGNIFICANCE=0.01

function print_success() { echo -e "\e[42m[ SUCCESS ]\e[0m $1"; }
function print_info()    { echo -e "\e[44m[ INFO    ]\e[0m $1"; }
function print_error()   { echo -e "\e[41m[ ERROR   ]\e[0m $1"; exit 1; }

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# Count empty values in the TEXTFIELD column (index 1, 0-based) of a DAT load file.
# Uses Python for correct UTF-8 multi-byte handling of the þ quote char (U+00FE).
count_empties() {
    local dat_file="$1"
    python3 - "$dat_file" <<'PYEOF'
import sys
COL_SEP = '\u0014'
QUOTE   = '\u00fe'
empty = 0
with open(sys.argv[1], encoding='utf-8') as f:
    next(f)  # skip header
    for line in f:
        line = line.rstrip('\r\n')
        if not line:
            continue
        fields = [c.strip(QUOTE) for c in line.split(COL_SEP)]
        if len(fields) > 1 and fields[1] == '':
            empty += 1
print(empty)
PYEOF
}

# --- Test 1: EmptyPercentage=20 with seeds 1, 42, 99, 1337 ---
print_info "=== EmptyPercentage=20 chi-square tests ==="
for seed in 1 42 99 1337; do
    out_dir="$TEST_OUTPUT_DIR/pct20_seed${seed}"
    mkdir -p "$out_dir"
    print_info "Running: profile=test-empty-pct-20  seed=$seed  count=$COUNT"

    zipper \
        --count "$COUNT" \
        --type pdf \
        --column-profile "$PROFILE_FILE" \
        --seed "$seed" \
        --loadfile-only \
        --output-path "$out_dir"

    dat_file=$(find "$out_dir" -name "*.dat" | head -1)
    [[ -z "$dat_file" ]] && print_error "No .dat file produced (seed=$seed)"

    empties=$(count_empties "$dat_file")
    print_info "seed=$seed: observed $empties empty out of $COUNT (expected rate=0.20)"

    if ! bash "$CHI_SQ" "$empties" "$COUNT" "0.20" "$SIGNIFICANCE"; then
        print_error "Chi-square FAILED for EmptyPercentage=20, seed=$seed (p < $SIGNIFICANCE)"
    fi
    print_success "seed=$seed: chi-square PASSED"
done

# --- Test 2: Edge case — EmptyPercentage=0 (exact: zero empties expected) ---
print_info "=== Edge case: EmptyPercentage=0 ==="
tmp_profile_0="$TEST_OUTPUT_DIR/test-empty-pct-0.json"
cat > "$tmp_profile_0" << 'JSONEOF'
{
  "name": "test-empty-pct-0",
  "description": "Empty percentage edge case: 0%",
  "version": "1.0",
  "fieldNamingConvention": "UPPERCASE",
  "settings": { "emptyValuePercentage": 0, "dateFormat": "yyyy-MM-dd" },
  "dataSources": {},
  "columns": [
    { "name": "DOCID", "type": "identifier", "required": true },
    { "name": "TEXTFIELD", "type": "text", "emptyPercentage": 0 }
  ]
}
JSONEOF

zipper \
    --count "$COUNT" --type pdf \
    --column-profile "$tmp_profile_0" \
    --seed 42 --loadfile-only \
    --output-path "$TEST_OUTPUT_DIR/pct0"

dat_0=$(find "$TEST_OUTPUT_DIR/pct0" -name "*.dat" | head -1)
empties_0=$(count_empties "$dat_0")
[[ "$empties_0" -ne 0 ]] && print_error "EmptyPercentage=0: expected 0 empties, got $empties_0"
print_success "EmptyPercentage=0: exactly 0 empty values"

# --- Test 3: Edge case — EmptyPercentage=100 (all empties) ---
print_info "=== Edge case: EmptyPercentage=100 ==="
tmp_profile_100="$TEST_OUTPUT_DIR/test-empty-pct-100.json"
cat > "$tmp_profile_100" << 'JSONEOF'
{
  "name": "test-empty-pct-100",
  "description": "Empty percentage edge case: 100%",
  "version": "1.0",
  "fieldNamingConvention": "UPPERCASE",
  "settings": { "emptyValuePercentage": 0, "dateFormat": "yyyy-MM-dd" },
  "dataSources": {},
  "columns": [
    { "name": "DOCID", "type": "identifier", "required": true },
    { "name": "TEXTFIELD", "type": "text", "emptyPercentage": 100 }
  ]
}
JSONEOF

zipper \
    --count "$COUNT" --type pdf \
    --column-profile "$tmp_profile_100" \
    --seed 42 --loadfile-only \
    --output-path "$TEST_OUTPUT_DIR/pct100"

dat_100=$(find "$TEST_OUTPUT_DIR/pct100" -name "*.dat" | head -1)
empties_100=$(count_empties "$dat_100")
[[ "$empties_100" -ne "$COUNT" ]] && print_error "EmptyPercentage=100: expected $COUNT empties, got $empties_100"
print_success "EmptyPercentage=100: all $COUNT values are empty"

# --- Test 4: EmptyPercentage=10 chi-square ---
print_info "=== EmptyPercentage=10 chi-square test ==="
tmp_profile_10="$TEST_OUTPUT_DIR/test-empty-pct-10.json"
cat > "$tmp_profile_10" << 'JSONEOF'
{
  "name": "test-empty-pct-10",
  "description": "Empty percentage test: 10%",
  "version": "1.0",
  "fieldNamingConvention": "UPPERCASE",
  "settings": { "emptyValuePercentage": 0, "dateFormat": "yyyy-MM-dd" },
  "dataSources": {},
  "columns": [
    { "name": "DOCID", "type": "identifier", "required": true },
    { "name": "TEXTFIELD", "type": "text", "emptyPercentage": 10 }
  ]
}
JSONEOF

zipper \
    --count "$COUNT" --type pdf \
    --column-profile "$tmp_profile_10" \
    --seed 42 --loadfile-only \
    --output-path "$TEST_OUTPUT_DIR/pct10"

dat_10=$(find "$TEST_OUTPUT_DIR/pct10" -name "*.dat" | head -1)
empties_10=$(count_empties "$dat_10")
print_info "EmptyPercentage=10: observed $empties_10 empty out of $COUNT"
if ! bash "$CHI_SQ" "$empties_10" "$COUNT" "0.10" "$SIGNIFICANCE"; then
    print_error "Chi-square FAILED for EmptyPercentage=10 (p < $SIGNIFICANCE)"
fi
print_success "EmptyPercentage=10: chi-square PASSED"

# --- Test 5: EmptyPercentage=50 chi-square ---
print_info "=== EmptyPercentage=50 chi-square test ==="
tmp_profile_50="$TEST_OUTPUT_DIR/test-empty-pct-50.json"
cat > "$tmp_profile_50" << 'JSONEOF'
{
  "name": "test-empty-pct-50",
  "description": "Empty percentage test: 50%",
  "version": "1.0",
  "fieldNamingConvention": "UPPERCASE",
  "settings": { "emptyValuePercentage": 0, "dateFormat": "yyyy-MM-dd" },
  "dataSources": {},
  "columns": [
    { "name": "DOCID", "type": "identifier", "required": true },
    { "name": "TEXTFIELD", "type": "text", "emptyPercentage": 50 }
  ]
}
JSONEOF

zipper \
    --count "$COUNT" --type pdf \
    --column-profile "$tmp_profile_50" \
    --seed 42 --loadfile-only \
    --output-path "$TEST_OUTPUT_DIR/pct50"

dat_50=$(find "$TEST_OUTPUT_DIR/pct50" -name "*.dat" | head -1)
empties_50=$(count_empties "$dat_50")
print_info "EmptyPercentage=50: observed $empties_50 empty out of $COUNT"
if ! bash "$CHI_SQ" "$empties_50" "$COUNT" "0.50" "$SIGNIFICANCE"; then
    print_error "Chi-square FAILED for EmptyPercentage=50 (p < $SIGNIFICANCE)"
fi
print_success "EmptyPercentage=50: chi-square PASSED"

echo ""
print_success "All EmptyPercentage tests PASSED."
