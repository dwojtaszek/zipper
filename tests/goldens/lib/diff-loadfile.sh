#!/usr/bin/env bash
# diff-loadfile.sh — byte-exact diff for load files, with bounded output.
#
# Usage:
#   diff-loadfile.sh <expected-file> <actual-file>
#
# Behaviour:
#   - Exit 0, no output:  files are byte-identical.
#   - Exit 1, useful output: files differ. Prints the first differing line
#     number, the expected vs actual line, and up to 3 lines of context on
#     each side. Load files can be hundreds of MB — we never dump the whole
#     thing.
#   - Exit 2: bad arguments / files not readable.
#
# Implementation notes:
#   - We compare bytes, not characters, so trailing-newline differences and
#     CRLF vs LF are surfaced.
#   - The "first differing line" is computed by streaming both files in
#     parallel; we stop on the first mismatch and only read enough trailing
#     context to print 3 more lines from each side.

set -euo pipefail

CONTEXT=3

if [[ $# -ne 2 ]]; then
  echo "usage: diff-loadfile.sh <expected-file> <actual-file>" >&2
  exit 2
fi

expected="$1"
actual="$2"

for f in "$expected" "$actual"; do
  if [[ ! -r "$f" ]]; then
    echo "diff-loadfile.sh: not readable: $f" >&2
    exit 2
  fi
done

# Fast path — byte-identical via cmp.
if cmp -s -- "$expected" "$actual"; then
  exit 0
fi

# Slow path — find the first differing line and print bounded context.
# (Variable names avoid `exp` / `act` which collide with gawk builtins.)
awk -v ctx="$CONTEXT" -v expfile="$expected" -v actfile="$actual" '
  BEGIN {
    # Read both files line-by-line in lockstep.
    while (1) {
      e_ok = (getline e_line < expfile)
      a_ok = (getline a_line < actfile)
      if (e_ok <= 0 && a_ok <= 0) {
        # Both ended at the same line count without finding a textual diff.
        # cmp already told us they differ in bytes (e.g. trailing newline),
        # so report that explicitly.
        printf("Files differ in trailing bytes (line counts equal at %d).\n", lineno) > "/dev/stderr"
        exit 1
      }
      lineno++
      e_buf[lineno] = (e_ok > 0) ? e_line : "<EOF>"
      a_buf[lineno] = (a_ok > 0) ? a_line : "<EOF>"
      if (e_buf[lineno] != a_buf[lineno]) {
        first = lineno
        break
      }
    }

    # Print preceding context (up to ctx lines, both files identical there).
    start = (first - ctx > 1) ? first - ctx : 1
    printf("First differing line: %d\n", first) > "/dev/stderr"
    printf("--- expected: %s\n", expfile) > "/dev/stderr"
    printf("+++ actual:   %s\n", actfile) > "/dev/stderr"
    for (i = start; i < first; i++) {
      printf("  %6d  %s\n", i, e_buf[i]) > "/dev/stderr"
    }
    printf("- %6d  %s\n", first, e_buf[first]) > "/dev/stderr"
    printf("+ %6d  %s\n", first, a_buf[first]) > "/dev/stderr"

    # Print up to ctx trailing lines from each side.
    for (j = 1; j <= ctx; j++) {
      e_ok = (getline e_line < expfile)
      a_ok = (getline a_line < actfile)
      if (e_ok <= 0 && a_ok <= 0) break
      ln = first + j
      e_show = (e_ok > 0) ? e_line : "<EOF>"
      a_show = (a_ok > 0) ? a_line : "<EOF>"
      printf("  %6d  expected: %s\n", ln, e_show) > "/dev/stderr"
      printf("  %6d  actual:   %s\n", ln, a_show) > "/dev/stderr"
    }
    exit 1
  }
'
