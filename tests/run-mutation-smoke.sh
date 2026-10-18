#!/bin/bash
# tests/run-mutation-smoke.sh — Lightweight mutation smoke for pre-push.
#
# Applies simple operator mutations to changed lines in .cs source files
# (vs merge-base with main), builds and runs unit tests to check if the
# mutation is caught.  Not a replacement for full Stryker analysis (which
# runs weekly in CI via typesafe-quality-audit.yml).
#
# Speed:  0 s when no source .cs files changed.
#         ~3 s per compile-error mutation; ~20 s per test-run mutation.
#         Default max 3 mutations → worst case ~60 s.
#
# Exit:   0  all killed / nothing to mutate / skipped
#         1  surviving mutants found
#
# Env:    MUTATION_SMOKE_MAX=N       max mutations (default 3)
#         MUTATION_SMOKE_TIMEOUT=N   overall timeout in seconds (default 120)

set -u

MAX="${MUTATION_SMOKE_MAX:-3}"
OVERALL_TIMEOUT="${MUTATION_SMOKE_TIMEOUT:-120}"

# ── Guards ────────────────────────────────────────────────────
BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")
if [[ "$BRANCH" == "main" || "$BRANCH" == "HEAD" ]]; then
    echo "  ✓ On main / detached HEAD; mutation smoke skipped."
    exit 0
fi

MERGE_BASE=$(git merge-base HEAD main 2>/dev/null || git merge-base HEAD origin/main 2>/dev/null || echo "")
if [[ -z "$MERGE_BASE" ]]; then
    echo "  ⚠ Cannot find merge-base with main or origin/main; mutation smoke skipped."
    exit 0
fi

# Clean up / restore any stale backup files from prior interrupted runs
shopt -s globstar nullglob 2>/dev/null || true
for bak in src/**/*.mutation-bak src/*.mutation-bak; do
    if [[ -f "$bak" ]]; then
        orig="${bak%.mutation-bak}"
        if [[ -f "$orig" ]]; then
            cp "$bak" "$orig"
            rm -f "$bak"
        fi
    fi
done 2>/dev/null || true
shopt -u globstar 2>/dev/null || true

# ── Changed source files ──────────────────────────────────────
FILES=()
while IFS= read -r f; do
    [[ -n "$f" ]] && FILES+=("$f")
done < <(
    git diff --name-only --diff-filter=ACMR "$MERGE_BASE" -- 'src/*.cs' |
    grep -v '/Zipper\.Tests/' |
    grep -v '/Zipper\.Analyzers' |
    grep -v '/bin/' |
    grep -v '/obj/' |
    grep -v '\.g\.cs$' || true
)

if [[ ${#FILES[@]} -eq 0 ]]; then
    echo "  ✓ No source .cs files changed; mutation smoke skipped."
    exit 0
fi

echo "  Changed source files: ${#FILES[@]}"

# ── Backup / restore (safe even with uncommitted edits) ───────
CURRENT_FILE=""
BACKUP_FILE=""

restore() {
    if [[ -n "$BACKUP_FILE" && -f "$BACKUP_FILE" ]]; then
        cp "$BACKUP_FILE" "$CURRENT_FILE"
        rm -f "$BACKUP_FILE"
    fi
    CURRENT_FILE=""
    BACKUP_FILE=""
}
trap restore EXIT INT TERM HUP QUIT

# ── Changed line numbers from unified diff ────────────────────
changed_lines() {
    git diff -U0 "$MERGE_BASE" -- "$1" |
    grep '^@@' |
    sed -n 's/^@@ -[-0-9,]* +\([0-9][0-9]*\)\(,\([0-9]*\)\)\{0,1\} .*/\1 \3/p' |
    while read -r start count; do
        count=${count:-1}
        [[ "$count" -eq 0 ]] && continue
        for ((i = start; i < start + count; i++)); do
            echo "$i"
        done
    done
}

# ── Mutation operators ────────────────────────────────────────
PATTERNS=(
    's/==/!=/'
    's/!=/==/'
    's/&&/||/'
    's/||/\&\&/'
    's/\btrue\b/false/'
    's/\bfalse\b/true/'
    's/>=/>/'
    's/<=/</'
)
DESCS=(
    '== → !='
    '!= → =='
    '&& → ||'
    '|| → &&'
    'true → false'
    'false → true'
    '>= → >'
    '<= → <'
)

