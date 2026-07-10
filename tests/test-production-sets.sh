#!/bin/bash

# Exit immediately if a command exits with a non-zero status, use unset variable as error, and fail on pipe failures.
set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

# --- Test Configuration ---

TEST_OUTPUT_DIR="./results/production-sets"
PROJECT="src/Zipper.csproj"

# --- Helper Functions ---

function print_success() {
  echo -e "\e[42m[ SUCCESS ]\e[0m $1"
}

function print_info() {
  echo -e "\e[44m[ INFO ]\e[0m $1"
}

function print_error() {
  echo -e "\e[41m[ ERROR ]\e[0m $1"
  exit 1
}

# --- Test Setup ---

print_info "Running Production Sets E2E Test"

# Clean up previous test results
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# --- Test Case 1: Basic Production Set ---

print_info "Test Case 1: Basic production set generation"

zipper \
  --production-set \
  --count 10 \
  --output-path "$TEST_OUTPUT_DIR/test1" \
  --bates-prefix "PROD" \
  --volume-size 3

# Find production dir
prod_dir=$(find "$TEST_OUTPUT_DIR/test1" -type d -name "PRODUCTION_*" -print -quit)

if [[ -z "$prod_dir" ]]; then
  print_error "Test 1: No production directory found."
fi

# Verify structure
if [[ ! -d "$prod_dir/DATA" ]]; then print_error "Missing DATA dir"; fi
if [[ ! -d "$prod_dir/NATIVES" ]]; then print_error "Missing NATIVES dir"; fi
if [[ ! -d "$prod_dir/IMAGES" ]]; then print_error "Missing IMAGES dir"; fi
if [[ ! -d "$prod_dir/TEXT" ]]; then print_error "Missing TEXT dir"; fi

# Verify load files exist
if [[ ! -f "$prod_dir/DATA/loadfile.dat" ]]; then print_error "Missing DAT load file"; fi
if [[ ! -f "$prod_dir/DATA/loadfile.opt" ]]; then print_error "Missing OPT load file"; fi
if [[ ! -f "$prod_dir/_manifest.json" ]]; then print_error "Missing manifest JSON"; fi

# Verify volumes (10 docs / 3 = 4 volumes)
vol1=$(find "$prod_dir/NATIVES" -name "VOL001" -print -quit)
vol4=$(find "$prod_dir/NATIVES" -name "VOL004" -print -quit)
if [[ -z "$vol1" ]]; then print_error "Missing VOL001"; fi
if [[ -z "$vol4" ]]; then print_error "Missing VOL004"; fi

# Verify DAT contents
if ! grep -q "PROD00000001" "$prod_dir/DATA/loadfile.dat"; then
  print_error "Bates start not found in DAT"
fi

print_success "Test Case 1: Basic production set passed"


# --- Test Case 2: Production ZIP ---

print_info "Test Case 2: Production set with --production-zip"

zipper \
  --production-set \
  --production-zip \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test2" \
  --bates-prefix "ZIP"

zip_file=$(find "$TEST_OUTPUT_DIR/test2" -name "*.zip" -print -quit)

if [[ -z "$zip_file" ]]; then
  print_error "Test 2: No ZIP archive generated."
fi

# Make sure manifest exists and matches
prod_dir=$(find "$TEST_OUTPUT_DIR/test2" -type d -name "PRODUCTION_*" -print -quit)
if [[ ! -f "$prod_dir/_manifest.json" ]]; then print_error "Missing manifest JSON"; fi

print_success "Test Case 2: Production ZIP passed"

# --- Test Case 3: Production Set Line Ending and manifest counts ---

print_info "Test Case 3: Production set LF line endings and Attachment counts"

zipper \
  --production-set \
  --type eml \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test3" \
  --bates-prefix "FAM" \
  --attachment-rate 100 \
  --with-families \
  --seed 42 \
  --eol LF

prod_dir=$(find "$TEST_OUTPUT_DIR/test3" -type d -name "PRODUCTION_*" -print -quit)
if [[ -z "$prod_dir" ]]; then
  print_error "Test 3: No production directory found."
fi

python3 - "$prod_dir" <<'PY'
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
for rel in ("DATA/loadfile.dat", "DATA/loadfile.opt"):
    data = (root / rel).read_bytes()
    if b"\r" in data:
        raise SystemExit(f"{rel} contains CR bytes despite --eol LF")

for rel in ("DATA/loadfile_properties.json", "DATA/loadfile.opt_properties.json"):
    doc = json.loads((root / rel).read_text())
    if doc["properties"]["lineEnding"] != "LF":
        raise SystemExit(f"{rel} did not report LF line ending")

