#!/bin/bash
# E2E tests for --column-profile with --loadfile-only: built-in profile matrix.
#
# Covers all 40 combinations of (4 profiles) × (5 file types) × (2 seeds):
#   profile  ∈ { minimal, standard, litigation, full }
#   filetype ∈ { pdf, eml, tiff, docx, xlsx }
#   seed     ∈ { 42, 1337 }
#
# Assertions per combo:
#   1. Column headers match the profile's declared columns in order.
#   2. Every data row has the correct column count.
#   3. Seeded output is deterministic (two runs with the same seed are byte-identical).
#   4. Seed sensitivity: same profile + filetype, seed 42 vs 1337 → different bytes.
#
# Requires bash 3.2+ (compatible with macOS system bash).

set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

FIXTURES_DIR="$(dirname "$0")/fixtures/profiles/expected-headers"
TEST_OUTPUT_DIR="./results/column-profile-matrix"
COUNT=500

function print_success() { echo -e "\e[42m[ SUCCESS ]\e[0m $1"; }
function print_info()    { echo -e "\e[44m[ INFO    ]\e[0m $1"; }
function print_error()   { echo -e "\e[41m[ ERROR   ]\e[0m $1"; exit 1; }

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

PROFILES=("minimal" "standard" "litigation" "full")
FILETYPES=("pdf" "eml" "tiff" "docx" "xlsx")
SEEDS=(42 1337)

PASSED=0
TOTAL=0

# Extracts column names (one per line) from the first line of a DAT load file.
# DAT uses Unicode U+0014 (ASCII 20) as column delimiter and U+00FE (þ) as quote char.
# Python handles UTF-8 multi-byte sequences correctly; shell tr/sed are unreliable here.
extract_columns() {
    python3 - "$1" <<'PYEOF'
import sys
with open(sys.argv[1], encoding='utf-8') as f:
    line = f.readline().rstrip('\r\n')
for col in line.split('\u0014'):
    print(col.strip('\u00fe'))
PYEOF
}

# Counts rows where the number of fields differs from expected.
count_bad_rows() {
    local dat_file="$1"
    local expected_cols="$2"
    python3 - "$dat_file" "$expected_cols" <<'PYEOF'
import sys
path, expected = sys.argv[1], int(sys.argv[2])
bad = 0
with open(path, encoding='utf-8') as f:
    next(f)  # skip header
    for line in f:
        fields = line.rstrip('\r\n').split('\u0014')
        if len(fields) != expected:
            bad += 1
print(bad)
PYEOF
}

for profile in "${PROFILES[@]}"; do
    expected_file="$FIXTURES_DIR/${profile}.txt"
    if [[ ! -f "$expected_file" ]]; then
        print_error "Missing expected-headers fixture: $expected_file"
    fi

    for filetype in "${FILETYPES[@]}"; do
        for seed in "${SEEDS[@]}"; do
            TOTAL=$((TOTAL + 1))
            combo="${profile}_${filetype}_seed${seed}"
            out_dir="$TEST_OUTPUT_DIR/${combo}"
            mkdir -p "$out_dir"

            print_info "Testing: profile=${profile}  type=${filetype}  seed=${seed}"

            zipper \
                --count "$COUNT" \
                --type "$filetype" \
                --column-profile "$profile" \
                --seed "$seed" \
                --loadfile-only \
                --output-path "$out_dir"

            dat_file=$(find "$out_dir" -name "*.dat" | head -1)
            [[ -z "$dat_file" ]] && print_error "${combo}: No .dat file produced"

            # --- Assertion 1: Column headers match profile declaration in order ---
            actual_cols=$(extract_columns "$dat_file")
            expected_cols=$(cat "$expected_file")
            if [[ "$actual_cols" != "$expected_cols" ]]; then
                echo "ACTUAL:" >&2
                echo "$actual_cols" >&2
                echo "EXPECTED:" >&2
                echo "$expected_cols" >&2
                print_error "${combo}: Column headers do not match expected (${profile}.txt)"
            fi

            # --- Assertion 2: Every row has the correct column count ---
            expected_col_count=$(wc -l < "$expected_file" | tr -d ' ')
            bad_rows=$(count_bad_rows "$dat_file" "$expected_col_count")
            if [[ "$bad_rows" -gt 0 ]]; then
                print_error "${combo}: $bad_rows row(s) have wrong column count (expected $expected_col_count)"
            fi

            # --- Assertion 3: Determinism — re-run with same seed and compare ---
            if [[ "$seed" -eq 42 ]]; then
                rerun_dir="${out_dir}_rerun"
                mkdir -p "$rerun_dir"
                zipper \
                    --count "$COUNT" \
                    --type "$filetype" \
                    --column-profile "$profile" \
                    --seed 42 \
                    --loadfile-only \
                    --output-path "$rerun_dir"

                rerun_dat=$(find "$rerun_dir" -name "*.dat" | head -1)
                if ! diff -q "$dat_file" "$rerun_dat" > /dev/null 2>&1; then
                    print_error "${combo}: Output is not deterministic — two runs with seed=42 differ"
                fi
            fi

            print_success "${combo}: PASSED"
            PASSED=$((PASSED + 1))
        done

        # --- Assertion 4: Seed sensitivity — seed=42 vs seed=1337 differ ---
        TOTAL=$((TOTAL + 1))
        seed42_dat=$(find "$TEST_OUTPUT_DIR/${profile}_${filetype}_seed42" -name "*.dat" | head -1)
        seed1337_dat=$(find "$TEST_OUTPUT_DIR/${profile}_${filetype}_seed1337" -name "*.dat" | head -1)

        if diff -q "$seed42_dat" "$seed1337_dat" > /dev/null 2>&1; then
            print_error "seed-sensitivity (${profile}/${filetype}): seed=42 and seed=1337 produced identical output"
        fi
        print_success "seed-sensitivity (${profile}/${filetype}): seeds produce different bytes"
        PASSED=$((PASSED + 1))
    done
done

echo ""
print_success "Column-profile built-in matrix: ${PASSED}/${TOTAL} assertions passed."
