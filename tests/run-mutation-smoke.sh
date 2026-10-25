#!/bin/bash
# tests/run-mutation-smoke.sh — Lightweight mutation smoke for pre-push.
#
# Applies simple operator mutations to changed lines in .cs source files
# (vs merge-base with main) plus string-literal mutations to value producers,
# builds and runs unit tests to check if the mutation is caught.  Not a
# replacement for full Stryker analysis (which runs weekly in CI via
# typesafe-quality-audit.yml).
#
# Value producers are SyntheticRowValues, Load File composers, collection
# metadata values, column value generators, and profile data tables.  Mutating
# a literal there exposes tests that only assert a column exists instead of
# asserting its value.
#
# Speed:  0 s when no source .cs files changed.
#         ~3 s per compile-error mutation; ~45 s per test-run mutation.
#         Default max 3 mutations → worst case ~2.5 min.
#
# Exit:   0  all killed / nothing to mutate / skipped
#         1  surviving mutants found
#
# Env:    MUTATION_SMOKE_MAX=N       max mutations (default 3)
#         MUTATION_SMOKE_TIMEOUT=N   overall timeout in seconds (default 240)
#         MUTATION_SMOKE_CACHE=0|1   enable content-hash mutant cache (default 1)
#         MUTATION_CACHE_DIR=PATH    override cache directory (default .git/zipper-hooks/mutation-cache)
#         MUTATION_SMOKE_BUILD_TIMEOUT=N  per-mutant build timeout in seconds (default 30)
#         MUTATION_SMOKE_TEST_TIMEOUT=N   per-mutant test timeout in seconds (default 60)
#
# Args:   --list-candidates   print the selected candidates and exit (no build)
#         --max N             max mutations (default 3)
#         --base REF          explicit merge base
#         --no-cache          bypass the mutant result cache

set -u

MAX="${MUTATION_SMOKE_MAX:-3}"
OVERALL_TIMEOUT="${MUTATION_SMOKE_TIMEOUT:-240}"
USE_CACHE="${MUTATION_SMOKE_CACHE:-1}"
BUILD_TIMEOUT="${MUTATION_SMOKE_BUILD_TIMEOUT:-30}"
TEST_TIMEOUT="${MUTATION_SMOKE_TEST_TIMEOUT:-60}"
EXPLICIT_BASE="${MUTATION_SMOKE_BASE:-}"
LIST_CANDIDATES=0

