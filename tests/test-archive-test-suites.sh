#!/bin/bash

# Archive Test workflow E2E (tickets #844/#846): CLI interactions for
# --archive-test-suite / --archive-test-cases — publication, validation
# failures, exit codes, frozen smoke membership, replay determinism, and
# independent verification. Uses the shared zipper CLI helper.

set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

TEST_OUTPUT_DIR="./results/archive-test-suites"
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

SMOKE_CASES=5    # the frozen smoke set (#834): valid-empty, valid-stored,
                 # valid-deflate, crc-both-mismatch, missing-eocd
ALL_CASES=103    # the frozen complete-catalogue size (#834, +1 #869, +1 #871, +1 #872, +2 #873, +3 #874, +2 #876, +1 #880, +1 #882, +1 #877, +1 #897, +1 #898, +1 consolidated #885/#886/#879, +2 #875, +1 #884, +1 #889, +5 #899, +6 #900, +1 #931, +5 #933, +2 #934 Zip64 descriptors, +7 #934 matrix, +1 #934 clean Unicode Path, +1 #935 nested budget, +2 #935 mixed, +1 #888, +1 #870, +2 #878): every unique Case Key

# Stored/header-only byte goldens (#846): stored entries plus fixed timestamps
# are byte-stable across runtimes, so the Fixture IDs are frozen and asserted on
# every CI platform. Deflate has no cross-runtime promise; it is replay-checked
# for same-run identity only. Golden mirrored in ArchiveTestSuiteReplayTests.cs,
# tests/test-archive-test-suites.bat, and docs/archive-test-suites.md.
FROZEN_VALID_STORED_ID=atc-d4998f7846f15ca3c2d709a07579a0ba6723f5f7b524ca39953622407b90a24a
FROZEN_VALID_EMPTY_ID=atc-860f76f376dcb6f212fd080f8ec5dc5e454388b779a8fb5dbdff1d49cde3e950

failures=0

function check() {
  local code="$1"
  local message="$2"
  if [[ "$code" -eq 0 ]]; then
    echo -e "\e[42m[ SUCCESS ]\e[0m $message"
  else
    echo -e "\e[41m[ ERROR ]\e[0m $message"
    failures=$((failures + 1))
  fi
}

# Guarded numeric assertion (#846): a failing condition records the failure and
# keeps running the remaining checks — never a bare `[ ... ]; check $?`, which
# `set -e` aborts before the failure is recorded.
function assert_eq() {
  local actual="$1"
  local expected="$2"
  local message="$3"
  if [[ "$actual" -eq "$expected" ]]; then
    check 0 "$message"
  else
    check 1 "$message"
  fi
}

# 1. Pinned Ajv restore + prerequisite (tickets #846/#932): restore the frozen
#    Ajv CLI explicitly with `npm ci` when missing — the only network step —
#    then validate the committed frozen vector. Verification itself runs the
#    restored package via node directly and never downloads; a missing restore
#    fails instead of skipping.
if [[ ! -f tests/archive-tests/node_modules/ajv-cli/dist/index.js ]]; then
  if ! command -v npm >/dev/null 2>&1; then
    check 1 "npm must be installed to restore the pinned Ajv CLI"
  elif ! npm ci --ignore-scripts --prefix tests/archive-tests >/dev/null 2>&1; then
    check 1 "npm ci must restore the pinned Ajv CLI"
  else
    check 0 "pinned Ajv CLI restored via npm ci"
  fi
else
  check 0 "pinned Ajv CLI already restored"
fi
if python3 -c 'import importlib.util, sys
spec = importlib.util.spec_from_file_location("vf", "tests/archive-tests/verify-fixtures.py")
vf = importlib.util.module_from_spec(spec)
spec.loader.exec_module(vf)
v = vf.AjvValidator(vf.DEFAULT_SCHEMA, vf.DEFAULT_AJV)
err = v.check_prerequisite()
if err:
    sys.stderr.write(err + "\n")
    sys.exit(1)
