#!/usr/bin/env bash
# Smart local developer CLI wrapper for Zipper.
# Inspects git diff against base branch (default: main) and runs only the affected
# local verification gates, reusing a single Release build across all steps.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

COMMAND="check"
BASE_REF="${DEV_BASE_REF:-main}"
DRY_RUN=0
FULL_E2E=0
ALL_TESTS=0
FILES_OVERRIDE=""
EXTRA_ARGS=()

usage() {
    cat <<'EOF'
Usage: ./dev.sh [command] [options]

Commands:
  check       Run only verification gates affected by current changes (default)
  test        Build Release once and run unit + property tests (--all includes analyzers)
  e2e         Run E2E smoke suite (--full runs full tests/run-tests.sh suite)
  mutation    Run Roslyn incremental mutation smoke (tests/run-mutation-smoke.sh)
  lint        Run dotnet format verification and E2E script parity validation
  hooks       Install/sync local Git hooks (setup-hook.sh)
  all         Run all required pre-PR gates (build once, lint, unit/analyzer tests, mutation, E2E smoke)
  help        Show this help message

Options:
  --base <ref>     Base Git ref for diff detection (default: main)
  --files <list>   Comma- or newline-separated file list override (for testing/CI)
  --dry-run        Print selected gates without executing them
  --full           With 'e2e', run full ./tests/run-tests.sh instead of basic smoke
  --all            With 'test', also run Zipper.Analyzers.Tests
EOF
}

if [[ $# -gt 0 ]]; then
    case "$1" in
        check|test|e2e|mutation|lint|hooks|all|help|-h|--help)
            COMMAND="$1"
            shift
            ;;
        -*)
            ;;
        *)
            echo "Error: Unknown command '$1'." >&2
            usage >&2
            exit 1
            ;;
    esac
fi

while [[ $# -gt 0 ]]; do
    case "$1" in
        --base)
            if [[ $# -lt 2 || -z "${2:-}" ]]; then
                echo "Error: --base requires a Git ref argument" >&2
                exit 1
            fi
            BASE_REF="$2"
            shift 2
            ;;
        --files)
            if [[ $# -lt 2 ]]; then
                echo "Error: --files requires a comma-separated file list" >&2
                exit 1
            fi
            FILES_OVERRIDE="$2"
            shift 2
            ;;
        --dry-run)
            DRY_RUN=1
            shift
            ;;
        --full)
            FULL_E2E=1
            shift
            ;;
        --all)
            ALL_TESTS=1
            shift
            ;;
        -h|--help)
            COMMAND="help"
            shift
            ;;
        *)
            EXTRA_ARGS+=("$1")
            shift
            ;;
    esac
done

if [[ "$COMMAND" == "help" ]]; then
    usage
    exit 0
fi

run_step() {
    local label="$1"
    shift
    if [[ "$DRY_RUN" -eq 1 ]]; then
        echo "[dry-run] $label -> $*"
        return 0
    fi
    echo "==> [dev] $label"
    "$@"
}

ensure_release_build() {
    if [[ "${_DEV_BUILT_RELEASE:-0}" -eq 1 ]]; then
        return 0
    fi
    run_step "Build solution (Release)" dotnet build zipper.sln -c Release
    _DEV_BUILT_RELEASE=1
}

resolve_zipper_binary() {
    if [[ "$DRY_RUN" -eq 1 ]]; then
        ZIPPER_EXECUTABLE="$REPO_ROOT/src/bin/Release/net10.0/Zipper"
        export ZIPPER_EXECUTABLE
        return 0
    fi
    ensure_release_build
    # shellcheck source=tests/_zipper-cli.sh
    source "$REPO_ROOT/tests/_zipper-cli.sh"
    export ZIPPER_EXECUTABLE="${_ZIPPER_BIN}"
}

