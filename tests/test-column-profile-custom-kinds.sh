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

# Expected columns from the fixture profile (same order as the JSON).
EXPECTED_COLS="DOCID
TEXTFIELD
LONGTEXTFIELD
DATEFIELD
DATETIMEFIELD
NUMBERFIELD
NUMBERGAUSSIAN
NUMBEREXPONENTIAL
NUMBERPARETO
BOOLEANFIELD
CODEDFIELD
CODEDWEIGHTED
EMAILFIELD
EMAILMULTI"

# --- Assertion 1: Header columns match fixture declaration ---
print_info "Checking column headers..."
actual_cols=$(python3 - "$dat_file" <<'PYEOF'
import sys
with open(sys.argv[1], encoding='utf-8') as f:
    line = f.readline().rstrip('\r\n')
for col in line.split('\u0014'):
    print(col.strip('\u00fe'))
PYEOF
)
if [[ "$actual_cols" != "$EXPECTED_COLS" ]]; then
    echo "ACTUAL:   $actual_cols" >&2
    echo "EXPECTED: $EXPECTED_COLS" >&2
    print_error "Header mismatch against expected columns"
fi
print_success "Headers match"

# Parse data rows into Python-friendly format and run all per-kind assertions in one pass.
print_info "Running per-kind invariant checks..."
python3 - "$dat_file" "$TEST_OUTPUT_DIR" <<'PYEOF'
import sys, os, re, csv

dat_path = sys.argv[1]
out_dir  = sys.argv[2]
COL_SEP  = '\u0014'
QUOTE    = '\u00fe'

# Read and parse all rows
rows = []
with open(dat_path, encoding='utf-8') as f:
    header_line = f.readline().rstrip('\r\n')
    headers = [c.strip(QUOTE) for c in header_line.split(COL_SEP)]
    for line in f:
        line = line.rstrip('\r\n')
        if not line:
            continue
        fields = [c.strip(QUOTE) for c in line.split(COL_SEP)]
        rows.append(dict(zip(headers, fields)))

assert len(rows) > 0, "No data rows found"
print(f"  Parsed {len(rows)} rows with {len(headers)} columns each")

errors = []

# Check row column count
for i, row in enumerate(rows):
    if len(row) != 14:
        errors.append(f"Row {i+1}: expected 14 cols, got {len(row)}")
if errors:
    for e in errors[:5]:
        print(f"  ERROR: {e}", file=sys.stderr)
    raise SystemExit(f"Column count errors: {len(errors)}")
print("  [OK] All rows have 14 columns")

# DOCID: unique, matches DOC[0-9]+
docids = [r['DOCID'] for r in rows]
if len(set(docids)) != len(docids):
    raise SystemExit(f"DOCID has {len(docids)-len(set(docids))} duplicate(s)")
bad_docid = [d for d in docids if not re.match(r'^DOC[0-9]+$', d)]
if bad_docid:
    raise SystemExit(f"DOCID has {len(bad_docid)} value(s) not matching DOC[0-9]+: {bad_docid[:3]}")
print("  [OK] DOCID: unique, matches DOC[0-9]+")

# TEXTFIELD (text, required): all non-empty
empty_text = sum(1 for r in rows if not r.get('TEXTFIELD'))
if empty_text:
    raise SystemExit(f"TEXTFIELD has {empty_text} empty value(s) (required=true)")
print("  [OK] TEXTFIELD: all non-empty")

# LONGTEXTFIELD (longtext, required): all >= 10 chars
short = [r['LONGTEXTFIELD'] for r in rows if len(r.get('LONGTEXTFIELD', '')) < 10]
if short:
    raise SystemExit(f"LONGTEXTFIELD has {len(short)} value(s) shorter than 10 chars")
print("  [OK] LONGTEXTFIELD: all >= 10 chars")

# DATEFIELD (date, yyyy-MM-dd)
import datetime
bad_date = []
for r in rows:
    v = r.get('DATEFIELD', '')
    if v:
        try:
            d = datetime.datetime.strptime(v, '%Y-%m-%d')
            if not (2018 <= d.year <= 2025):
                bad_date.append(v)
        except ValueError:
            bad_date.append(v)
if bad_date:
    raise SystemExit(f"DATEFIELD has {len(bad_date)} invalid value(s): {bad_date[:3]}")
print("  [OK] DATEFIELD: all yyyy-MM-dd within 2018-2024")

# DATETIMEFIELD (datetime, ISO 8601)
bad_dt = [r['DATETIMEFIELD'] for r in rows if r.get('DATETIMEFIELD') and 'T' not in r['DATETIMEFIELD']]
if bad_dt:
    raise SystemExit(f"DATETIMEFIELD has {len(bad_dt)} values without time component: {bad_dt[:3]}")
print("  [OK] DATETIMEFIELD: all contain 'T' (ISO 8601)")

# NUMBERFIELD (uniform, 0-10000)
bad_num = []
for r in rows:
    v = r.get('NUMBERFIELD', '')
    if v:
        try:
            n = int(v)
            if not (0 <= n <= 10000):
                bad_num.append(v)
        except ValueError:
            bad_num.append(v)
if bad_num:
    raise SystemExit(f"NUMBERFIELD has {len(bad_num)} out-of-range value(s): {bad_num[:3]}")
print("  [OK] NUMBERFIELD: all integers in [0,10000]")

# BOOLEANFIELD (boolean, YN)
valid_bool = {'Y', 'N'}
bad_bool = [r['BOOLEANFIELD'] for r in rows if r.get('BOOLEANFIELD') and r['BOOLEANFIELD'] not in valid_bool]
if bad_bool:
    raise SystemExit(f"BOOLEANFIELD has {len(bad_bool)} invalid value(s): {bad_bool[:3]}")
print("  [OK] BOOLEANFIELD: all Y or N")

# CODEDFIELD (coded, from statusValues)
valid_coded = {'Active', 'Inactive', 'Pending', 'Closed', 'Archived'}
bad_coded = [r['CODEDFIELD'] for r in rows if r.get('CODEDFIELD') and r['CODEDFIELD'] not in valid_coded]
if bad_coded:
    raise SystemExit(f"CODEDFIELD has {len(bad_coded)} invalid value(s): {bad_coded[:3]}")
print("  [OK] CODEDFIELD: all non-empty values in declared set")

# EMAILFIELD (email, single)
email_re = re.compile(r'^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$')
bad_email = [r['EMAILFIELD'] for r in rows if r.get('EMAILFIELD') and not email_re.match(r['EMAILFIELD'])]
if bad_email:
    raise SystemExit(f"EMAILFIELD has {len(bad_email)} invalid value(s): {bad_email[:3]}")
print("  [OK] EMAILFIELD: all non-empty values match email pattern")

# EMAILMULTI (email, multi-value): some rows should have ';'
multi_rows = sum(1 for r in rows if ';' in r.get('EMAILMULTI', ''))
if multi_rows == 0:
    raise SystemExit("EMAILMULTI has no multi-value rows (expected some with ';' separator)")
print(f"  [OK] EMAILMULTI: {multi_rows} row(s) have multiple values")

print("All per-kind invariants PASSED")
PYEOF

print_success "Per-kind invariants: all PASSED"

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
