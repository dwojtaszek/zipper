#!/usr/bin/env bash
# run-goldens.sh — drive the Zipper CLI against scenarios.tsv and byte-compare
# its output to checked-in fixtures.
#
# Usage:
#   run-goldens.sh [--capture] [--scenario NAME]
#
#   --capture           Write fresh fixtures instead of diffing. Used when
#                       intentionally regenerating goldens. Never used in CI.
#   --scenario NAME     Run only the row whose `scenario_name` equals NAME,
#                       for debugging.
#
# scenarios.tsv contract:
#   - Header row first (pipe-delimited): scenario_name | cli_args | seed | description
#   - Empty body OK — exits 0 with no work done.
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

# ── Normalisation helpers ────────────────────────────────────────────────
# The Zipper CLI embeds timestamps in output filenames and JSON content.
# To make fixtures stable across runs we normalise all of it.

normalize_output() {
  local dir="$1"

  # 1) Production directories: PRODUCTION_YYYYMMDD_HHMMSS → PRODUCTION
  for d in "$dir"/PRODUCTION_*; do
    [[ -d "$d" ]] || continue
    mv "$d" "$dir/PRODUCTION"
  done

  # 2) Archive files: archive_YYYYMMDD_HHMMSS.{zip,dat,...} → archive.{ext}
  for f in "$dir"/archive_*; do
    [[ -f "$f" ]] || continue
    local base ext
    base="$(basename "$f")"
    ext="${base##*.}"
    mv "$f" "$dir/archive.$ext"
  done

  # 3) Loadfile-only files: loadfile_YYYYMMDD_HHMMSS{_properties.json,.dat,...}
  for f in "$dir"/loadfile_*; do
    [[ -f "$f" ]] || continue
    local base newname
    base="$(basename "$f")"
    newname=$(echo "$base" | sed -E 's/loadfile_[0-9]{8}_[0-9]{6}/loadfile/')
    mv "$f" "$dir/$newname"
  done

  # 4) Patch timestamps inside JSON files so content is deterministic.
  #    Covers: fileName refs, productionDate, directory names.
  while IFS= read -r -d '' jf; do
    sed -i -E \
      -e 's/archive_[0-9]{8}_[0-9]{6}/archive/g' \
      -e 's/loadfile_[0-9]{8}_[0-9]{6}/loadfile/g' \
      -e 's/PRODUCTION_[0-9]{8}_[0-9]{6}/PRODUCTION/g' \
      -e 's/"productionDate": "[^"]*"/"productionDate": "NORMALIZED"/g' \
      -e 's/"generationTime": "[^"]*"/"generationTime": "NORMALIZED"/g' \
      "$jf"
  done < <( find "$dir" -type f -name '*.json' -print0 2>/dev/null )
}

# Produce a "fixture-ready" snapshot of a work directory.
#
# What gets committed to fixtures/:
#   - tree.txt             deterministic file listing
#   - sha-manifest.txt     SHA-256 of every file EXCEPT .zip (ZIP internal
#                          timestamps make them non-deterministic)
#   - *.dat, *.opt, *.json committed verbatim (small load files)
#   - .zip files are NOT committed (captured only via sha-manifest of their
#     constituent load file, which IS deterministic)
#
# Args: $1 = source work_dir, $2 = destination fixture_dir
snapshot_fixture() {
  local src="$1" dst="$2"

  rm -rf "$dst"
  mkdir -p "$dst"

  # tree.txt — deterministic listing of all output files (path-sorted).
  ( cd "$src" && find . -type f | LC_ALL=C sort ) > "$dst/tree.txt"

  # SHA manifest, including uncompressed ZIP entry contents.
  bash "$LIB_DIR/sha-manifest.sh" "$src" > "$dst/sha-manifest.txt"

  # Copy load files and metadata verbatim (small files safe to commit).
  ( cd "$src" && find . -type f -print0 ) | while IFS= read -r -d '' f; do
    local rel="${f#./}"
    local ext="${rel##*.}"
    case "$ext" in
      dat|opt|csv|xml|json)
        mkdir -p "$dst/$(dirname "$rel")"
        cp "$src/$rel" "$dst/$rel"
        ;;
    esac
  done
}

# ── Main loop ────────────────────────────────────────────────────────────

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

  # Resolve the Zipper CLI.
  zipper_cli="${ZIPPER_CLI:-}"
  if [[ -z "$zipper_cli" ]]; then
    echo "run-goldens.sh: ZIPPER_CLI env var must point at the Zipper executable" >&2
    exit 2
  fi

  # Execute the CLI. Catch non-zero exits so `set -e` doesn't abort the whole suite.
  # shellcheck disable=SC2086 # cli_args is intentionally word-split here.
  if ! ( cd "$work_dir" && "$zipper_cli" $cli_args --seed "$seed" ); then
    echo "    CLI crashed with non-zero exit code" >&2
    failed=$((failed + 1))
    scenario_failed=1
    rm -rf "$work_dir"
    trap - EXIT
    continue
  fi

  # Normalize timestamped output to stable names.
  normalize_output "$work_dir"

  if [[ $CAPTURE -eq 1 ]]; then
    snapshot_fixture "$work_dir" "$fixture_dir"
    echo "    captured -> $fixture_dir"
  else
    if [[ ! -d "$fixture_dir" ]]; then
      echo "    MISSING fixture dir: $fixture_dir" >&2
      failed=$((failed + 1))
      rm -rf "$work_dir"
      trap - EXIT
      continue
    fi

    # Build a snapshot of the fresh run in a temp fixture dir for comparison.
    actual_fixture="$(mktemp -d)"
    snapshot_fixture "$work_dir" "$actual_fixture"

    scenario_failed=0

    # Byte-exact comparison via SHA manifest.
    if ! diff -u "$fixture_dir/sha-manifest.txt" "$actual_fixture/sha-manifest.txt" >&2; then
      echo "    SHA manifest mismatch"
      scenario_failed=1
    fi

    # Compare tree listings.
    if ! diff -u "$fixture_dir/tree.txt" "$actual_fixture/tree.txt" >&2; then
      echo "    tree.txt mismatch"
      scenario_failed=1
    fi

    # Per-file diff on load files for actionable first-divergence output.
    if [[ $scenario_failed -eq 1 ]]; then
      ( cd "$fixture_dir" && find . -type f \( -name '*.dat' -o -name '*.opt' -o -name '*.json' \) -print0 \
        | LC_ALL=C sort -z ) \
        | while IFS= read -r -d '' relpath; do
            local_rel="${relpath#./}"
            e="$fixture_dir/$local_rel"
            a="$actual_fixture/$local_rel"
            if [[ -f "$e" && -f "$a" ]]; then
              if ! bash "$LIB_DIR/diff-loadfile.sh" "$e" "$a"; then
                break
              fi
            elif [[ -f "$e" && ! -f "$a" ]]; then
              echo "    MISSING in actual: $local_rel" >&2
            fi
          done
    fi

    if [[ $scenario_failed -eq 1 ]]; then
      failed=$((failed + 1))
    else
      echo "    OK"
    fi

    rm -rf "$actual_fixture"
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