collect_changed_files() {
    if [[ -n "$FILES_OVERRIDE" ]]; then
        printf '%s\n' "$FILES_OVERRIDE" | tr ',' '\n' | sed '/^[[:space:]]*$/d' | sort -u
        return 0
    fi

    local base_commit=""
    if git rev-parse --verify "${BASE_REF}^{commit}" >/dev/null 2>&1; then
        base_commit="$BASE_REF"
    elif git rev-parse --verify "origin/${BASE_REF}^{commit}" >/dev/null 2>&1; then
        base_commit="origin/${BASE_REF}"
    else
        echo "Error: Cannot resolve base ref '${BASE_REF}' (checked '${BASE_REF}' and 'origin/${BASE_REF}')." >&2
        return 1
    fi

    {
        git diff --name-only "${base_commit}...HEAD" 2>/dev/null || true
        git diff --cached --name-only 2>/dev/null || true
        git diff --name-only 2>/dev/null || true
        git ls-files --others --exclude-standard 2>/dev/null || true
    } | sed '/^[[:space:]]*$/d' | sort -u
}

DOCS_REGEX='(^|/)(docs/|CHANGELOG(\.md)?$|.*\.(md|markdown)$)'

run_check() {
    local raw_files
    if ! raw_files="$(collect_changed_files)"; then
        return 1
    fi
    local changed_files=()
    while IFS= read -r _f; do
        [[ -n "$_f" ]] && changed_files+=("$_f")
    done <<< "$raw_files"
    if [[ "${#changed_files[@]}" -eq 0 ]]; then
        echo "[dev] No changed files detected relative to '$BASE_REF'. Nothing to run."
        return 0
    fi

    local need_build=0
    local need_format=0
    local need_unit_tests=0
    local need_docs_drift=0
    local need_analyzer_tests=0
    local need_mutation=0
    local need_e2e_smoke=0
    local need_parity=0
    local need_ci_tests=0
    local specific_e2e_scripts=()

    for file in "${changed_files[@]}"; do
        if echo "$file" | grep -q -E "$DOCS_REGEX"; then
            need_docs_drift=1
            continue
        fi

        case "$file" in
            src/Zipper.Analyzers/*|src/Zipper.Analyzers.Tests/*)
                need_build=1
                need_format=1
                need_analyzer_tests=1
                ;;
            src/Zipper.Tests/*)
                need_build=1
                need_format=1
                need_unit_tests=1
                ;;
            src/*)
                need_build=1
                need_format=1
                need_unit_tests=1
                if [[ "$file" == *.cs ]]; then
                    need_mutation=1
                    need_e2e_smoke=1
                fi
                ;;
            Directory.Build.props|zipper.sln|BannedSymbols.txt|global.json)
                need_build=1
                need_format=1
                need_unit_tests=1
                need_analyzer_tests=1
                need_parity=1
                ;;
            .github/workflows/*|.github/scripts/*|.github/actions/*|tools/*|tests/ci/*|tests/perf/*)
                need_ci_tests=1
                need_docs_drift=1
                ;;
            .github/hooks/*|setup-hook.sh|setup-hook.bat|dev.sh|dev.bat)
                need_parity=1
                need_ci_tests=1
                need_docs_drift=1
                ;;
            tests/*)
                need_parity=1
                need_docs_drift=1
                if [[ "$file" =~ ^tests/(test-[a-zA-Z0-9._-]+|run-e2e-[a-zA-Z0-9._-]+)\.sh$ ]]; then
                    specific_e2e_scripts+=("$file")
                fi
                ;;
            *)
                need_build=1
                need_unit_tests=1
                ;;
        esac
    done

    # Full unit test suite already includes DocsDriftTests
    if [[ "$need_unit_tests" -eq 1 ]]; then
        need_docs_drift=0
    fi

    if [[ "$need_build" -eq 1 || "$need_unit_tests" -eq 1 || "$need_docs_drift" -eq 1 || "$need_analyzer_tests" -eq 1 || "$need_e2e_smoke" -eq 1 || "${#specific_e2e_scripts[@]}" -gt 0 ]]; then
        ensure_release_build
    fi

    if [[ "$need_format" -eq 1 ]]; then
        run_step "Verify code formatting" dotnet format --verify-no-changes src/
    fi

    if [[ "$need_parity" -eq 1 ]]; then
        run_step "Validate E2E .sh/.bat parity" bash tests/validate-e2e-parity.sh
        run_step "Validate build properties contract" bash tests/test-ci-build-props.sh
    fi

    if [[ "$need_ci_tests" -eq 1 ]]; then
        run_step "Run CI workflow contract tests" python3 -m unittest discover -s tests/ci -p "test_*.py"
        run_step "Run workflow artifact handling check" bash tests/test-artifact-handling.sh
        run_step "Run coverage gate action tests" bash .github/actions/coverage-gate/test-coverage-gate.sh
    fi

    if [[ "$need_docs_drift" -eq 1 ]]; then
        run_step "Run DocsDriftTests" dotnet test src/Zipper.Tests/Zipper.Tests.csproj --no-build -c Release --filter "FullyQualifiedName~DocsDriftTests"
    fi

    if [[ "$need_unit_tests" -eq 1 ]]; then
        run_step "Run unit + property tests" dotnet test src/Zipper.Tests/Zipper.Tests.csproj --no-build -c Release
    fi

    if [[ "$need_analyzer_tests" -eq 1 ]]; then
        run_step "Run Roslyn analyzer tests" dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj --no-build -c Release
    fi

    if [[ "$need_mutation" -eq 1 ]]; then
        run_step "Run incremental mutation smoke" bash tests/run-mutation-smoke.sh
    fi

    if [[ "$need_e2e_smoke" -eq 1 || "${#specific_e2e_scripts[@]}" -gt 0 ]]; then
        resolve_zipper_binary
    fi
    if [[ "$need_e2e_smoke" -eq 1 ]]; then
        run_step "Run basic E2E smoke" bash tests/run-e2e-basic.sh
    fi
    for script in ${specific_e2e_scripts[@]+"${specific_e2e_scripts[@]}"}; do
        if [[ "$need_e2e_smoke" -eq 1 && "$script" == "tests/run-e2e-basic.sh" ]]; then
            continue
        fi
        if [[ -f "$script" ]]; then
            run_step "Run modified E2E script ($script)" bash "$script"
        fi
    done
}

case "$COMMAND" in
    check)
        run_check
        ;;
    test)
        ensure_release_build
        run_step "Run unit + property tests" dotnet test src/Zipper.Tests/Zipper.Tests.csproj --no-build -c Release ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"}
        if [[ "$ALL_TESTS" -eq 1 ]]; then
            run_step "Run Roslyn analyzer tests" dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj --no-build -c Release
        fi
        ;;
    e2e)
        resolve_zipper_binary
        if [[ "$FULL_E2E" -eq 1 ]]; then
            run_step "Run full E2E suite" bash tests/run-tests.sh
        else
            run_step "Run basic E2E smoke" bash tests/run-e2e-basic.sh
        fi
        ;;
    mutation)
        run_step "Run incremental mutation smoke" bash tests/run-mutation-smoke.sh ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"}
        ;;
    lint)
        run_step "Verify code formatting" dotnet format --verify-no-changes src/
        run_step "Validate E2E .sh/.bat parity" bash tests/validate-e2e-parity.sh
        ;;
    hooks)
        run_step "Sync local Git hooks" bash setup-hook.sh
        ;;
    all)
        ensure_release_build
        run_step "Verify code formatting" dotnet format --verify-no-changes src/
        run_step "Validate E2E .sh/.bat parity" bash tests/validate-e2e-parity.sh
        run_step "Validate build properties contract" bash tests/test-ci-build-props.sh
        run_step "Run unit + property tests" dotnet test src/Zipper.Tests/Zipper.Tests.csproj --no-build -c Release
        run_step "Run Roslyn analyzer tests" dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj --no-build -c Release
        run_step "Run CI workflow contract tests" python3 -m unittest discover -s tests/ci -p "test_*.py"
        run_step "Run workflow artifact handling check" bash tests/test-artifact-handling.sh
        run_step "Run coverage gate action tests" bash .github/actions/coverage-gate/test-coverage-gate.sh
        run_step "Run incremental mutation smoke" bash tests/run-mutation-smoke.sh
        resolve_zipper_binary
        run_step "Run basic E2E smoke" bash tests/run-e2e-basic.sh
        ;;
esac
