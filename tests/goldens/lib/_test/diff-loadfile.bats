#!/usr/bin/env bats

# Unit tests for tests/goldens/lib/diff-loadfile.sh.
#
# Run with:  bats tests/goldens/lib/_test/diff-loadfile.bats

setup() {
  SCRIPT="$BATS_TEST_DIRNAME/../diff-loadfile.sh"
  TMPDIR_LOCAL="$(mktemp -d)"
}

teardown() {
  rm -rf "$TMPDIR_LOCAL"
}

@test "errors when called with no args" {
  run bash "$SCRIPT"
  [ "$status" -eq 2 ]
}

@test "errors when files are missing" {
  run bash "$SCRIPT" "$TMPDIR_LOCAL/missing-a" "$TMPDIR_LOCAL/missing-b"
  [ "$status" -eq 2 ]
}

@test "identical files exit 0 silently" {
  printf 'one\ntwo\nthree\n' >"$TMPDIR_LOCAL/a"
  printf 'one\ntwo\nthree\n' >"$TMPDIR_LOCAL/b"
  run bash "$SCRIPT" "$TMPDIR_LOCAL/a" "$TMPDIR_LOCAL/b"
  [ "$status" -eq 0 ]
  [ -z "$output" ]
}

@test "differing files exit 1 with first-divergence info" {
  printf 'one\ntwo\nthree\nfour\n' >"$TMPDIR_LOCAL/a"
  printf 'one\nTWO\nthree\nfour\n' >"$TMPDIR_LOCAL/b"
  run bash "$SCRIPT" "$TMPDIR_LOCAL/a" "$TMPDIR_LOCAL/b"
  [ "$status" -eq 1 ]
  # The diagnostic is on stderr; bats merges it into $output.
  [[ "$output" == *"First differing line: 2"* ]]
  [[ "$output" == *"two"* ]]
  [[ "$output" == *"TWO"* ]]
}

@test "trailing newline difference is detected" {
  printf 'a\nb\n' >"$TMPDIR_LOCAL/a"
  printf 'a\nb'   >"$TMPDIR_LOCAL/b"
  run bash "$SCRIPT" "$TMPDIR_LOCAL/a" "$TMPDIR_LOCAL/b"
  [ "$status" -eq 1 ]
}

@test "output is bounded — does not dump entire file" {
  # 5000 lines, differ on line 10. We should print far fewer lines than 5000.
  python3 -c "import sys; sys.stdout.write('\n'.join(f'line{i}' for i in range(1, 5001)) + '\n')" >"$TMPDIR_LOCAL/a"
  python3 -c "import sys
out = ['line%d' % i for i in range(1, 5001)]
out[9] = 'DIVERGED'
sys.stdout.write('\n'.join(out) + '\n')" >"$TMPDIR_LOCAL/b"
  run bash "$SCRIPT" "$TMPDIR_LOCAL/a" "$TMPDIR_LOCAL/b"
  [ "$status" -eq 1 ]
  # ctx=3 → expect at most ~12 diagnostic lines, well under 100.
  count=$(printf '%s\n' "$output" | wc -l)
  [ "$count" -lt 100 ]
  [[ "$output" == *"First differing line: 10"* ]]
}
