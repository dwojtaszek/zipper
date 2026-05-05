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

# Extracts column names from the first line of a DAT load file.
# DAT uses ASCII-20 (0x14) as column delimiter and ASCII-254 (0xfe, þ) as quote char.
extract_columns() {
    local dat_file="$1"
    head -1 "$dat_file" | tr '\x14' '\n' | tr -d '\xfe'
}

for profile in "${PROFILES[@]}"; do
    expected_file="$FIXTURES_DIR/${profile}.txt"
    if [[ ! -f "$expected_file" ]]; then
        print_error "Missing expected-headers fixture: $expected_file"
    fi

    # Collect one reference run per (profile, filetype) pair for seed-sensitivity check
    declare -A seed42_files

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
            # Skip header line; for each data line count delimiters (cols = delimiters + 1)
            bad_rows=$(tail -n +2 "$dat_file" | awk -v expected="$expected_col_count" '
                BEGIN { FS = "\x14"; bad = 0 }
                NF > 0 && NF != expected { bad++ }
                END { print bad }
            ')
            if [[ "$bad_rows" -gt 0 ]]; then
                print_error "${combo}: $bad_rows row(s) have wrong column count (expected $expected_col_count)"
            fi

            # --- Assertion 3: Determinism — save path for second run ---
            if [[ "$seed" -eq 42 ]]; then
                seed42_files["$filetype"]="$dat_file"

                # Re-run with same seed and compare byte-for-byte
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
        seed42_dat="${seed42_files[$filetype]}"
        seed1337_dir="$TEST_OUTPUT_DIR/${profile}_${filetype}_seed1337"
        seed1337_dat=$(find "$seed1337_dir" -name "*.dat" | head -1)

        if diff -q "$seed42_dat" "$seed1337_dat" > /dev/null 2>&1; then
            print_error "seed-sensitivity (${profile}/${filetype}): seed=42 and seed=1337 produced identical output"
        fi
        print_success "seed-sensitivity (${profile}/${filetype}): seeds produce different bytes"
        PASSED=$((PASSED + 1))
    done

    unset seed42_files
    declare -A seed42_files
done

echo ""
print_success "Column-profile built-in matrix: ${PASSED}/${TOTAL} assertions passed."