manifest = json.loads((root / "_manifest.json").read_text())
actual_natives = len(list((root / "NATIVES").glob("*/*")))
if manifest["nativeFileCount"] != actual_natives:
    raise SystemExit(f"nativeFileCount {manifest['nativeFileCount']} != actual {actual_natives}")
if manifest["parentNativeFileCount"] != 5:
    raise SystemExit("parentNativeFileCount should be 5")
if manifest["attachmentNativeFileCount"] <= 0:
    raise SystemExit("attachmentNativeFileCount should be positive")
if manifest["nativeFileCount"] != manifest["parentNativeFileCount"] + manifest["attachmentNativeFileCount"]:
    raise SystemExit("manifest Native File counts do not add up")
PY

print_success "Test Case 3: Production Set LF line endings and Attachment counts passed"

# --- Test Case 4: Rolling Production Sets - Continuous Mode ---

print_info "Test Case 4: Rolling production sets continuous mode"

zipper \
  --production-set \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test4" \
  --bates-prefix "CONT" \
  --bates-start 10 \
  --production-id "CONT_001" \
  --rolling-count 3 \
  --rolling-bates-mode continuous

# Verify folders CONT_001, CONT_002, CONT_003 exist
if [[ ! -d "$TEST_OUTPUT_DIR/test4/CONT_001" ]]; then print_error "Missing CONT_001 folder"; fi
if [[ ! -d "$TEST_OUTPUT_DIR/test4/CONT_002" ]]; then print_error "Missing CONT_002 folder"; fi
if [[ ! -d "$TEST_OUTPUT_DIR/test4/CONT_003" ]]; then print_error "Missing CONT_003 folder"; fi

# Verify bates ranges:
# CONT_001 should start at CONT00000010 and end at CONT00000014
# CONT_002 should start at CONT00000015 and end at CONT00000019
# CONT_003 should start at CONT00000020 and end at CONT00000024
python3 - "$TEST_OUTPUT_DIR/test4" <<'PY'
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
m1 = json.loads((root / "CONT_001" / "_manifest.json").read_text())
m2 = json.loads((root / "CONT_002" / "_manifest.json").read_text())
m3 = json.loads((root / "CONT_003" / "_manifest.json").read_text())

if m1["productionId"] != "CONT_001": raise SystemExit("m1 prod ID invalid")
if m2["productionId"] != "CONT_002": raise SystemExit("m2 prod ID invalid")
if m3["productionId"] != "CONT_003": raise SystemExit("m3 prod ID invalid")

if m1["rollingSequenceNumber"] != 1: raise SystemExit("m1 rolling seq invalid")
if m2["rollingSequenceNumber"] != 2: raise SystemExit("m2 rolling seq invalid")
if m3["rollingSequenceNumber"] != 3: raise SystemExit("m3 rolling seq invalid")

if m1["batesNumberStart"] != "CONT00000010": raise SystemExit(f"m1 start: {m1['batesNumberStart']}")
if m1["batesNumberEnd"] != "CONT00000014": raise SystemExit("m1 end invalid")
if m2["batesNumberStart"] != "CONT00000015": raise SystemExit("m2 start invalid")
if m2["batesNumberEnd"] != "CONT00000019": raise SystemExit("m2 end invalid")
if m3["batesNumberStart"] != "CONT00000020": raise SystemExit("m3 start invalid")
if m3["batesNumberEnd"] != "CONT00000024": raise SystemExit("m3 end invalid")

if m1["batesRangeMode"] != "continuous": raise SystemExit("m1 mode invalid")
PY

print_success "Test Case 4: Rolling production sets continuous mode passed"


# --- Test Case 5: Rolling Production Sets - Restart Mode ---

print_info "Test Case 5: Rolling production sets restart mode"

zipper \
  --production-set \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test5" \
  --bates-prefix "REST" \
  --bates-start 100 \
  --production-id "REST_A" \
  --rolling-count 2 \
  --rolling-bates-mode restart

if [[ ! -d "$TEST_OUTPUT_DIR/test5/REST_A" ]]; then print_error "Missing REST_A folder"; fi
if [[ ! -d "$TEST_OUTPUT_DIR/test5/REST_A_2" ]]; then print_error "Missing REST_A_2 folder"; fi

python3 - "$TEST_OUTPUT_DIR/test5" <<'PY'
import json
import pathlib
import sys

