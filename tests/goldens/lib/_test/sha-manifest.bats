#!/usr/bin/env bats

# Unit tests for tests/goldens/lib/sha-manifest.sh.
#
# Run with:  bats tests/goldens/lib/_test/sha-manifest.bats

setup() {
  SCRIPT="$BATS_TEST_DIRNAME/../sha-manifest.sh"
  TMPDIR_LOCAL="$(mktemp -d)"
}

teardown() {
  rm -rf "$TMPDIR_LOCAL"
}

@test "errors when no args" {
  run bash "$SCRIPT"
  [ "$status" -eq 1 ]
}

@test "errors when path is not a directory" {
  run bash "$SCRIPT" "$TMPDIR_LOCAL/does-not-exist"
  [ "$status" -eq 1 ]
}

@test "empty directory produces empty output" {
  run bash "$SCRIPT" "$TMPDIR_LOCAL"
  [ "$status" -eq 0 ]
  [ -z "$output" ]
}

@test "single file produces one manifest line" {
  printf 'hello\n' >"$TMPDIR_LOCAL/a.txt"
  run bash "$SCRIPT" "$TMPDIR_LOCAL"
  [ "$status" -eq 0 ]
  # sha256("hello\n") = 5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03
  [[ "$output" == "5891b5b522d5df086d0ff0b110fbd9d21bb4fc7163af34d08286a2e846f6be03  a.txt" ]]
}

@test "output is sorted by relative path" {
  mkdir -p "$TMPDIR_LOCAL/sub"
  printf 'a\n' >"$TMPDIR_LOCAL/zeta.txt"
  printf 'b\n' >"$TMPDIR_LOCAL/alpha.txt"
  printf 'c\n' >"$TMPDIR_LOCAL/sub/inner.txt"
  run bash "$SCRIPT" "$TMPDIR_LOCAL"
  [ "$status" -eq 0 ]
  paths=$(printf '%s\n' "$output" | awk '{print $2}')
  expected=$'alpha.txt\nsub/inner.txt\nzeta.txt'
  [[ "$paths" == "$expected" ]]
}

@test "output is deterministic across runs" {
  printf 'one\n'  >"$TMPDIR_LOCAL/a.txt"
  printf 'two\n'  >"$TMPDIR_LOCAL/b.txt"
  printf 'three\n' >"$TMPDIR_LOCAL/c.txt"
  run1=$(bash "$SCRIPT" "$TMPDIR_LOCAL")
  run2=$(bash "$SCRIPT" "$TMPDIR_LOCAL")
  [[ "$run1" == "$run2" ]]
}

@test "no trailing blank line" {
  printf 'x\n' >"$TMPDIR_LOCAL/only.txt"
  run bash "$SCRIPT" "$TMPDIR_LOCAL"
  [ "$status" -eq 0 ]
  # `output` from `run` strips one trailing newline; check raw bytes.
  raw="$(bash "$SCRIPT" "$TMPDIR_LOCAL" | od -c | tail -1)"
  # Last printed char should be newline immediately after the path; no extra blank line.
  [[ "$(bash "$SCRIPT" "$TMPDIR_LOCAL" | wc -l)" -eq 1 ]]
}

@test "handles nested directories" {
  mkdir -p "$TMPDIR_LOCAL/a/b/c"
  printf 'deep\n' >"$TMPDIR_LOCAL/a/b/c/file.txt"
  printf 'top\n'  >"$TMPDIR_LOCAL/top.txt"
  run bash "$SCRIPT" "$TMPDIR_LOCAL"
  [ "$status" -eq 0 ]
  paths=$(printf '%s\n' "$output" | awk '{print $2}')
  expected=$'a/b/c/file.txt\ntop.txt'
  [[ "$paths" == "$expected" ]]
}