# ── Collect candidates ────────────────────────────────────────
CANDIDATES=()
for file in "${FILES[@]}"; do
    [[ -f "$file" ]] || continue
    LINES=()
    while IFS= read -r ln; do
        [[ -n "$ln" ]] && LINES+=("$ln")
    done < <(changed_lines "$file")

    for ln in ${LINES[@]+"${LINES[@]}"}; do
        content=$(sed -n "${ln}p" "$file" 2>/dev/null) || continue
        # Skip comments, attributes, using directives, blanks
        [[ "$content" =~ ^[[:space:]]*// ]] && continue
        [[ "$content" =~ ^[[:space:]]*/\* ]] && continue
        [[ "$content" =~ ^[[:space:]]*\* ]] && continue
        [[ "$content" =~ ^[[:space:]]*\[.* ]] && continue
        [[ "$content" =~ ^[[:space:]]*using[[:space:]] ]] && continue

        # Strip inline comments before candidate matching
        code_only="${content%%//*}"
        trimmed="${code_only// /}"
        [[ -z "$trimmed" ]] && continue

        for idx in "${!PATTERNS[@]}"; do
            mutated=$(printf '%s\n' "$code_only" | sed -E "${PATTERNS[$idx]}" 2>/dev/null) || continue
            if [[ "$mutated" != "$code_only" ]]; then
                CANDIDATES+=("${file}|${ln}|${idx}")
            fi
        done
    done
done

if [[ ${#CANDIDATES[@]} -eq 0 ]]; then
    echo "  ✓ No mutable patterns on changed lines; mutation smoke skipped."
    exit 0
fi

TOTAL=${#CANDIDATES[@]}
if [[ $TOTAL -gt $MAX ]]; then
    CANDIDATES=("${CANDIDATES[@]:0:$MAX}")
fi
echo "  Testing ${#CANDIDATES[@]} of $TOTAL candidate mutations (max $MAX)"

# ── Timeout command (portable: Linux timeout / macOS gtimeout) ─
TCMD=""
if command -v timeout >/dev/null 2>&1; then
    TCMD="timeout"
elif command -v gtimeout >/dev/null 2>&1; then
    TCMD="gtimeout"
fi

TEST_PROJ="src/Zipper.Tests/Zipper.Tests.csproj"
T0=$(date +%s)

# Verify baseline build and tests before mutating (enforce baseline timeout)
BUILD=(dotnet build "$TEST_PROJ" --nologo -v quiet)
[[ -n "$TCMD" ]] && BUILD=("$TCMD" 60 "${BUILD[@]}")
if ! "${BUILD[@]}" >/dev/null 2>&1; then
    echo "  ❌ Baseline build failed before mutation smoke; check compilation errors." >&2
    exit 1
fi
TEST=(dotnet test "$TEST_PROJ" --nologo --no-build)
[[ -n "$TCMD" ]] && TEST=("$TCMD" 60 "${TEST[@]}")
if ! "${TEST[@]}" >/dev/null 2>&1; then
    echo "  ❌ Baseline tests failed before mutation smoke; unit tests must pass before mutation smoke." >&2
    exit 1
fi

# ── Run mutations ─────────────────────────────────────────────
SURVIVORS=()
KILLED=0
SKIPPED=0
TESTED=0

for candidate in "${CANDIDATES[@]}"; do
    NOW=$(date +%s)
    if [[ $((NOW - T0)) -ge $OVERALL_TIMEOUT ]]; then
        echo "  ⏱ Overall timeout (${OVERALL_TIMEOUT}s) reached after $TESTED of ${#CANDIDATES[@]} mutations."
        SURVIVORS+=("TIMED_OUT: overall timeout reached before all candidates were tested ($TESTED/${#CANDIDATES[@]} tested)")
        break
    fi

    IFS='|' read -r file ln idx <<< "$candidate"
    desc="${DESCS[$idx]}"
    TESTED=$((TESTED + 1))

    printf "  [%d/%d] %s:%s %s ... " \
        "$TESTED" "${#CANDIDATES[@]}" "$(basename "$file")" "$ln" "$desc"

    # Backup and mutate (portable: no sed -i)
    CURRENT_FILE="$file"
    BACKUP_FILE="${file}.mutation-bak"
    cp "$file" "$BACKUP_FILE"
    tmp="${file}.mutation-tmp"
    sed -E "${ln}${PATTERNS[$idx]}" "$file" > "$tmp" && mv "$tmp" "$file"

    # Phase 1: build — compile error = invalid mutant, skip
    BUILD=(dotnet build "$TEST_PROJ" --nologo -v quiet)
    [[ -n "$TCMD" ]] && BUILD=("$TCMD" 30 "${BUILD[@]}")
    set +e
    "${BUILD[@]}" >/dev/null 2>&1
    build_exit=$?
    set -e
    if [[ $build_exit -eq 124 ]]; then
        echo "timeout (build >30s) ⚠"
        SURVIVORS+=("$file:$ln — $desc (build timed out)")
        restore
        continue
    elif [[ $build_exit -ne 0 ]]; then
        echo "skipped (compile error)"
        SKIPPED=$((SKIPPED + 1))
        restore
        continue
    fi

    # Phase 2: test — failure = killed (~16 s)
    TEST=(dotnet test "$TEST_PROJ" --nologo --no-build)
    [[ -n "$TCMD" ]] && TEST=("$TCMD" 30 "${TEST[@]}")
    set +e
    "${TEST[@]}" >/dev/null 2>&1
    test_exit=$?
    set -e
    if [[ $test_exit -eq 124 ]]; then
        echo "timeout (test >30s) ⚠"
        SURVIVORS+=("$file:$ln — $desc (test timed out)")
    elif [[ $test_exit -ne 0 ]]; then
        echo "killed ✓"
        KILLED=$((KILLED + 1))
    else
        echo "SURVIVED ⚠"
        SURVIVORS+=("$file:$ln — $desc")
    fi

    restore
done

# ── Summary ───────────────────────────────────────────────────
echo ""
echo "  Mutation smoke: $KILLED killed, ${#SURVIVORS[@]} survived, $SKIPPED skipped ($TESTED tested)"

if [[ ${#SURVIVORS[@]} -gt 0 ]]; then
    echo ""
    echo "  Surviving mutants (tests did not catch these changes):"
    for s in "${SURVIVORS[@]}"; do
        echo "    ⚠ $s"
    done
    echo ""
    echo "  How to fix: Add targeted unit tests in src/Zipper.Tests/ to assert the"
    echo "  behavior of the surviving conditions/operators until all mutants are killed."
    echo "  Verify locally with: bash tests/run-mutation-smoke.sh"
    exit 1
fi

exit 0