root = pathlib.Path(sys.argv[1])
m1 = json.loads((root / "REST_A" / "_manifest.json").read_text())
m2 = json.loads((root / "REST_A_2" / "_manifest.json").read_text())

if m1["batesNumberStart"] != "REST00000100" or m1["batesNumberEnd"] != "REST00000104":
    raise SystemExit("m1 range invalid in restart mode")
if m2["batesNumberStart"] != "REST00000100" or m2["batesNumberEnd"] != "REST00000104":
    raise SystemExit("m2 range invalid in restart mode")

if m1["batesRangeMode"] != "restart": raise SystemExit("m1 mode invalid")
PY

print_success "Test Case 5: Rolling production sets restart mode passed"


# --- Test Case 6: Rolling Production Sets - Zip Packaging ---

print_info "Test Case 6: Rolling production sets with zip packaging"

zipper \
  --production-set \
  --production-zip \
  --count 3 \
  --output-path "$TEST_OUTPUT_DIR/test6" \
  --bates-prefix "ROLLZIP" \
  --production-id "ROLLZIP_01" \
  --rolling-count 2

# Verify zip files exist
if [[ ! -f "$TEST_OUTPUT_DIR/test6/ROLLZIP_01.zip" ]]; then print_error "Missing ROLLZIP_01.zip"; fi
if [[ ! -f "$TEST_OUTPUT_DIR/test6/ROLLZIP_02.zip" ]]; then print_error "Missing ROLLZIP_02.zip"; fi

print_success "Test Case 6: Rolling production sets zip packaging passed"


# --- Test Case 7: Successful supplemental Production Set after a prior manifest ---

print_info "Test Case 7: Successful supplemental Production Set after a prior manifest"

# 1. Generate prior
zipper \
  --production-set \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test7_prior" \
  --bates-prefix "SUPP" \
  --bates-start 1

prior_dir=$(find "$TEST_OUTPUT_DIR/test7_prior" -type d -name "PRODUCTION_*" -print -quit)
if [[ -z "$prior_dir" ]]; then
  print_error "Test 7: Prior production directory not found."
fi
prior_manifest="$prior_dir/_manifest.json"

# 2. Generate supplemental
zipper \
  --production-set \
  --supplemental-production \
  --prior-manifest "$prior_manifest" \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test7_supp" \
  --bates-prefix "SUPP" \
  --bates-start 6

supp_dir=$(find "$TEST_OUTPUT_DIR/test7_supp" -type d -name "PRODUCTION_*" -print -quit)
if [[ -z "$supp_dir" ]]; then
  print_error "Test 7: Supplemental production directory not found."
fi
supp_manifest="$supp_dir/_manifest.json"

# Validate manifest content using python
python3 - "$supp_manifest" "$prior_manifest" <<'PY'
import json
import sys

supp_path = sys.argv[1]
prior_path = sys.argv[2]

with open(supp_path, 'r') as f:
    supp = json.load(f)

# check priorManifests is recorded
if "priorManifests" not in supp:
    raise SystemExit("priorManifests missing from manifest")
if prior_path not in supp["priorManifests"]:
    raise SystemExit(f"prior path {prior_path} not found in priorManifests: {supp['priorManifests']}")

# check supplementalValidation is recorded
if "supplementalValidation" not in supp:
    raise SystemExit("supplementalValidation missing from manifest")
val = supp["supplementalValidation"]
if val.get("expectedNextBates") != "SUPP00000006":
    raise SystemExit(f"expectedNextBates was {val.get('expectedNextBates')}, expected SUPP00000006")
if val.get("actualStartingBates") != "SUPP00000006":
    raise SystemExit(f"actualStartingBates was {val.get('actualStartingBates')}, expected SUPP00000006")
PY

print_success "Test Case 7: Successful supplemental Production Set passed"


# --- Test Case 8: Failing duplicate supplemental Bates range ---

print_info "Test Case 8: Failing duplicate supplemental Bates range"

# Generate overlapping/duplicate range which must fail
if zipper \
  --production-set \
  --supplemental-production \
  --prior-manifest "$prior_manifest" \
  --count 5 \
  --output-path "$TEST_OUTPUT_DIR/test8_supp" \
  --bates-prefix "SUPP" \
  --bates-start 4 2>/dev/null; then
  print_error "Test 8: Supplemental generation succeeded but should have failed due to duplicate Bates numbers."
else
  print_success "Test Case 8: Failing duplicate supplemental Bates range passed"
fi
# --- All Tests Passed ---

print_success "All Production Sets E2E tests passed!"
