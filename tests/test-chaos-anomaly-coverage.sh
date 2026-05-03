#!/bin/bash
# test-chaos-anomaly-coverage.sh
#
# E2E: Chaos Engine anomaly-type + scenario coverage.
# Addresses issues:
#   B1 (#200) — per-Anomaly-Type assertions + _properties.json audit
#   B2 (#201, absorbed into #200) — per-Scenario coverage
#   B3 (#202, absorbed into #200) — _properties.json schema validation
#
# Must be called from the repository root:
#   bash ./tests/test-chaos-anomaly-coverage.sh

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
FIXTURES_DIR="$SCRIPT_DIR/fixtures"
GOLDENS_LIB_DIR="$SCRIPT_DIR/goldens/lib"
TEST_OUTPUT_DIR="$SCRIPT_DIR/results/chaos-coverage"

# shellcheck source=./_zipper-cli.sh
source "$SCRIPT_DIR/_zipper-cli.sh"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function print_success() { echo -e "\e[42m[ SUCCESS ]\e[0m $1"; }
function print_info()    { echo -e "\e[44m[ INFO ]\e[0m $1"; }
function print_error()   {
    echo -e "\e[41m[ ERROR ]\e[0m $1" >&2
    exit 1
}

function validate_properties_json() {
    bash "$GOLDENS_LIB_DIR/validate-properties-json.sh" "$1"
}

PASSED=0
TOTAL=0

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# ---------------------------------------------------------------------------
# Load fixture lists
# ---------------------------------------------------------------------------
# DAT types: all lines that do NOT start with 'opt-' (or '#' or empty)
mapfile -t DAT_TYPES < <(
    grep -v '^#' "$FIXTURES_DIR/chaos-anomaly-types.txt" \
    | grep -v '^[[:space:]]*$' \
    | grep -v '^opt-'
)

# OPT types: lines starting with 'opt-'
mapfile -t OPT_TYPES < <(
    grep -v '^#' "$FIXTURES_DIR/chaos-anomaly-types.txt" \
    | grep -v '^[[:space:]]*$' \
    | grep '^opt-'
)

# Scenario names (one per line, skip blank)
mapfile -t SCENARIO_NAMES < <(
    grep -v '^[[:space:]]*$' "$FIXTURES_DIR/chaos-scenarios.txt"
)

# Scenario → format + chaos_types from TSV (skip header row)
declare -A SCENARIO_FORMAT
declare -A SCENARIO_TYPES
while IFS=$'\t' read -r name types fmt; do
    [[ "$name" == "scenario_name" ]] && continue
    SCENARIO_FORMAT["$name"]="${fmt:-dat}"
    SCENARIO_TYPES["$name"]="${types:-}"
done < "$FIXTURES_DIR/chaos-scenario-types.tsv"

# ---------------------------------------------------------------------------
# B3 prep: validate existing golden _properties.json files up-front
# ---------------------------------------------------------------------------
TOTAL=$((TOTAL + 1))
print_info "B3: Validating committed golden _properties.json fixtures..."
GOLDEN_COUNT=0
while IFS= read -r -d '' prop_file; do
    validate_properties_json "$prop_file"
    GOLDEN_COUNT=$((GOLDEN_COUNT + 1))
done < <(find "$SCRIPT_DIR/goldens/fixtures" -name "*_properties.json" -print0)
print_info "Validated $GOLDEN_COUNT golden fixture(s)"
PASSED=$((PASSED + 1))
print_success "Golden fixture schema validation — PASSED"

# ---------------------------------------------------------------------------
# Generate no-chaos baselines (for diff assertions)
# ---------------------------------------------------------------------------
print_info "Generating no-chaos DAT baseline (seed 42, count 500)..."
zipper --loadfile-only --count 500 --output-path "$TEST_OUTPUT_DIR/baseline_dat" --seed 42
BASELINE_DAT=$(find "$TEST_OUTPUT_DIR/baseline_dat" -name "*.dat" | head -1)
[[ -z "$BASELINE_DAT" ]] && print_error "No baseline DAT file generated"

