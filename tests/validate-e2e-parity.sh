#!/usr/bin/env bash
# validate-e2e-parity.sh — detect drift between the Linux (.sh) and Windows
# (.bat) E2E orchestrators. Both must run the same inline "Test Case N" cases
# and the same sub-script suites, so a test added on one side cannot be
# silently dropped on the other (#624).
#
# Detection relies on the established invocation conventions:
#   inline cases:  run_test_case "Test Case N: ..." (both runners) and custom
#                  blocks announced via a "START: Test Case N: ..." print line
#   sub-scripts:   bash ./<repo-relative-path>.sh  |  call .\<path>.bat
# Invoke new suites in exactly these forms or the gate cannot see them.
#
# Usage:
#   validate-e2e-parity.sh [--strict]
#
#   --strict   Fail on any drift (CI mode). Without --strict, report only.
#
# Exit codes:
#   0  no drift (or report-only mode)
#   1  drift detected (--strict)
#   2  missing files / parse errors

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SH_RUNNER="$SCRIPT_DIR/run-tests.sh"
BAT_RUNNER="$SCRIPT_DIR/run-tests.bat"

# Sub-scripts that exist on one side only, by design:
#   tests/test-run-tests-fatal — Windows-only regression guard for #442
#     (cmd/batch errorlevel semantics); run-tests.sh has no
#     --meta-test-fail-only path.
#   .github/actions/coverage-gate/test-coverage-gate — self-test for the
#     Linux-only CI composite action; no Windows counterpart by design.
EXEMPT_SUBSCRIPTS=(
    tests/test-run-tests-fatal
    .github/actions/coverage-gate/test-coverage-gate
)

STRICT=0
[[ "${1:-}" == "--strict" ]] && STRICT=1

for f in "$SH_RUNNER" "$BAT_RUNNER"; do
    if [[ ! -f "$f" ]]; then
        echo "Error: runner not found at $f" >&2
        exit 2
    fi
done

# --- Extract inline test case names ---
# Invocation strings ('run_test_case "Test Case N: ..."' on both sides) plus
# custom blocks announced via 'START: Test Case N: ...' print lines.
extract_cases_sh() {
    {
        grep -oE 'run_test_case "Test Case [^"]+"' "$SH_RUNNER" | sed 's/^run_test_case "//; s/"$//'
        grep -oE '"START: Test Case [^"]+"' "$SH_RUNNER" | sed 's/^"START: //; s/"$//'
    } | sort -u
}

extract_cases_bat() {
    {
        grep -oiE 'call :run_test_case "Test Case [^"]+"' "$BAT_RUNNER" | sed 's/^call :run_test_case "//I; s/"$//'
        grep -oE '"START: Test Case [^"]+"' "$BAT_RUNNER" | sed 's/^"START: //; s/"$//'
    } | sort -u
}

# --- Extract invoked sub-script path-stems (repo-relative, no extension) ---
# Invocation convention (both runners): `bash ./<path>.sh` / `call .\<path>.bat`.
extract_scripts_sh() {
    grep -oE 'bash \./[A-Za-z0-9._/-]+\.sh' "$SH_RUNNER" | sed 's|^bash \./||; s|\.sh$||' | sort -u
}

extract_scripts_bat() {
    grep -oiE 'call \.\\[A-Za-z0-9._\\/-]+\.bat' "$BAT_RUNNER" | sed 's|\\|/|g; s|^call \./||I; s|\.bat$||I' | sort -u
}

is_exempt() {
    local stem="$1"
    for e in "${EXEMPT_SUBSCRIPTS[@]}"; do
        [[ "$stem" == "$e" ]] && return 0
    done
    return 1
}

mapfile -t SH_CASES < <(extract_cases_sh)
mapfile -t BAT_CASES < <(extract_cases_bat)
mapfile -t SH_SCRIPTS < <(extract_scripts_sh)
mapfile -t BAT_SCRIPTS < <(extract_scripts_bat)

if [[ ${#SH_CASES[@]} -eq 0 || ${#BAT_CASES[@]} -eq 0 || ${#SH_SCRIPTS[@]} -eq 0 || ${#BAT_SCRIPTS[@]} -eq 0 ]]; then
    echo "Error: extraction yielded an empty set (sh cases: ${#SH_CASES[@]}, bat cases: ${#BAT_CASES[@]}, sh scripts: ${#SH_SCRIPTS[@]}, bat scripts: ${#BAT_SCRIPTS[@]})" >&2
    exit 2
fi

DRIFT=0

report_diff() {
    local label="$1"; shift
    local -n only_sh_ref=$1
    local -n only_bat_ref=$2
    if [[ ${#only_sh_ref[@]} -gt 0 ]]; then
        DRIFT=1
        echo "$label present in run-tests.sh only:"
        printf '  - %s\n' "${only_sh_ref[@]}"
    fi
    if [[ ${#only_bat_ref[@]} -gt 0 ]]; then
        DRIFT=1
        echo "$label present in run-tests.bat only:"
        printf '  - %s\n' "${only_bat_ref[@]}"
    fi
}

# Inline test cases
# shellcheck disable=SC2034  # consumed via nameref in report_diff
mapfile -t SH_ONLY_CASES < <(comm -23 <(printf '%s\n' "${SH_CASES[@]}") <(printf '%s\n' "${BAT_CASES[@]}"))
# shellcheck disable=SC2034  # consumed via nameref in report_diff
mapfile -t BAT_ONLY_CASES < <(comm -13 <(printf '%s\n' "${SH_CASES[@]}") <(printf '%s\n' "${BAT_CASES[@]}"))
report_diff "Inline test case" SH_ONLY_CASES BAT_ONLY_CASES

# Sub-scripts (exemptions filtered from both sides)
mapfile -t SH_ONLY_SCRIPTS_RAW < <(comm -23 <(printf '%s\n' "${SH_SCRIPTS[@]}") <(printf '%s\n' "${BAT_SCRIPTS[@]}"))
mapfile -t BAT_ONLY_SCRIPTS_RAW < <(comm -13 <(printf '%s\n' "${SH_SCRIPTS[@]}") <(printf '%s\n' "${BAT_SCRIPTS[@]}"))
# shellcheck disable=SC2034  # consumed via nameref in report_diff
SH_ONLY_SCRIPTS=()
for s in ${SH_ONLY_SCRIPTS_RAW[@]+"${SH_ONLY_SCRIPTS_RAW[@]}"}; do
    is_exempt "$s" || SH_ONLY_SCRIPTS+=("$s")
done
# shellcheck disable=SC2034  # consumed via nameref in report_diff
BAT_ONLY_SCRIPTS=()
for s in ${BAT_ONLY_SCRIPTS_RAW[@]+"${BAT_ONLY_SCRIPTS_RAW[@]}"}; do
    is_exempt "$s" || BAT_ONLY_SCRIPTS+=("$s")
done
report_diff "Sub-script suite" SH_ONLY_SCRIPTS BAT_ONLY_SCRIPTS

if [[ $DRIFT -eq 0 ]]; then
    echo "OK: run-tests.sh and run-tests.bat are in sync (${#SH_CASES[@]} inline cases, ${#SH_SCRIPTS[@]} sub-script suites)."
    exit 0
fi

echo
echo "Drift detected between run-tests.sh and run-tests.bat."
echo "Add the missing test to the other runner, or add a documented exemption in $0."
[[ $STRICT -eq 1 ]] && exit 1
exit 0