while [[ $# -gt 0 ]]; do
    case "$1" in
        --no-cache)
            USE_CACHE=0
            shift
            ;;
        --list-candidates)
            LIST_CANDIDATES=1
            shift
            ;;
        --max)
            if [[ $# -lt 2 || ! "${2:-}" =~ ^[0-9]+$ || "$2" -lt 1 ]]; then
                echo "Error: --max requires a positive integer" >&2
                exit 1
            fi
            MAX="$2"
            shift 2
            ;;
        --base)
            if [[ $# -lt 2 || -z "${2:-}" ]]; then
                echo "Error: --base requires a Git ref" >&2
                exit 1
            fi
            EXPLICIT_BASE="$2"
            shift 2
            ;;
        *)
            echo "Unknown option: $1" >&2
            exit 1
            ;;
    esac
done

# Clean up / restore any stale backup or temp files from prior interrupted runs
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
for tmp in src/**/*.mutation-tmp src/*.mutation-tmp; do
    [[ -f "$tmp" ]] && rm -f "$tmp"
done 2>/dev/null || true
shopt -u globstar 2>/dev/null || true

# ── Guards ────────────────────────────────────────────────────
BRANCH=$(git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "")
if [[ -n "$EXPLICIT_BASE" ]]; then
    MERGE_BASE=$(git rev-parse "$EXPLICIT_BASE" 2>/dev/null || echo "")
else
    MERGE_BASE=$(git merge-base HEAD origin/main 2>/dev/null || git merge-base HEAD main 2>/dev/null || echo "")
fi

if [[ -z "$EXPLICIT_BASE" && ( "$BRANCH" == "main" || "$BRANCH" == "HEAD" ) ]]; then
    # Allow running mutation smoke against uncommitted working-tree changes on main
    UNCOMMITTED_CS=$(git diff --name-only HEAD -- 'src/*.cs' 2>/dev/null || echo "")
    if [[ -z "$UNCOMMITTED_CS" ]]; then
        echo "  ✓ On main / detached HEAD with no uncommitted C# edits; mutation smoke skipped."
        exit 0
    fi
    MERGE_BASE="HEAD"
elif [[ -z "$MERGE_BASE" ]]; then
    echo "  ⚠ Cannot find merge-base with main or origin/main; mutation smoke skipped."
    exit 0
fi

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
TMP_FILE=""

restore() {
    if [[ -n "$TMP_FILE" && -f "$TMP_FILE" ]]; then
        rm -f "$TMP_FILE"
    fi
    if [[ -n "$BACKUP_FILE" && -f "$BACKUP_FILE" ]]; then
        cp "$BACKUP_FILE" "$CURRENT_FILE"
        rm -f "$BACKUP_FILE"
    fi
    CURRENT_FILE=""
    BACKUP_FILE=""
    TMP_FILE=""
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
    's/\|\|/\&\&/'
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

OPERATOR_COUNT=${#PATTERNS[@]}
PATTERNS+=('s/"(\\"|[^"])*"/"MUTATED"/')
DESCS+=('string literal → "MUTATED"')
STRING_IDX=$(( ${#PATTERNS[@]} - 1 ))

# String-literal mutants only make sense for value producers. Mutating a log
# line or an unscoped file would report a survivor that no test should kill.
is_value_producer_file() {
    case "$1" in
        src/LoadFiles/SyntheticRowValues.cs|src/LoadFiles/*Composer*.cs|src/LoadFiles/CollectionMetadataValues.cs|src/Profiles/Generation/*.cs|src/Profiles/Data/*.cs) return 0 ;;
        *) return 1 ;;
    esac
}

# ── Collect candidates ────────────────────────────────────────
CANDIDATES=()
STRING_CANDIDATES=()
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

        for ((idx = 0; idx < OPERATOR_COUNT; idx++)); do
            mutated=$(printf '%s\n' "$code_only" | sed -E "${PATTERNS[$idx]}" 2>/dev/null) || continue
            if [[ "$mutated" != "$code_only" ]]; then
                CANDIDATES+=("${file}|${ln}|${idx}")
            fi
        done

        if is_value_producer_file "$file" && [[ "$code_only" == *'"'* ]]; then
            case "$code_only" in
                *'"""'*|*Console.*|*"throw "*|*Debug.*|*Trace.*|*".Log"*|*"nameof("*|*Assert.*) ;;
                *)
                    # A bare continuation literal (e.g. a multi-line throw message) is not a
                    # value producer and no test asserts it.
                    if [[ "$trimmed" != \"* ]]; then
                        mutated=$(printf '%s\n' "$code_only" | sed -E "${PATTERNS[$STRING_IDX]}" 2>/dev/null) || continue
                        if [[ "$mutated" != "$code_only" ]]; then
                            STRING_CANDIDATES+=("${file}|${ln}|${STRING_IDX}")
                        fi
                    fi
                    ;;
            esac
        fi
    done
done

if [[ ${#CANDIDATES[@]} -eq 0 && ${#STRING_CANDIDATES[@]} -eq 0 ]]; then
    echo "  ✓ No mutable patterns on changed lines; mutation smoke skipped."
    exit 0
fi

TOTAL=$(( ${#CANDIDATES[@]} + ${#STRING_CANDIDATES[@]} ))

# Reserve a slot for string-literal value mutants, so a weak "column exists"
# test cannot hide behind operator candidates, then fill the remaining budget.
SELECTED=()
if [[ ${#STRING_CANDIDATES[@]} -gt 0 && $MAX -ge 1 ]]; then
    SELECTED+=("${STRING_CANDIDATES[0]}")
fi
for ((i = 0; i < ${#CANDIDATES[@]} && ${#SELECTED[@]} < MAX; i++)); do
    SELECTED+=("${CANDIDATES[$i]}")
done
for ((i = 1; i < ${#STRING_CANDIDATES[@]} && ${#SELECTED[@]} < MAX; i++)); do
    SELECTED+=("${STRING_CANDIDATES[$i]}")
done
CANDIDATES=(${SELECTED[@]+"${SELECTED[@]}"})

if [[ "$LIST_CANDIDATES" == "1" ]]; then
    for candidate in "${CANDIDATES[@]}"; do
        IFS='|' read -r file ln idx <<< "$candidate"
        printf '%s:%s %s\n' "$file" "$ln" "${DESCS[$idx]}"
    done
    exit 0
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

# ── Content-hash mutant result cache ──────────────────────────
CACHE_DIR="${MUTATION_CACHE_DIR:-$(git rev-parse --git-path zipper-hooks/mutation-cache 2>/dev/null || echo "")}"
if [[ "$USE_CACHE" == "1" && -n "$CACHE_DIR" ]]; then
    mkdir -p "$CACHE_DIR" 2>/dev/null || USE_CACHE=0
else
    USE_CACHE=0
fi

compute_sha256() {
    if command -v sha256sum >/dev/null 2>&1; then
        sha256sum "$@" | awk '{print $1}'
    elif command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$@" | awk '{print $1}'
    else
        python3 -c 'import hashlib,sys; h=hashlib.sha256(); [h.update(open(f,"rb").read()) for f in sys.argv[1:]] if len(sys.argv)>1 else h.update(sys.stdin.buffer.read()); print(h.hexdigest())' "$@"
    fi
}

hash_stdin_files() {
    local file_hashes=""
    if command -v sha256sum >/dev/null 2>&1; then
        file_hashes=$(xargs -0 sha256sum 2>/dev/null || true)
        if [[ -n "$file_hashes" && "$file_hashes" != *"  -" ]]; then
            printf '%s\n' "$file_hashes" | sha256sum | awk '{print $1}'
        fi
    elif command -v shasum >/dev/null 2>&1; then
        file_hashes=$(xargs -0 shasum -a 256 2>/dev/null || true)
        if [[ -n "$file_hashes" && "$file_hashes" != *"  -" ]]; then
            printf '%s\n' "$file_hashes" | shasum -a 256 | awk '{print $1}'
        fi
    elif command -v python3 >/dev/null 2>&1; then
        python3 -c 'import hashlib,sys
h=hashlib.sha256()
count=0
for p in sys.stdin.buffer.read().split(b"\0"):
    if p:
        count += 1
        h.update(p + b"\0")
        with open(p, "rb") as f: h.update(f.read())
if count > 0:
    print(h.hexdigest())' 2>/dev/null || true
    fi
}

PROD_TREE_HASH=""
TESTS_TREE_HASH=""
if [[ "$USE_CACHE" == "1" ]]; then
    PROD_TREE_HASH=$(
        (find src Directory.Build.props global.json BannedSymbols.txt -type f \
            -not -path '*/Zipper.Tests/*' \
            -not -path '*/Zipper.Analyzers.Tests/*' \
            -not -path '*/bin/*' \
            -not -path '*/obj/*' \
            -not -name '*.mutation-*' \
            -print0 2>/dev/null | sort -z | hash_stdin_files) || true
    )
    TESTS_TREE_HASH=$(
        (find src/Zipper.Tests -type f \
            -not -path '*/bin/*' \
            -not -path '*/obj/*' \
            -not -name '*.mutation-*' \
            -print0 2>/dev/null | sort -z | hash_stdin_files) || true
    )
    if [[ -z "$PROD_TREE_HASH" || -z "$TESTS_TREE_HASH" ]]; then
        echo "  ⚠ Mutant cache hashing unavailable; falling back to uncached mutation smoke."
        USE_CACHE=0
    fi
fi

# Disable Roslyn analyzers during mutation smoke builds: static analyzers
# (especially CA5403/CA5390 security dataflow rules) already run in standard
# builds/pre-commit, and skipping them cuts per-mutant compile time from ~46s
# down to ~1.1s while ensuring mutants are only skipped on real C# compiler errors.
BUILD_FLAGS=(--nologo -v quiet -p:RunAnalyzers=false -p:RunAnalyzersDuringBuild=false)
if [[ -f "src/Zipper.Tests/obj/project.assets.json" ]]; then
    BUILD_FLAGS+=(--no-restore)
fi

BASELINE_VERIFIED=0
ensure_baseline_verified() {
    if [[ "$BASELINE_VERIFIED" -eq 1 ]]; then
        return 0
    fi

    # Verify baseline build before mutating (enforce baseline timeout)
    BUILD=(dotnet build "$TEST_PROJ" "${BUILD_FLAGS[@]}")
    [[ -n "$TCMD" ]] && BUILD=("$TCMD" 60 "${BUILD[@]}")
    if ! "${BUILD[@]}" >/dev/null 2>&1; then
        # Fallback once with full restore in case packages changed
        BUILD_FALLBACK=(dotnet build "$TEST_PROJ" --nologo -v quiet -p:RunAnalyzers=false -p:RunAnalyzersDuringBuild=false)
        [[ -n "$TCMD" ]] && BUILD_FALLBACK=("$TCMD" 60 "${BUILD_FALLBACK[@]}")
        if ! "${BUILD_FALLBACK[@]}" >/dev/null 2>&1; then
            echo "  ❌ Baseline build failed before mutation smoke; check compilation errors." >&2
            exit 1
        fi
    fi

    # Skip redundant 20s baseline test run when caller (pre-push) or fresh pre-commit.ok already verified HEAD
    SKIP_BASELINE="${MUTATION_SMOKE_SKIP_BASELINE:-0}"
    if [[ "$SKIP_BASELINE" != "1" ]]; then
        MARKER_FILE="$(git rev-parse --git-path zipper-hooks 2>/dev/null || echo "")/pre-commit.ok"
        if [[ -f "$MARKER_FILE" ]] && git diff --quiet 2>/dev/null && git diff --cached --quiet 2>/dev/null; then
            MARKER_TS=$(grep '^timestamp=' "$MARKER_FILE" 2>/dev/null | cut -d= -f2)
            MARKER_HEAD=$(grep '^head=' "$MARKER_FILE" 2>/dev/null | cut -d= -f2)
            CURRENT_HEAD=$(git rev-parse HEAD 2>/dev/null || echo "")
            NOW_TS=$(date +%s)
            if [[ -n "$MARKER_TS" && -n "$MARKER_HEAD" && "$MARKER_HEAD" == "$CURRENT_HEAD" ]] && (( NOW_TS - MARKER_TS < 600 )); then
                SKIP_BASELINE=1
            fi
        fi
    fi

    if [[ "$SKIP_BASELINE" == "1" ]]; then
        echo "  ✓ Baseline unit tests already verified — skipping duplicate baseline test run."
    else
        TEST=(dotnet test "$TEST_PROJ" --nologo --no-build)
        [[ -n "$TCMD" ]] && TEST=("$TCMD" 60 "${TEST[@]}")
        if ! "${TEST[@]}" >/dev/null 2>&1; then
            echo "  ❌ Baseline tests failed before mutation smoke; unit tests must pass before mutation smoke." >&2
            exit 1
        fi
    fi

    BASELINE_VERIFIED=1
}

# ── Run mutations ─────────────────────────────────────────────
SURVIVORS=()
KILLED=0
SKIPPED=0
CACHED=0
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

    # Generate mutated candidate into temporary file first so we can check content-hash cache
    TMP_FILE="${file}.mutation-tmp"
    sed -E "${ln}${PATTERNS[$idx]}" "$file" > "$TMP_FILE"

    stem=$(basename "$file" .cs)
    stem_test_file=$(find src/Zipper.Tests -name "${stem}Tests.cs" -print -quit 2>/dev/null || echo "")
    compile_key=""
    targeted_key=""
    full_key=""

    if [[ "$USE_CACHE" == "1" ]]; then
        mutated_hash=$(compute_sha256 "$TMP_FILE")
        compile_key=$(printf '%s:%s:%s\n' "$PROD_TREE_HASH" "$file" "$mutated_hash" | compute_sha256)
        full_key=$(printf '%s:%s\n' "$compile_key" "$TESTS_TREE_HASH" | compute_sha256)
        targeted_key="$full_key"

        if [[ -f "$CACHE_DIR/compile_${compile_key}" ]]; then
            printf "  [%d/%d] %s:%s %s ... skipped (compile error, cached)\n" \
                "$TESTED" "${#CANDIDATES[@]}" "$(basename "$file")" "$ln" "$desc"
            SKIPPED=$((SKIPPED + 1))
            CACHED=$((CACHED + 1))
            rm -f "$TMP_FILE"
            TMP_FILE=""
            continue
        elif [[ -n "$targeted_key" && -f "$CACHE_DIR/killed_targeted_${targeted_key}" ]] || [[ -f "$CACHE_DIR/killed_full_${full_key}" ]]; then
            printf "  [%d/%d] %s:%s %s ... killed ✓ (cached)\n" \
                "$TESTED" "${#CANDIDATES[@]}" "$(basename "$file")" "$ln" "$desc"
            KILLED=$((KILLED + 1))
            CACHED=$((CACHED + 1))
            rm -f "$TMP_FILE"
            TMP_FILE=""
            continue
        fi
    fi

    ensure_baseline_verified

    printf "  [%d/%d] %s:%s %s ... " \
        "$TESTED" "${#CANDIDATES[@]}" "$(basename "$file")" "$ln" "$desc"

    # Backup and apply mutation (touch ensures mtime is newer than baseline build outputs)
    CURRENT_FILE="$file"
    BACKUP_FILE="${file}.mutation-bak"
    cp "$file" "$BACKUP_FILE"
    mv "$TMP_FILE" "$file"
    touch "$file"
    TMP_FILE=""

    # Phase 1: build — compile error = invalid mutant, skip
    BUILD=(dotnet build "$TEST_PROJ" "${BUILD_FLAGS[@]}")
    [[ -n "$TCMD" ]] && BUILD=("$TCMD" "$BUILD_TIMEOUT" "${BUILD[@]}")
    set +e
    "${BUILD[@]}" >/dev/null 2>&1
    build_exit=$?
    set -e
    if [[ $build_exit -eq 124 ]]; then
        echo "timeout (build >${BUILD_TIMEOUT}s) ⚠"
        SURVIVORS+=("$file:$ln — $desc (build timed out)")
        restore
        continue
    elif [[ $build_exit -ne 0 ]]; then
        echo "skipped (compile error)"
        SKIPPED=$((SKIPPED + 1))
        if [[ "$USE_CACHE" == "1" && -n "$compile_key" ]]; then
            touch "$CACHE_DIR/compile_${compile_key}" 2>/dev/null || true
        fi
        restore
        continue
    fi

    # Phase 2a: fast-path targeted test class (<FileStem>Tests) when present (~1.5 s)
    targeted_killed=0
    if [[ -n "$stem_test_file" ]]; then
        FAST_TEST=(dotnet test "$TEST_PROJ" --nologo --no-build --filter "FullyQualifiedName~${stem}Tests")
        [[ -n "$TCMD" ]] && FAST_TEST=("$TCMD" "$TEST_TIMEOUT" "${FAST_TEST[@]}")
        set +e
        "${FAST_TEST[@]}" >/dev/null 2>&1
        fast_exit=$?
        set -e
        if [[ $fast_exit -ne 0 && $fast_exit -ne 124 ]]; then
            echo "killed ✓"
            KILLED=$((KILLED + 1))
            targeted_killed=1
            if [[ "$USE_CACHE" == "1" && -n "$targeted_key" ]]; then
                touch "$CACHE_DIR/killed_targeted_${targeted_key}" 2>/dev/null || true
            fi
        fi
    fi

    if [[ $targeted_killed -eq 1 ]]; then
        restore
        continue
    fi

    # Phase 2b: fallback to full unit test suite (~31 s)
    TEST=(dotnet test "$TEST_PROJ" --nologo --no-build)
    [[ -n "$TCMD" ]] && TEST=("$TCMD" "$TEST_TIMEOUT" "${TEST[@]}")
    set +e
    "${TEST[@]}" >/dev/null 2>&1
    test_exit=$?
    set -e
    if [[ $test_exit -eq 124 ]]; then
        echo "timeout (test >${TEST_TIMEOUT}s) ⚠"
        SURVIVORS+=("$file:$ln — $desc (test timed out)")
    elif [[ $test_exit -ne 0 ]]; then
        echo "killed ✓"
        KILLED=$((KILLED + 1))
        if [[ "$USE_CACHE" == "1" && -n "$full_key" ]]; then
            touch "$CACHE_DIR/killed_full_${full_key}" 2>/dev/null || true
        fi
    else
        echo "SURVIVED ⚠"
        SURVIVORS+=("$file:$ln — $desc")
    fi

    restore
done

# If any mutant was compiled, restore clean build outputs so subsequent --no-build runs never see a mutated DLL
if [[ "$BASELINE_VERIFIED" -eq 1 ]]; then
    dotnet build "$TEST_PROJ" "${BUILD_FLAGS[@]}" >/dev/null 2>&1 || true
fi

# ── Summary ───────────────────────────────────────────────────
echo ""
echo "  Mutation smoke: $KILLED killed, ${#SURVIVORS[@]} survived, $SKIPPED skipped ($TESTED tested, $CACHED cached)"

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