print_info "Generating no-chaos OPT baseline (seed 42, count 500)..."
zipper --loadfile-only --loadfile-format opt --count 500 \
    --output-path "$TEST_OUTPUT_DIR/baseline_opt" --seed 42
BASELINE_OPT=$(find "$TEST_OUTPUT_DIR/baseline_opt" -name "*.opt" | head -1)
[[ -z "$BASELINE_OPT" ]] && print_error "No baseline OPT file generated"

# ---------------------------------------------------------------------------
# B1 — per-Anomaly-Type: DAT types
# ---------------------------------------------------------------------------
for TYPE in "${DAT_TYPES[@]}"; do
    TOTAL=$((TOTAL + 1))
    SAFE="${TYPE//-/_}"
    OUT_DIR="$TEST_OUTPUT_DIR/dat_type_${SAFE}"
    print_info "B1 DAT: $TYPE"

    # Run 1
    zipper --loadfile-only --count 500 --output-path "$OUT_DIR/run1" \
        --chaos-mode --chaos-types "$TYPE" --chaos-amount 10 --seed 42

    PROPS=$(find "$OUT_DIR/run1" -name "*_properties.json" | head -1)
    [[ -z "$PROPS" ]] && print_error "No _properties.json for type: $TYPE"

    # B3: schema
    validate_properties_json "$PROPS"

    # At least one anomaly
    ANOMALY_COUNT=$(jq '.chaosMode.totalAnomalies' "$PROPS")
    [[ "$ANOMALY_COUNT" -le 0 ]] && print_error "No anomalies for type: $TYPE"

    # Set equality: only the requested type appears
    TYPES_PRESENT=$(jq -r '[.chaosMode.injectedAnomalies[]?.errorType] | unique | sort | .[]' \
        "$PROPS" 2>/dev/null | paste -sd ',' || true)
    if [[ "$TYPES_PRESENT" != "$TYPE" ]]; then
        print_error "Type mismatch for $TYPE: got '$TYPES_PRESENT'"
    fi

    # Unique line numbers (duplicates would indicate a bug)
    DUP_LINES=$(jq -r '
        [.chaosMode.injectedAnomalies[]?.lineNumber]
        | group_by(.)
        | map(select(length > 1))
        | .[0]? // empty' "$PROPS" 2>/dev/null || true)
    [[ -n "$DUP_LINES" ]] && print_error "Duplicate line numbers for type: $TYPE"

    # Numeric line numbers must be in [1, totalRecords+1]
    TOTAL_RECORDS=$(jq '.totalRecords' "$PROPS")
    MAX_LINE=$(( TOTAL_RECORDS + 1 ))  # header is line 1, data is 2..501
    BAD_LINES=$(jq -r --argjson max "$MAX_LINE" '
        [.chaosMode.injectedAnomalies[]?
         | select(.lineNumber | test("^[0-9]+$"))
         | .lineNumber | tonumber
         | select(. < 1 or . > $max)]
        | .[]?' "$PROPS" 2>/dev/null || true)
    [[ -n "$BAD_LINES" ]] && print_error "Line number out of [1,$MAX_LINE] for type: $TYPE: $BAD_LINES"

    # Baseline diff: the chaos file must differ from the no-chaos baseline.
    # (For 'encoding', differences are at the byte level — still detectable via cmp.)
    CHAOS_DAT=$(find "$OUT_DIR/run1" -name "*.dat" | head -1)
    if cmp -s "$BASELINE_DAT" "$CHAOS_DAT" 2>/dev/null; then
        print_error "Chaos DAT identical to baseline for type: $TYPE — anomaly not injected?"
    fi

    # Determinism: run 2 must produce byte-identical output
    zipper --loadfile-only --count 500 --output-path "$OUT_DIR/run2" \
        --chaos-mode --chaos-types "$TYPE" --chaos-amount 10 --seed 42
    CHAOS_DAT2=$(find "$OUT_DIR/run2" -name "*.dat" | head -1)
    PROPS2=$(find "$OUT_DIR/run2" -name "*_properties.json" | head -1)
    cmp -s "$CHAOS_DAT" "$CHAOS_DAT2" \
        || print_error "Determinism failed (DAT differs) for type: $TYPE"
    cmp -s "$PROPS" "$PROPS2" \
        || print_error "Determinism failed (_properties.json differs) for type: $TYPE"

    PASSED=$((PASSED + 1))
    print_success "B1 DAT type '$TYPE' — PASSED (anomalies: $ANOMALY_COUNT)"
done

# ---------------------------------------------------------------------------
# B1 — per-Anomaly-Type: OPT types
# ---------------------------------------------------------------------------
for TYPE in "${OPT_TYPES[@]}"; do
    TOTAL=$((TOTAL + 1))
    SAFE="${TYPE//-/_}"
    OUT_DIR="$TEST_OUTPUT_DIR/opt_type_${SAFE}"
    print_info "B1 OPT: $TYPE"

    zipper --loadfile-only --loadfile-format opt --count 500 \
        --output-path "$OUT_DIR/run1" \
        --chaos-mode --chaos-types "$TYPE" --chaos-amount 10 --seed 42

    PROPS=$(find "$OUT_DIR/run1" -name "*_properties.json" | head -1)
    [[ -z "$PROPS" ]] && print_error "No _properties.json for OPT type: $TYPE"

    validate_properties_json "$PROPS"

    ANOMALY_COUNT=$(jq '.chaosMode.totalAnomalies' "$PROPS")
    [[ "$ANOMALY_COUNT" -le 0 ]] && print_error "No anomalies for OPT type: $TYPE"

    TYPES_PRESENT=$(jq -r '[.chaosMode.injectedAnomalies[]?.errorType] | unique | sort | .[]' \
        "$PROPS" 2>/dev/null | paste -sd ',' || true)
    [[ "$TYPES_PRESENT" != "$TYPE" ]] \
        && print_error "Type mismatch for OPT $TYPE: got '$TYPES_PRESENT'"

    DUP_LINES=$(jq -r '
        [.chaosMode.injectedAnomalies[]?.lineNumber]
        | group_by(.)
        | map(select(length > 1))
        | .[0]? // empty' "$PROPS" 2>/dev/null || true)
    [[ -n "$DUP_LINES" ]] && print_error "Duplicate line numbers for OPT type: $TYPE"

    TOTAL_RECORDS=$(jq '.totalRecords' "$PROPS")
    MAX_LINE=$(( TOTAL_RECORDS + 1 ))
    BAD_LINES=$(jq -r --argjson max "$MAX_LINE" '
        [.chaosMode.injectedAnomalies[]?
         | select(.lineNumber | test("^[0-9]+$"))
         | .lineNumber | tonumber
         | select(. < 1 or . > $max)]
        | .[]?' "$PROPS" 2>/dev/null || true)
    [[ -n "$BAD_LINES" ]] && print_error "Line number out of [1,$MAX_LINE] for OPT type: $TYPE"

    CHAOS_OPT=$(find "$OUT_DIR/run1" -name "*.opt" | head -1)
    if cmp -s "$BASELINE_OPT" "$CHAOS_OPT" 2>/dev/null; then
        print_error "Chaos OPT identical to baseline for type: $TYPE — anomaly not injected?"
    fi

    zipper --loadfile-only --loadfile-format opt --count 500 \
        --output-path "$OUT_DIR/run2" \
        --chaos-mode --chaos-types "$TYPE" --chaos-amount 10 --seed 42
    CHAOS_OPT2=$(find "$OUT_DIR/run2" -name "*.opt" | head -1)
    PROPS2=$(find "$OUT_DIR/run2" -name "*_properties.json" | head -1)
    cmp -s "$CHAOS_OPT" "$CHAOS_OPT2" \
        || print_error "Determinism failed (OPT differs) for type: $TYPE"
    cmp -s "$PROPS" "$PROPS2" \
        || print_error "Determinism failed (_properties.json differs) for OPT type: $TYPE"

    PASSED=$((PASSED + 1))
    print_success "B1 OPT type '$TYPE' — PASSED (anomalies: $ANOMALY_COUNT)"
done

# ---------------------------------------------------------------------------
# B2 — per-Scenario coverage
# ---------------------------------------------------------------------------
for SCENARIO in "${SCENARIO_NAMES[@]}"; do
    [[ -z "$SCENARIO" ]] && continue
    TOTAL=$((TOTAL + 1))
    SAFE="${SCENARIO//-/_}"
    OUT_DIR="$TEST_OUTPUT_DIR/scenario_${SAFE}"
    FMT="${SCENARIO_FORMAT[$SCENARIO]:-dat}"
    DECLARED_TYPES="${SCENARIO_TYPES[$SCENARIO]:-}"

    print_info "B2 Scenario: $SCENARIO (format: $FMT)"

    FORMAT_ARG=()
    [[ "$FMT" == "opt" ]] && FORMAT_ARG=(--loadfile-format opt)

    # Run 1
    zipper --loadfile-only --count 1000 \
        --output-path "$OUT_DIR/run1" \
        --chaos-mode --chaos-scenario "$SCENARIO" --seed 42 \
        "${FORMAT_ARG[@]}"

    PROPS=$(find "$OUT_DIR/run1" -name "*_properties.json" | head -1)
    [[ -z "$PROPS" ]] && print_error "No _properties.json for scenario: $SCENARIO"

    validate_properties_json "$PROPS"

    ANOMALY_COUNT=$(jq '.chaosMode.totalAnomalies' "$PROPS")
    [[ "$ANOMALY_COUNT" -le 0 ]] && print_error "No anomalies for scenario: $SCENARIO"

    # Assert every actual type is within the declared set (skip check for full-chaos)
    if [[ -n "$DECLARED_TYPES" ]]; then
        while IFS= read -r actual_type; do
            [[ -z "$actual_type" ]] && continue
            if ! echo ",$DECLARED_TYPES," | grep -q ",$actual_type,"; then
                print_error "Scenario $SCENARIO: unexpected type '$actual_type' (declared: $DECLARED_TYPES)"
            fi
        done < <(jq -r '[.chaosMode.injectedAnomalies[]?.errorType] | unique | .[]' "$PROPS" 2>/dev/null || true)
    fi

    # Determinism
    zipper --loadfile-only --count 1000 \
        --output-path "$OUT_DIR/run2" \
        --chaos-mode --chaos-scenario "$SCENARIO" --seed 42 \
        "${FORMAT_ARG[@]}"
    PROPS2=$(find "$OUT_DIR/run2" -name "*_properties.json" | head -1)

    if [[ "$FMT" == "opt" ]]; then
        FILE1=$(find "$OUT_DIR/run1" -name "*.opt" | head -1)
        FILE2=$(find "$OUT_DIR/run2" -name "*.opt" | head -1)
    else
        FILE1=$(find "$OUT_DIR/run1" -name "*.dat" | head -1)
        FILE2=$(find "$OUT_DIR/run2" -name "*.dat" | head -1)
    fi
    cmp -s "$FILE1" "$FILE2" \
        || print_error "Determinism failed (load file differs) for scenario: $SCENARIO"
    cmp -s "$PROPS" "$PROPS2" \
        || print_error "Determinism failed (_properties.json differs) for scenario: $SCENARIO"

    PASSED=$((PASSED + 1))
    print_success "B2 Scenario '$SCENARIO' — PASSED (anomalies: $ANOMALY_COUNT)"
done

# ---------------------------------------------------------------------------
# Cleanup & summary
# ---------------------------------------------------------------------------
rm -rf "$TEST_OUTPUT_DIR"
echo ""
print_success "Chaos coverage: $PASSED/$TOTAL tests passed."
