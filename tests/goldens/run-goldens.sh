#!/usr/bin/env bash
# run-goldens.sh — drive the Zipper CLI against scenarios.tsv and byte-compare
# its output to checked-in fixtures.
#
# Usage:
#   run-goldens.sh [--capture] [--scenario NAME]
#
#   --capture           Write fresh fixtures instead of diffing. Used by the
#                       capture tickets when intentionally regenerating
#                       goldens. Never used in CI.
#   --scenario NAME     Run only the row whose `scenario_name` equals NAME,
#                       for debugging.
#
# scenarios.tsv contract:
#   - Header row first (pipe-delimited): scenario_name | cli_args | seed | description
#   - Empty body OK — exits 0 with no work done. This ticket ships an empty
#     table on purpose; capture tickets populate it.
#   - Comment lines start with `#` and are ignored.
#
# Exit codes:
#   0  all scenarios matched their fixtures (or scenarios.tsv was empty).
#   1  one or more scenarios diverged.
#   2  bad arguments / scenarios.tsv missing / Zipper CLI not runnable.

set -euo pipefail

GOLDENS_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LIB_DIR="$GOLDENS_DIR/lib"
FIXTURES_DIR="$GOLDENS_DIR/fixtures"
SCENARIOS_FILE="$GOLDENS_DIR/scenarios.tsv"

CAPTURE=0
ONLY_SCENARIO=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --capture) CAPTURE=1; shift ;;
    --scenario) ONLY_SCENARIO="${2:-}"; shift 2 ;;
    -h|--help)
      sed -n '1,30p' "${BASH_SOURCE[0]}"
      exit 0
      ;;
    *)
      echo "run-goldens.sh: unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ ! -f "$SCENARIOS_FILE" ]]; then
  echo "run-goldens.sh: scenarios.tsv not found at $SCENARIOS_FILE" >&2
  exit 2
fi

# Walk the scenarios file. We accept either tab or pipe as separator on the
# header row to stay forward-compatible; data rows must match the header.
header_seen=0
total=0
ran=0
failed=0

while IFS= read -r raw_line || [[ -n "$raw_line" ]]; do
  # Strip CR (Windows-authored TSVs) and skip blank/comment lines.
  line="${raw_line%$'\r'}"
  [[ -z "$line" ]] && continue
  [[ "${line:0:1}" == "#" ]] && continue

  if [[ $header_seen -eq 0 ]]; then
    header_seen=1
    continue
  fi

  total=$((total + 1))

  # Pipe-delimited per the issue spec.
  IFS='|' read -r scenario_name cli_args seed description <<<"$line"
  scenario_name="$(echo "$scenario_name" | xargs)"
  cli_args="$(echo "$cli_args" | sed 's/^ *//;s/ *$//')"
  seed="$(echo "$seed" | xargs)"

  if [[ -n "$ONLY_SCENARIO" && "$ONLY_SCENARIO" != "$scenario_name" ]]; then
    continue
  fi

  ran=$((ran + 1))
  echo "==> scenario: $scenario_name"

  fixture_dir="$FIXTURES_DIR/$scenario_name"
  work_dir="$(mktemp -d)"
  trap 'rm -rf "$work_dir"' EXIT

  # Resolve the Zipper CLI. Capture/diff scenarios are populated in follow-up
  # tickets; until then this script is exercised only with empty/no rows.
  zipper_cli="${ZIPPER_CLI:-}"
  if [[ -z "$zipper_cli" ]]; then
    echo "run-goldens.sh: ZIPPER_CLI env var must point at the Zipper executable" >&2
    exit 2
  fi

  # shellcheck disable=SC2086 # cli_args is intentionally word-split here.
  ( cd "$work_dir" && SEED="$seed" "$zipper_cli" $cli_args )

  if [[ $CAPTURE -eq 1 ]]; then
    rm -rf "$fixture_dir"
    mkdir -p "$fixture_dir"
    cp -R "$work_dir"/. "$fixture_dir"/
    echo "    captured -> $fixture_dir"
  else
    if [[ ! -d "$fixture_dir" ]]; then
      echo "    MISSING fixture dir: $fixture_dir" >&2
      failed=$((failed + 1))
      continue
    fi
    expected_manifest="$(mktemp)"
    actual_manifest="$(mktemp)"
    bash "$LIB_DIR/sha-manifest.sh" "$fixture_dir" >"$expected_manifest"
    bash "$LIB_DIR/sha-manifest.sh" "$work_dir"    >"$actual_manifest"
    if ! diff -u "$expected_manifest" "$actual_manifest"; then
      echo "    manifest mismatch — running per-file diff to surface first divergence"
      while IFS=  read -r relpath; do
        e="$fixture_dir/$relpath"
        a="$work_dir/$relpath"
        if [[ -f "$e" && -f "$a" ]]; then
          if ! bash "$LIB_DIR/diff-loadfile.sh" "$e" "$a"; then
            failed=$((failed + 1))
            break
          fi
        fi
      done < <(awk '{print $2}' "$expected_manifest")
    fi
    rm -f "$expected_manifest" "$actual_manifest"
  fi

  rm -rf "$work_dir"
  trap - EXIT
done <"$SCENARIOS_FILE"

if [[ $total -eq 0 ]]; then
  echo "run-goldens.sh: scenarios.tsv has no data rows; nothing to do."
  exit 0
fi

echo "ran=$ran  failed=$failed  total=$total"
[[ $failed -eq 0 ]]