v.validate("tests/fixtures/archive-tests/valid-empty.json")' >/dev/null 2>&1; then
  check 0 "pinned Ajv prerequisite installed and validated the frozen vector"
else
  check 1 "pinned Ajv prerequisite must be installed and validate the frozen vector"
fi

# 2. Smoke suite publication: the five frozen Case Keys as flat pairs.
OUT="$TEST_OUTPUT_DIR/smoke"
zipper --archive-test-suite smoke --seed 42 --output-path "$OUT"
files=$(find "$OUT" -type f | wc -l)
assert_eq "$files" $((SMOKE_CASES * 2)) "smoke suite published $files files ($SMOKE_CASES pairs)"

zips=$(find "$OUT" -maxdepth 1 -name '*.zip' | wc -l)
jsons=$(find "$OUT" -maxdepth 1 -name '*.json' | wc -l)
if [[ "$zips" -eq "$SMOKE_CASES" ]] && [[ "$jsons" -eq "$SMOKE_CASES" ]]; then
  check 0 "smoke suite published $SMOKE_CASES .zip and $SMOKE_CASES .json files"
else
  check 1 "smoke suite published $zips .zip and $jsons .json files (expected $SMOKE_CASES each)"
fi

flat=$(find "$OUT" -mindepth 2 -print | wc -l)
assert_eq "$flat" 0 "no subfolders or outer Archive"

