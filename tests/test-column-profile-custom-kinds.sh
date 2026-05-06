#!/bin/bash
# E2E tests for --column-profile with a custom JSON profile exercising every
# Column Kind (identifier, text, longtext, date, datetime, number, boolean,
# coded, email) and every Distribution Pattern (uniform, gaussian, exponential,
# pareto, weighted).
#
# Uses: tests/fixtures/profiles/test-every-kind.json

set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

PROFILE_FILE="$(dirname "$0")/fixtures/profiles/test-every-kind.json"
TEST_OUTPUT_DIR="./results/column-profile-custom-kinds"
COUNT=2000
SEED=42

function print_success() { echo -e "\e[42m[ SUCCESS ]\e[0m $1"; }
function print_info()    { echo -e "\e[44m[ INFO    ]\e[0m $1"; }
function print_error()   { echo -e "\e[41m[ ERROR   ]\e[0m $1"; exit 1; }

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

print_info "Generating load file with custom every-kind profile (count=$COUNT, seed=$SEED)..."

zipper \
    --count "$COUNT" \
    --type pdf \
    --column-profile "$PROFILE_FILE" \
    --seed "$SEED" \
    --loadfile-only \
    --output-path "$TEST_OUTPUT_DIR/run1"

dat_file=$(find "$TEST_OUTPUT_DIR/run1" -name "*.dat" | head -1)
[[ -z "$dat_file" ]] && print_error "No .dat file produced"

# Expected columns from the fixture profile (same order as the JSON)
EXPECTED_COLS=(
    DOCID TEXTFIELD LONGTEXTFIELD DATEFIELD DATETIMEFIELD
    NUMBERFIELD NUMBERGAUSSIAN NUMBEREXPONENTIAL NUMBERPARETO
    BOOLEANFIELD CODEDFIELD CODEDWEIGHTED EMAILFIELD EMAILMULTI
)

# --- Assertion 1: Header columns match fixture declaration ---
print_info "Checking column headers..."
actual_cols=$(head -1 "$dat_file" | tr $'\x14' $'\n' | tr -d $'\xfe\r')
expected_joined=$(printf '%s\n' "${EXPECTED_COLS[@]}")
if [[ "$actual_cols" != "$expected_joined" ]]; then
    echo "ACTUAL:   $actual_cols" >&2
    echo "EXPECTED: $expected_joined" >&2
    print_error "Header mismatch against expected columns"
fi
print_success "Headers match"

# --- Assertion 2: Every row has exactly 14 columns ---
print_info "Checking column count per row..."
bad_rows=$(tail -n +2 "$dat_file" | awk -F$'\x14' 'NF != 14 { bad++ } END { print bad+0 }')
[[ "$bad_rows" -gt 0 ]] && print_error "$bad_rows row(s) have wrong column count (expected 14)"
print_success "All rows have 14 columns"

# --- Assertion 3: Per-kind invariants ---
# Parse data rows into a TSV-like format (replace DAT delimiter with TAB, strip quotes)
tmp_tsv="$TEST_OUTPUT_DIR/data.tsv"
tail -n +2 "$dat_file" | tr -d $'\xfe\r' | tr $'\x14' $'\t' > "$tmp_tsv"

# DOCID (identifier): unique, matches DOC########
print_info "Checking DOCID (identifier) invariants..."
dup_count=$(awk -F'\t' '{print $1}' "$tmp_tsv" | sort | uniq -d | wc -l)
[[ "$dup_count" -gt 0 ]] && print_error "DOCID has $dup_count duplicate value(s)"
bad_docid=$(awk -F'\t' '{if ($1 !~ /^DOC[0-9]+$/) print $1}' "$tmp_tsv" | wc -l)
[[ "$bad_docid" -gt 0 ]] && print_error "DOCID has $bad_docid value(s) not matching DOC[0-9]+"
print_success "DOCID: unique and matches DOC[0-9]+"

# TEXTFIELD (text, required=true, emptyPercentage=0 via settings): all non-empty
print_info "Checking TEXTFIELD (text, required) is non-empty..."
empty_text=$(awk -F'\t' '{if ($2 == "") count++} END {print count+0}' "$tmp_tsv")
[[ "$empty_text" -gt 0 ]] && print_error "TEXTFIELD has $empty_text empty value(s) (required=true)"
print_success "TEXTFIELD: all non-empty"

# LONGTEXTFIELD (longtext, required=true): all values >= 10 chars
print_info "Checking LONGTEXTFIELD (longtext) length..."
short_longtext=$(awk -F'\t' '{if (length($3) < 10) count++} END {print count+0}' "$tmp_tsv")
[[ "$short_longtext" -gt 0 ]] && print_error "LONGTEXTFIELD has $short_longtext value(s) shorter than 10 chars"
print_success "LONGTEXTFIELD: all values >= 10 chars"

