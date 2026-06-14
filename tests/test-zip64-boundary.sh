#!/bin/bash

# E2E test: Zip64 boundary (REQ_E-015: >65,535 files / >4GB)
# Validates that archives exceeding 65,535 files cross into the Zip64 format correctly.

set -euo pipefail

source "$(dirname "$0")/_zipper-cli.sh"

TEST_OUTPUT_DIR="./results/zip64-boundary"
PASSED=0
FAILED=0

function print_info() { local msg="$1"; echo -e "\033[44m[ INFO ]\033[0m $msg"; }
function print_success() { local msg="$1"; echo -e "\033[42m[ SUCCESS ]\033[0m $msg"; }
function print_error() { local msg="$1"; echo -e "\033[41m[ ERROR ]\033[0m $msg" >&2; }

rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

COUNT=70000

print_info "Generating archive with $COUNT files to cross 65,535 boundary..."

# Use zipper command defined in _zipper-cli.sh
zipper --type jpg --count "$COUNT" --output-path "$TEST_OUTPUT_DIR" --seed 42 

ZIP_FILE=$(find "$TEST_OUTPUT_DIR" -name "*.zip" -print | head -n 1 || true)

if [[ -z "$ZIP_FILE" ]]; then
    print_error "No zip file generated"
    exit 1
fi

print_info "Found generated archive: $ZIP_FILE"

# 1. Entry count assertions using unzip -Z1
JPG_COUNT=$(unzip -Z1 "$ZIP_FILE" | awk '/\.jpg$/ {count++} END {print count+0}')
TOTAL_COUNT=$(unzip -Z1 "$ZIP_FILE" | wc -l | xargs)

if [[ "$JPG_COUNT" -eq 70000 ]]; then
    print_info "Assertion OK: unzip -Z1 found 70000 jpg files"
    PASSED=$((PASSED + 1))
else
    print_error "Assertion FAILED: unzip -Z1 found $JPG_COUNT jpg files, expected 70000"
    FAILED=$((FAILED + 1))
fi

if [[ "$TOTAL_COUNT" -eq 70000 ]]; then
    print_info "Assertion OK: unzip -Z1 found 70000 total entries"
    PASSED=$((PASSED + 1))
else
    print_error "Assertion FAILED: unzip -Z1 found $TOTAL_COUNT total entries, expected 70000"
    FAILED=$((FAILED + 1))
fi

# 2. Structural validity by a second tool
if unzip -t "$ZIP_FILE" ; then
    print_info "Assertion OK: unzip -t exited 0 (structurally valid)"
    PASSED=$((PASSED + 1))
else
    print_error "Assertion FAILED: unzip -t exited non-zero"
    FAILED=$((FAILED + 1))
fi

# 3. Zip64 actually engaged: python3 independent parser check
PY_COUNT=$(python3 -c "import zipfile,sys; z=zipfile.ZipFile(sys.argv[1]); print(len(z.infolist()))" "$ZIP_FILE" 2>/dev/null || echo "ERROR")
if [[ "$PY_COUNT" == "70000" ]]; then
    print_info "Assertion OK: Python zipfile parser found 70000 entries (Zip64 format engaged properly)"
    PASSED=$((PASSED + 1))
else
    print_error "Assertion FAILED: Python zipfile parser returned $PY_COUNT, expected 70000"
    FAILED=$((FAILED + 1))
fi

# 4. Load-file row count (.opt file generated alongside)
DAT_FILE="${ZIP_FILE%.zip}.opt"

if [[ -f "$DAT_FILE" ]]; then
    # Load files have a header row, so 70000 files = 70001 lines
    DAT_LINES=$(wc -l < "$DAT_FILE" | xargs)
    if [[ "$DAT_LINES" -eq 70001 ]]; then
        print_info "Assertion OK: Load file (.opt) has 70001 lines"
        PASSED=$((PASSED + 1))
    else
        print_error "Assertion FAILED: Load file has $DAT_LINES lines, expected 70001"
        FAILED=$((FAILED + 1))
    fi
else
    print_error "Assertion FAILED: Could not find .opt load file alongside archive"
    FAILED=$((FAILED + 1))
fi

# 5. >4GB target size case (Behind run_4gb flag)
if [[ "${RUN_4GB_CASE:-false}" == "true" ]]; then
    print_info "Running 4GB target size Zip64 boundary test..."
    OUT_4GB="$TEST_OUTPUT_DIR/4gb"
    mkdir -p "$OUT_4GB"
    
    zipper --type pdf --count 10 --target-zip-size 4500MB --output-path "$OUT_4GB" --seed 42 
    
    ZIP_4GB=$(find "$OUT_4GB" -name "*.zip" -print | head -n 1 || true)
    if [[ -z "$ZIP_4GB" ]]; then
        print_error "No 4GB zip file generated"
        FAILED=$((FAILED + 1))
    else
        SIZE=$(wc -c < "$ZIP_4GB" | xargs)
        if (( SIZE > 4294967296 )); then
            print_info "Assertion OK: 4GB archive generated successfully ($SIZE bytes)"
            PASSED=$((PASSED + 1))
            
            if unzip -t "$ZIP_4GB" ; then
                print_info "Assertion OK: unzip -t exited 0 on 4GB archive"
                PASSED=$((PASSED + 1))
            else
                print_error "Assertion FAILED: unzip -t failed on 4GB archive"
                FAILED=$((FAILED + 1))
            fi
        else
            print_error "Assertion FAILED: 4GB archive was too small ($SIZE bytes)"
            FAILED=$((FAILED + 1))
        fi
    fi
fi

# --- Summary ---
echo ""
TOTAL=$((PASSED + FAILED))
if [[ "$FAILED" -eq 0 ]]; then
    print_success "All Zip64 boundary tests passed! ($PASSED/$TOTAL)"
else
    print_error "Zip64 boundary tests: $FAILED/$TOTAL FAILED"
    exit 1
fi