names_ok=1
for json in "$OUT"/*.json; do
  base=$(basename "$json" .json)
  grep -Eq "\"fixtureId\": *\"$base\"" "$json" || names_ok=0
  grep -Eq "\"fileName\": *\"$base\.zip\"" "$json" || names_ok=0
  [[ -f "$OUT/$base.zip" ]] || names_ok=0
done
assert_eq "$names_ok" 1 "JSON fixtureId/fileName match basenames"

if [[ -f "$OUT/$FROZEN_VALID_STORED_ID.zip" ]] && [[ -f "$OUT/$FROZEN_VALID_STORED_ID.json" ]]; then
  check 0 "stored control matches the frozen cross-platform Fixture ID"
else
  check 1 "stored control must match the frozen cross-platform Fixture ID"
fi
if [[ -f "$OUT/$FROZEN_VALID_EMPTY_ID.zip" ]] && [[ -f "$OUT/$FROZEN_VALID_EMPTY_ID.json" ]]; then
  check 0 "empty control matches the frozen Fixture ID"
else
  check 1 "empty control must match the frozen Fixture ID"
fi

# 3. Unopenable Archive with a still-readable Expectation File: the missing-eocd
#    smoke member cannot be opened by a reference reader, yet its JSON parses
#    standalone — the external-sidecar rationale in practice.
ME_JSON=$(python3 -c 'import glob,json,sys
print(next((p for p in glob.glob(sys.argv[1]+"/*.json") if json.load(open(p))["caseKey"]=="missing-eocd"), ""))' "$OUT" || true)
if [[ -n "$ME_JSON" ]]; then
  if python3 -c 'import sys,zipfile; zipfile.ZipFile(sys.argv[1])' "${ME_JSON%.json}.zip" >/dev/null 2>&1; then
    check 1 "missing-eocd Archive must be unopenable by a reference reader"
  else
    check 0 "missing-eocd Archive is unopenable by a reference reader"
  fi
  if python3 -c 'import json,sys; json.load(open(sys.argv[1]))' "$ME_JSON" >/dev/null 2>&1; then
    check 0 "the Expectation File of an unopenable Archive is still readable standalone"
  else
    check 1 "the Expectation File of an unopenable Archive must be readable standalone"
  fi
else
  check 1 "smoke suite must contain the missing-eocd Case Key"
fi

# 4. Case selection: exactly the named Case Keys.
OUT="$TEST_OUTPUT_DIR/two-cases"
zipper --archive-test-suite malformed --archive-test-cases crc-both-mismatch,missing-eocd --seed 42 --output-path "$OUT"
files=$(find "$OUT" -type f | wc -l)
assert_eq "$files" 4 "case selection published exactly 2 pairs"

# 5. Replay determinism: the same selection into a new directory repeats the
#    Fixture IDs, and a full smoke replay into a fresh directory is byte-identical
#    (Archive bytes and Expectation File bytes both).
OUT2="$TEST_OUTPUT_DIR/two-cases-replay"
zipper --archive-test-suite malformed --archive-test-cases missing-eocd,crc-both-mismatch --seed 42 --output-path "$OUT2"
if diff <(ls "$OUT" | sort) <(ls "$OUT2" | sort) >/dev/null; then
  check 0 "reordered selection repeats identical Fixture IDs"
else
  check 1 "reordered selection must repeat identical Fixture IDs"
fi

SMOKE_REPLAY="$TEST_OUTPUT_DIR/smoke-replay"
zipper --archive-test-suite smoke --seed 42 --output-path "$SMOKE_REPLAY"
if python3 -c 'import os,filecmp,sys
a,b=sys.argv[1:3]
names=set(os.listdir(a))
sys.exit(0 if names==set(os.listdir(b)) and all(filecmp.cmp(os.path.join(a,n),os.path.join(b,n),shallow=False) for n in names) else 1)' \
     "$TEST_OUTPUT_DIR/smoke" "$SMOKE_REPLAY" >/dev/null 2>&1; then
  check 0 "fresh-directory smoke replay is byte-identical"
else
  check 1 "fresh-directory smoke replay must be byte-identical"
fi

# 6. Repeated run into the same directory fails (exit 1) and preserves the pairs.
if zipper --archive-test-suite smoke --output-path "$TEST_OUTPUT_DIR/smoke" >/dev/null 2>&1; then
  check 1 "repeated run into existing directory must fail"
else
  preserved=$(find "$TEST_OUTPUT_DIR/smoke" -type f | wc -l)
  assert_eq "$preserved" $((SMOKE_CASES * 2)) "failed repeated run preserved the first run"
fi

# 7. Closed flag set: a generation flag is rejected even at its default value.
if zipper --archive-test-suite smoke --folders 1 --output-path "$TEST_OUTPUT_DIR/x" >/dev/null 2>&1; then
  check 1 "generation flag alongside --archive-test-suite must fail"
else
  check 0 "generation flag alongside --archive-test-suite rejected"
fi

# 8. --benchmark / --chaos-list mixing is rejected before their early exits.
if zipper --archive-test-suite smoke --benchmark >/dev/null 2>&1; then
  check 1 "--archive-test-suite with --benchmark must fail"
else
  check 0 "--archive-test-suite with --benchmark rejected"
fi

if zipper --archive-test-suite smoke --chaos-list >/dev/null 2>&1; then
  check 1 "--archive-test-suite with --chaos-list must fail"
else
  check 0 "--archive-test-suite with --chaos-list rejected"
fi

# 9. Validation failures: unknown suite, unknown Case Key, missing --output-path.
if zipper --archive-test-suite bogus --output-path "$TEST_OUTPUT_DIR/x" >/dev/null 2>&1; then
  check 1 "unknown suite must fail"
else
  check 0 "unknown suite rejected"
fi

if zipper --archive-test-suite smoke --archive-test-cases no-such-case --output-path "$TEST_OUTPUT_DIR/x" >/dev/null 2>&1; then
  check 1 "unknown Case Key must fail"
else
  check 0 "unknown Case Key rejected"
fi

if zipper --archive-test-suite smoke >/dev/null 2>&1; then
  check 1 "missing --output-path must fail"
else
  check 0 "missing --output-path rejected"
fi

# 10. Independent verifier: the published pairs pass cross-implementation
#     verification (schema, identity, mutation audit, reader operations; ticket
#     #845). The complete catalogue must publish exactly one pair per unique
#     Case Key and verify every pair — unsafe policy fixtures are never extracted.
if python3 tests/archive-tests/verify-fixtures.py "$TEST_OUTPUT_DIR/smoke" \
     --report "$TEST_OUTPUT_DIR/smoke-verification.json" >/dev/null 2>&1; then
  check 0 "independent verifier passed the smoke pairs"
else
  check 1 "independent verifier must pass the smoke pairs"
fi

if python3 tests/archive-tests/verify-fixtures.py "$TEST_OUTPUT_DIR/two-cases" \
     --report "$TEST_OUTPUT_DIR/two-cases-verification.json" >/dev/null 2>&1; then
  check 0 "independent verifier passed the malformed selection"
else
  check 1 "independent verifier must pass the malformed selection"
fi

ALL_OUT="$TEST_OUTPUT_DIR/all"
zipper --archive-test-suite all --seed 42 --output-path "$ALL_OUT"
all_files=$(find "$ALL_OUT" -type f | wc -l)
all_zips=$(find "$ALL_OUT" -maxdepth 1 -name '*.zip' | wc -l)
all_jsons=$(find "$ALL_OUT" -maxdepth 1 -name '*.json' | wc -l)
distinct=$(python3 -c 'import glob,json,sys
print(len({json.load(open(p))["caseKey"] for p in glob.glob(sys.argv[1]+"/*.json")}))' "$ALL_OUT" || true)
distinct=${distinct:-0}
assert_eq "$distinct" "$ALL_CASES" "all suite published the frozen $ALL_CASES unique Case Keys (found $distinct)"
if [[ "$all_files" -eq $((distinct * 2)) ]] && [[ "$all_zips" -eq "$distinct" ]] && [[ "$all_jsons" -eq "$distinct" ]]; then
  check 0 "all suite published exactly one pair per unique Case Key"
else
  check 1 "all suite pair counts mismatch: $all_files files, $all_zips zips, $all_jsons jsons, $distinct Case Keys"
fi

if python3 tests/archive-tests/verify-fixtures.py "$ALL_OUT" \
     --report "$TEST_OUTPUT_DIR/all-verification.json" >/dev/null 2>&1; then
  check 0 "independent verifier passed the complete catalogue"
else
  check 1 "independent verifier must pass the complete catalogue"
fi

# 11. Tamper detection: one flipped Archive byte must fail verification.
TAMPER="$TEST_OUTPUT_DIR/tamper"
mkdir -p "$TAMPER"
cp "$TEST_OUTPUT_DIR"/smoke/* "$TAMPER/" || check 1 "tamper preparation copy failed"
FIRST_ZIP=$(find "$TAMPER" -name '*.zip' | sort | head -n 1)
if [[ -n "$FIRST_ZIP" ]] && python3 tests/archive-tests/flip-last-byte.py "$FIRST_ZIP"; then
  if python3 tests/archive-tests/verify-fixtures.py "$TAMPER" \
       --report "$TEST_OUTPUT_DIR/tamper-verification.json" >/dev/null 2>&1; then
    check 1 "tampered Archive byte must fail verification"
  else
    check 0 "tampered Archive byte rejected by the verifier"
  fi
else
  check 1 "tamper preparation failed"
fi

# 12. Verifier self-test module (tamper reasons, sleeper deadline, prerequisites).
if python3 tests/archive-tests/test_verify_fixtures.py >/dev/null 2>&1; then
  check 0 "verifier self-test module passed"
else
  check 1 "verifier self-test module must pass"
fi

# 13. Full-codec verification profile via 7-Zip (ticket #930): valid coded
#     controls must extract with matching content hashes; coded-corruption
#     fixtures must be rejected. Tested with `t` (no extraction) for malformed
#     fixtures so malformed bytes are never extracted. Missing 7-Zip fails.
SEVEN_ZIP=""
if command -v 7zz >/dev/null 2>&1; then
  SEVEN_ZIP="7zz"
elif command -v 7z >/dev/null 2>&1; then
  SEVEN_ZIP="7z"
fi
if [[ -z "$SEVEN_ZIP" ]]; then
  check 1 "7-Zip (7zz/7z) must be installed for full-codec verification"
else
  SEVEN_VERSION=$("$SEVEN_ZIP" 2>&1 | grep -m1 '7-Zip' || true)
  if [[ -z "$SEVEN_VERSION" ]]; then
    check 1 "7-Zip adapter must report its version"
  else
    check 0 "7-Zip full-codec adapter present: $SEVEN_VERSION"
  fi
  if python3 - "$ALL_OUT" "$SEVEN_ZIP" <<'PYEOF' >/dev/null 2>&1; then
import glob, hashlib, json, os, subprocess, sys, tempfile
out, seven = sys.argv[1:3]
for key in ("valid-bzip2", "valid-deflate64", "valid-deflate64-long-match", "valid-mixed-methods", "bzip2-high-ratio-bounded", "valid-zip64-descriptor-signature", "valid-zip64-descriptor-no-signature"):
    jp = next(p for p in glob.glob(os.path.join(out, "*.json")) if json.load(open(p))["caseKey"] == key)
    d = json.load(open(jp))
    zp = jp[:-5] + ".zip"
    files = [e for e in d["entries"] if e["kind"] == "file"]
    if not files:
        sys.exit(f"no file entries for {key}")
    with tempfile.TemporaryDirectory() as td:
        r = subprocess.run([seven, "x", zp, f"-o{td}", "-y"], capture_output=True)
        if r.returncode != 0:
            sys.exit(f"extract failed for {key}")
        for e in files:
            fp = os.path.join(td, e["readableName"])
            if not os.path.isfile(fp):
                sys.exit(f"missing extracted entry for {key}")
            h = hashlib.sha256(open(fp, "rb").read()).hexdigest()
            if h != e["contentSha256"]:
                sys.exit(f"hash mismatch for {key}")
PYEOF
    check 0 "7-Zip extracted valid coded controls with matching content hashes"
  else
    check 1 "7-Zip must extract valid coded controls with matching hashes"
  fi
  CORRUPT_OK=1
  for key in bzip2-corrupt-block-magic bzip2-truncated-stream bzip2-wrong-crc deflate64-corrupt-stream deflate64-truncated-stream; do
    CZIP=$(python3 -c 'import glob,json,sys; print(next((p[:-5]+".zip" for p in glob.glob(sys.argv[1]+"/*.json") if json.load(open(p))["caseKey"]==sys.argv[2]), ""))' "$ALL_OUT" "$key" || true)
    if [[ -z "$CZIP" ]] || [[ ! -f "$CZIP" ]]; then
      CORRUPT_OK=0
    elif "$SEVEN_ZIP" t "$CZIP" >/dev/null 2>&1; then
      CORRUPT_OK=0
    fi
  done
  if [[ "$CORRUPT_OK" -eq 1 ]]; then
    check 0 "7-Zip rejected all #900 coded-corruption fixtures"
  else
    check 1 "7-Zip must reject all #900 coded-corruption fixtures"
  fi
  # Ticket #933: split-archive declarations and count disagreements are
  # malformed/unsupported declarations tested with `t` (never extracted).
  # Four are rejected by 7-Zip; the locator total-disks declaration is
  # ignored by 7-Zip (while Python's reader refuses it), so only its
  # presence is asserted, not a rejection.
  DISPUTE_OK=1
  for key in multidisk-eocd-declared multidisk-central-entry-declared eocd-entry-count-mismatch zip64-eocd-entry-count-mismatch; do
    DZIP=$(python3 -c 'import glob,json,sys; print(next((p[:-5]+".zip" for p in glob.glob(sys.argv[1]+"/*.json") if json.load(open(p))["caseKey"]==sys.argv[2]), ""))' "$ALL_OUT" "$key" || true)
    if [[ -z "$DZIP" ]] || [[ ! -f "$DZIP" ]]; then
      DISPUTE_OK=0
    elif "$SEVEN_ZIP" t "$DZIP" >/dev/null 2>&1; then
      DISPUTE_OK=0
    fi
  done
  LOCATOR_ZIP=$(python3 -c 'import glob,json,sys; print(next((p[:-5]+".zip" for p in glob.glob(sys.argv[1]+"/*.json") if json.load(open(p))["caseKey"]=="zip64-locator-disk-mismatch"), ""))' "$ALL_OUT" || true)
  if [[ -z "$LOCATOR_ZIP" ]] || [[ ! -f "$LOCATOR_ZIP" ]]; then
    DISPUTE_OK=0
  fi
  if [[ "$DISPUTE_OK" -eq 1 ]]; then
    check 0 "7-Zip rejected the #933 spanning and count-disagreement fixtures"
  else
    check 1 "7-Zip must reject the #933 spanning and count-disagreement fixtures"
  fi
  # Ticket #935: mixed one-bad-member archives must fail as a whole (`t`
  # rejects) while the healthy Store and Deflate siblings extract by name
  # with matching hashes — the bad member's bytes are never decoded.
  if python3 - "$ALL_OUT" "$SEVEN_ZIP" <<'PYEOF' >/dev/null 2>&1; then
import glob, hashlib, json, os, subprocess, sys, tempfile
out, seven = sys.argv[1:3]
for key in ("mixed-methods-one-corrupt-member", "mixed-methods-one-unsupported-member"):
    jp = next(p for p in glob.glob(os.path.join(out, "*.json")) if json.load(open(p))["caseKey"] == key)
    d = json.load(open(jp))
    zp = jp[:-5] + ".zip"
    healthy = [e for e in d["entries"] if e["kind"] == "file" and e["readableName"] in ("a.txt", "b.bin")]
    if len(healthy) != 2:
        sys.exit(f"expected healthy siblings for {key}")
    if subprocess.run([seven, "t", zp], capture_output=True).returncode == 0:
        sys.exit(f"one-bad-member Archive must not verify as success: {key}")
    with tempfile.TemporaryDirectory() as td:
        names = [e["readableName"] for e in healthy]
        r = subprocess.run([seven, "x", zp, f"-o{td}", "-y", *names], capture_output=True)
        if r.returncode != 0:
            sys.exit(f"healthy sibling extraction failed for {key}")
        for e in healthy:
            fp = os.path.join(td, e["readableName"])
            if not os.path.isfile(fp):
                sys.exit(f"healthy sibling missing for {key}")
            if hashlib.sha256(open(fp, "rb").read()).hexdigest() != e["contentSha256"]:
                sys.exit(f"healthy sibling hash mismatch for {key}")
PYEOF
    check 0 "7-Zip isolates the bad member while healthy siblings match"
  else
    check 1 "7-Zip must isolate the bad member while healthy siblings match"
  fi
fi

# 14. Offline verification (ticket #932): with an empty npm cache and an
#     unreachable registry, the full catalogue still verifies — proving
#     verification downloads nothing. The pinned CLI runs via node directly,
#     so these settings cannot affect it; they only turn any attempted
#     download into a loud failure.
OFFLINE_CACHE="$TEST_OUTPUT_DIR/npm-cache-empty"
rm -rf "$OFFLINE_CACHE"
mkdir -p "$OFFLINE_CACHE"
if npm_config_cache="$OFFLINE_CACHE" npm_config_registry=http://127.0.0.1:9/ python3 tests/archive-tests/verify-fixtures.py "$ALL_OUT" \
     --report "$TEST_OUTPUT_DIR/all-offline-verification.json" >/dev/null 2>&1; then
  check 0 "offline verification passed the complete catalogue without downloads"
else
  check 1 "offline verification must pass the complete catalogue without downloads"
fi

if [[ "$failures" -ne 0 ]]; then
  echo -e "\e[41m[ ERROR ]\e[0m Archive Test E2E failed with $failures errors."
  exit 1
fi

echo -e "\e[42m[ SUCCESS ]\e[0m Archive Test E2E passed."
exit 0