# DATEFIELD (date, yyyy-MM-dd): parseable and within 2018-2024
print_info "Checking DATEFIELD (date) format..."
bad_date=$(awk -F'\t' '
    {
        if ($4 !~ /^[0-9]{4}-[0-9]{2}-[0-9]{2}$/) { bad++; next }
        y=substr($4,1,4); if (y < 2018 || y > 2025) bad++
    }
    END { print bad+0 }
' "$tmp_tsv")
[[ "$bad_date" -gt 0 ]] && print_error "DATEFIELD has $bad_date value(s) with invalid format or out-of-range"
print_success "DATEFIELD: all values are yyyy-MM-dd within 2018-2024"

# DATETIMEFIELD (datetime): parseable ISO 8601
print_info "Checking DATETIMEFIELD (datetime) format..."
bad_datetime=$(awk -F'\t' '
    {
        if ($5 !~ /^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$/) bad++
    }
    END { print bad+0 }
' "$tmp_tsv")
[[ "$bad_datetime" -gt 0 ]] && print_error "DATETIMEFIELD has $bad_datetime value(s) with invalid ISO 8601 format"
print_success "DATETIMEFIELD: all values match ISO 8601 datetime"

# NUMBERFIELD (uniform, 0-10000): integers within range
print_info "Checking NUMBERFIELD (number, uniform)..."
bad_num=$(awk -F'\t' '{if ($6 !~ /^[0-9]+$/ || $6+0 < 0 || $6+0 > 10000) bad++} END {print bad+0}' "$tmp_tsv")
[[ "$bad_num" -gt 0 ]] && print_error "NUMBERFIELD has $bad_num value(s) out of [0,10000]"
print_success "NUMBERFIELD: all integers in [0,10000]"

# BOOLEANFIELD (boolean, YN format): values are Y or N
print_info "Checking BOOLEANFIELD (boolean)..."
bad_bool=$(awk -F'\t' '{if ($10 != "Y" && $10 != "N") bad++} END {print bad+0}' "$tmp_tsv")
[[ "$bad_bool" -gt 0 ]] && print_error "BOOLEANFIELD has $bad_bool value(s) that are not Y or N"
print_success "BOOLEANFIELD: all values are Y or N"

# CODEDFIELD (coded, from statusValues): values in declared set
print_info "Checking CODEDFIELD (coded)..."
bad_coded=$(awk -F'\t' '
    BEGIN { valid["Active"]=1; valid["Inactive"]=1; valid["Pending"]=1; valid["Closed"]=1; valid["Archived"]=1 }
    { if ($11 != "" && !($11 in valid)) bad++ }
    END { print bad+0 }
' "$tmp_tsv")
[[ "$bad_coded" -gt 0 ]] && print_error "CODEDFIELD has $bad_coded value(s) not in declared coded set"
print_success "CODEDFIELD: all non-empty values are in declared set"

# EMAILFIELD (email, single): matches simple RFC pattern
print_info "Checking EMAILFIELD (email) format..."
bad_email=$(awk -F'\t' '
    { if ($13 != "" && $13 !~ /@.*\./) bad++ }
    END { print bad+0 }
' "$tmp_tsv")
[[ "$bad_email" -gt 0 ]] && print_error "EMAILFIELD has $bad_email value(s) not matching email pattern"
print_success "EMAILFIELD: all non-empty values match email pattern"

# EMAILMULTI (email, multi-value): some rows contain ; separator
print_info "Checking EMAILMULTI (email, multi-value) has some multi-value rows..."
multi_count=$(awk -F'\t' '{if ($14 ~ /;/) count++} END {print count+0}' "$tmp_tsv")
[[ "$multi_count" -eq 0 ]] && print_error "EMAILMULTI has no multi-value rows (expected some with ';' separator)"
print_success "EMAILMULTI: $multi_count row(s) have multiple email values"

# --- Assertion 4: Determinism ---
print_info "Verifying determinism (re-run with seed=$SEED)..."
zipper \
    --count "$COUNT" \
    --type pdf \
    --column-profile "$PROFILE_FILE" \
    --seed "$SEED" \
    --loadfile-only \
    --output-path "$TEST_OUTPUT_DIR/run2"

dat_file2=$(find "$TEST_OUTPUT_DIR/run2" -name "*.dat" | head -1)
if ! diff -q "$dat_file" "$dat_file2" > /dev/null 2>&1; then
    print_error "Determinism check failed: two runs with seed=$SEED differ"
fi
print_success "Determinism: two runs with seed=$SEED are byte-identical"

echo ""
print_success "Custom every-kind profile tests PASSED."
