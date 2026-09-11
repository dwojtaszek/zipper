#!/bin/bash

# Archive Test workflow E2E (ticket #844): basic CLI interactions for
# --archive-test-suite / --archive-test-cases — publication, validation
# failures, and exit codes. Uses the shared zipper CLI helper.

set -euo pipefail

# shellcheck source=./_zipper-cli.sh
source "$(dirname "$0")/_zipper-cli.sh"

TEST_OUTPUT_DIR="./results/archive-test-suites"
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

SMOKE_CASES=3   # valid-empty, valid-stored, valid-deflate

failures=0

function check() {
  if [ "$1" -eq 0 ]; then
    echo -e "\e[42m[ SUCCESS ]\e[0m $2"
  else
    echo -e "\e[41m[ ERROR ]\e[0m $2"
    failures=$((failures + 1))
  fi
}

# 1. Smoke suite publication: flat pairs only, filenames and JSON agree.
OUT="$TEST_OUTPUT_DIR/smoke"
zipper --archive-test-suite smoke --seed 42 --output-path "$OUT"
files=$(find "$OUT" -type f | wc -l)
[ "$files" -eq $((SMOKE_CASES * 2)) ]; check $? "smoke suite published $files files ($SMOKE_CASES pairs)"

flat=$(find "$OUT" -mindepth 2 -print | wc -l)
[ "$flat" -eq 0 ]; check $? "no subfolders or outer Archive"

names_ok=1
for json in "$OUT"/*.json; do
  base=$(basename "$json" .json)
  grep -Eq "\"fixtureId\": *\"$base\"" "$json" || names_ok=0
  grep -Eq "\"fileName\": *\"$base\.zip\"" "$json" || names_ok=0
  [ -f "$OUT/$base.zip" ] || names_ok=0
done
[ "$names_ok" -eq 1 ]; check $? "JSON fixtureId/fileName match basenames"

# 2. Case selection: exactly the named Case Keys.
OUT="$TEST_OUTPUT_DIR/two-cases"
zipper --archive-test-suite malformed --archive-test-cases crc-both-mismatch,missing-eocd --seed 42 --output-path "$OUT"
files=$(find "$OUT" -type f | wc -l)
[ "$files" -eq 4 ]; check $? "case selection published exactly 2 pairs"

# 3. Determinism: the same selection into a new directory repeats the Fixture IDs.
OUT2="$TEST_OUTPUT_DIR/two-cases-replay"
zipper --archive-test-suite malformed --archive-test-cases missing-eocd,crc-both-mismatch --seed 42 --output-path "$OUT2"
diff <(ls "$OUT" | sort) <(ls "$OUT2" | sort) >/dev/null
check $? "reordered selection repeats identical Fixture IDs"

# 4. Repeated run into the same directory fails (exit 1) and preserves the pairs.
if zipper --archive-test-suite smoke --output-path "$TEST_OUTPUT_DIR/smoke" >/dev/null 2>&1; then
  check 1 "repeated run into existing directory must fail"
else
  preserved=$(find "$TEST_OUTPUT_DIR/smoke" -type f | wc -l)
  [ "$preserved" -eq $((SMOKE_CASES * 2)) ]; check $? "failed repeated run preserved the first run"
fi

# 5. Closed flag set: a generation flag is rejected even at its default value.
if zipper --archive-test-suite smoke --folders 1 --output-path "$TEST_OUTPUT_DIR/x" >/dev/null 2>&1; then
  check 1 "generation flag alongside --archive-test-suite must fail"
else
  check 0 "generation flag alongside --archive-test-suite rejected"
fi

# 6. --benchmark / --chaos-list mixing is rejected before their early exits.
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

# 7. Validation failures: unknown suite, unknown Case Key, missing --output-path.
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

# 8. Independent verifier: the published pairs pass cross-implementation verification
#     (schema, identity, mutation audit, reader operations; ticket #845).
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

# 9. Tamper detection: one flipped Archive byte must fail verification.
TAMPER="$TEST_OUTPUT_DIR/tamper"
mkdir -p "$TAMPER"
cp "$TEST_OUTPUT_DIR"/smoke/* "$TAMPER/" || check 1 "tamper preparation copy failed"
FIRST_ZIP=$(find "$TAMPER" -name '*.zip' | sort | head -n 1)
if [ -n "$FIRST_ZIP" ] && python3 tests/archive-tests/flip-last-byte.py "$FIRST_ZIP"; then
  if python3 tests/archive-tests/verify-fixtures.py "$TAMPER" \
       --report "$TEST_OUTPUT_DIR/tamper-verification.json" >/dev/null 2>&1; then
    check 1 "tampered Archive byte must fail verification"
  else
    check 0 "tampered Archive byte rejected by the verifier"
  fi
else
  check 1 "tamper preparation failed"
fi

# 10. Verifier self-test module (tamper reasons, sleeper deadline, prerequisites).
if python3 tests/archive-tests/test_verify_fixtures.py >/dev/null 2>&1; then
  check 0 "verifier self-test module passed"
else
  check 1 "verifier self-test module must pass"
fi

if [ "$failures" -ne 0 ]; then
  echo -e "\e[41m[ ERROR ]\e[0m Archive Test E2E failed with $failures errors."
  exit 1
fi

echo -e "\e[42m[ SUCCESS ]\e[0m Archive Test E2E passed."
exit 0
