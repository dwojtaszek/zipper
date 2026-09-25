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
# The .bat side carries one extra rule (#1040): every `call .\tests\*.bat` must be
# followed, as the next significant statement (blank lines, REM, and :: comments
# are skipped), by an `if errorlevel 1` guard that exits non-zero — either the
# single-line form `if errorlevel 1 exit /b 1` or a block containing `exit /b 1`.
# A child that returns non-zero and is not aborted is a silent pass. The .sh side
# already does this with `|| print_error`.
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
#   tests/test-perf-measure — Linux-only regression guard for measure.sh
#     and perf-guard / baseline-refresh integrations (#826).
EXEMPT_SUBSCRIPTS=(
    tests/test-run-tests-fatal
    .github/actions/coverage-gate/test-coverage-gate
    tests/test-perf-measure
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

# #1040: a `call .\tests\*.bat` whose exit code is never checked is a silent pass — a child that
# exits non-zero is swallowed and the wrapper still reports success. Each child call must be
# followed, as the next significant statement, by an `if errorlevel 1` guard that itself exits
# non-zero. run-tests.sh already does this with `|| print_error`, so only the .bat side can drift.
#
# Single awk pass over the runner: it reports every offending call site, so a second unguarded
# duplicate of an already-guarded child is caught, and a REM/:: comment quoting the call text is
# not mistaken for a call.
mapfile -t UNGUARDED_CHILDREN < <(awk '
    { line[NR] = $0 }

    function trim(s) {
        sub(/\r$/, "", s)
        gsub(/^[ \t]+/, "", s)
        return s
    }

    # Index of the next line at or after `from` that is a real statement, or 0 if none.
    # Skips blanks, REM, and :: comments.
    function next_stmt(from,   i, s) {
        for (i = from; i <= total; i++) {
            s = trim(line[i])
            if (s == "") continue
            t = tolower(s)
            if (t ~ /^rem/ || t ~ /^::/) continue
            return i
        }
        return 0
    }

    END {
        total = NR
        for (n = 1; n <= total; n++) {
            stmt = trim(line[n])
            t = tolower(stmt)
            if (t ~ /^(rem|::)/) continue
            if (match(t, /call[ \t]+\.\\tests\\[a-z0-9._-]+\.bat/) == 0) continue
            child = substr(t, RSTART, RLENGTH)
            sub(/^call[ \t]+\.\\tests\\/, "", child)

            g = next_stmt(n + 1)
            if (g == 0) { print child ": no guard after the call"; continue }
            gt = tolower(trim(line[g]))
            if (gt !~ /^if[ \t]+errorlevel[ \t]+1/) { print child ": no '"'"'if errorlevel 1'"'"' guard"; continue }

            # Single-line form: `if errorlevel 1 exit /b 1`
            if (gt ~ /exit[ \t]+\/b[ \t]+1/) continue

            # Block form: the body up to the closing paren must exit non-zero.
            aborts = 0
            for (i = g + 1; i <= g + 12 && i <= total; i++) {
                b = tolower(trim(line[i]))
                if (b ~ /^exit[ \t]+\/b[ \t]+1/) { aborts = 1; break }
                if (trim(line[i]) ~ /^\)/) break
            }
            if (!aborts) print child ": guard does not '"'"'exit /b 1'"'"'"
        }
    }
' "$BAT_RUNNER" | sed "s/^\(.*\.bat\): \(.*\)$/\1 — \2/")

if [[ ${#UNGUARDED_CHILDREN[@]} -eq 0 ]] \
    && ! grep -qiE 'call[[:space:]]+\.\\tests\\[A-Za-z0-9._-]+\.bat' "$BAT_RUNNER"; then
    echo "ERROR: no 'call .\tests\*.bat' child invocations found in $BAT_RUNNER —" >&2
    echo "the #1040 guard check could not run, so treat this validator as failed." >&2
    exit 2
fi

if [[ ${#UNGUARDED_CHILDREN[@]} -gt 0 ]]; then
    DRIFT=1
    echo "Child E2E script calls in run-tests.bat without an 'if errorlevel 1' guard that exits non-zero:"
    printf '  - %s\n' "${UNGUARDED_CHILDREN[@]}"
fi

if [[ $DRIFT -eq 0 ]]; then
    echo "OK: run-tests.sh and run-tests.bat are in sync (${#SH_CASES[@]} inline cases, ${#SH_SCRIPTS[@]} sub-script suites, every .bat child call guarded)."
    exit 0
fi

echo
echo "Drift detected between run-tests.sh and run-tests.bat."
echo "Add the missing test to the other runner, or add a documented exemption in $0."
[[ $STRICT -eq 1 ]] && exit 1
exit 0
