#!/usr/bin/env bash
# Smart local developer CLI wrapper for Zipper.
# Inspects git diff against base branch (default: main) and runs only the affected
# local verification gates, reusing a single Release build across all steps.

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

COMMAND="check"
BASE_REF="${DEV_BASE_REF:-main}"
EXPLICIT_BASE=0
if [[ -n "${DEV_BASE_REF:-}" ]]; then
    EXPLICIT_BASE=1
fi
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
  lint        Run dotnet format verification, shell/Python lint, and E2E script parity validation
  sonar       Query SonarCloud quality gate and open issues for a PR (./dev.sh sonar <PR>)
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
        check|test|e2e|mutation|lint|sonar|hooks|all|help|-h|--help)
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
            EXPLICIT_BASE=1
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
    local log_dir="${DEV_CHECK_LOG_DIR:-}"
    if [[ -z "$log_dir" ]]; then
        log_dir=$(mktemp -d "${TMPDIR:-/tmp}/zipper-dev.XXXXXX") || return 1
    else
        mkdir -p "$log_dir"
    fi
    python3 .github/scripts/run-check.py --log "$(mktemp "$log_dir/check.XXXXXX")" -- "$@"
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
    elif [[ "$EXPLICIT_BASE" -eq 0 ]] && git rev-parse --verify "HEAD~1^{commit}" >/dev/null 2>&1; then
        echo "Warning: Default base ref '${BASE_REF}' not found; falling back to 'HEAD~1'." >&2
        base_commit="HEAD~1"
    elif [[ "$EXPLICIT_BASE" -eq 0 ]] && git rev-parse --verify "HEAD^{commit}" >/dev/null 2>&1; then
        echo "Warning: Default base ref '${BASE_REF}' not found; falling back to 'HEAD'." >&2
        base_commit="HEAD"
    else
        echo "Error: Cannot resolve base ref '${BASE_REF}' (checked '${BASE_REF}' and 'origin/${BASE_REF}')." >&2
        return 1
    fi

    local committed staged working untracked
    committed=$(git diff --name-only --no-renames "${base_commit}...HEAD") || return 1
    staged=$(git diff --cached --name-only --no-renames) || return 1
    working=$(git diff --name-only --no-renames) || return 1
    untracked=$(git ls-files --others --exclude-standard) || return 1
    printf '%s\n' "$committed" "$staged" "$working" "$untracked" | sed '/^[[:space:]]*$/d' | sort -u
}


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
    local need_runner_tests=0
    local need_python_lint=0
    local need_shell_lint=0
    local need_complexity=0
    local specific_e2e_scripts=()

    local scope
    scope=$(printf '%s\0' "${changed_files[@]}" | python3 .github/scripts/change-scope.py)
    if [[ "$scope" == *"only_runner=true"* ]]; then
        need_runner_tests=1
        need_docs_drift=1
        need_python_lint=1
        need_complexity=1
    else
    for file in "${changed_files[@]}"; do
        scope=$(printf '%s\0' "$file" | python3 .github/scripts/change-scope.py)
        if [[ "$scope" == *"only_docs=true"* ]]; then
            need_docs_drift=1
            continue
        fi

        case "$file" in
            src/Zipper.Analyzers/*|src/Zipper.Analyzers.Tests/*)
                need_build=1
                need_format=1
                need_analyzer_tests=1
                if [[ "$file" == src/Zipper.Analyzers/*.cs || "$file" == src/Zipper.Analyzers/*/*.cs ]]; then
                    need_complexity=1
                fi
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
                    need_complexity=1
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
                if [[ "$file" == *.py ]]; then
                    need_python_lint=1
                    need_complexity=1
                fi
                ;;
            .github/hooks/*|setup-hook.sh|setup-hook.bat|dev.sh|dev.bat)
                need_parity=1
                need_ci_tests=1
                need_docs_drift=1
                if [[ "$file" != *.bat ]]; then
                    need_shell_lint=1
                fi
                ;;
            .zipper-runner/*)
                need_runner_tests=1
                need_docs_drift=1
                if [[ "$file" == *.py ]]; then
                    need_python_lint=1
                    need_complexity=1
                fi
                ;;
            .pi-lens.json)
                ;;
            ruff.toml)
                need_python_lint=1
                ;;
            tests/wait-for-reviews.sh|tests/test-unified-workflow.sh|tests/test-unified-workflow.bat|tests/test-artifact-handling.sh|tests/test-ci-build-props.sh|tests/validate-*.sh|tests/validate-*.bat)
                need_ci_tests=1
                need_parity=1
                need_docs_drift=1
                if [[ "$file" == tests/wait-for-reviews.sh ]]; then
                    need_shell_lint=1
                fi
                if [[ "$file" =~ ^tests/(test-[a-zA-Z0-9._-]+|run-e2e-[a-zA-Z0-9._-]+)\.sh$ ]]; then
                    specific_e2e_scripts+=("$file")
                fi
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

    fi

    # Full unit test suite already includes DocsDriftTests
    if [[ "$need_unit_tests" -eq 1 ]]; then
        need_docs_drift=0
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

    if [[ "$need_build" -eq 1 || "$need_unit_tests" -eq 1 || "$need_docs_drift" -eq 1 || "$need_analyzer_tests" -eq 1 || "$need_e2e_smoke" -eq 1 || "${#specific_e2e_scripts[@]}" -gt 0 ]]; then
        ensure_release_build
        if [[ "$need_ci_tests" -eq 1 ]]; then
            run_step "Verify SDK hook synchronization" env PYTHONPATH=tests/ci python3 -m unittest test_dev_cli.DevCliAndLocalWorkflowTests.test_directory_build_props_msbuild_sync_git_hooks_targets_succeed
        fi
    fi

    if [[ "$need_format" -eq 1 ]]; then
        run_step "Verify code formatting" dotnet format --verify-no-changes zipper.sln
    fi

    if [[ "$need_python_lint" -eq 1 ]]; then
        if command -v ruff >/dev/null 2>&1; then
            run_step "Run Python lint (ruff)" ruff check
        else
            echo "Warning: ruff not found on PATH; skipping local Python lint." >&2
        fi
    fi

    if [[ "$need_shell_lint" -eq 1 ]]; then
        if command -v shellcheck >/dev/null 2>&1; then
            run_step "Run shell lint (shellcheck)" shellcheck -S warning tests/wait-for-reviews.sh dev.sh setup-hook.sh .github/hooks/pre-commit .github/hooks/pre-push .github/hooks/post-commit
        else
            echo "Warning: shellcheck not found on PATH; skipping local shell lint." >&2
        fi
    fi

    if [[ "$need_complexity" -eq 1 && -f .github/scripts/check-complexity.py ]]; then
        if [[ -n "$FILES_OVERRIDE" ]]; then
            run_step "Check diff complexity" python3 .github/scripts/check-complexity.py --files "$FILES_OVERRIDE"
        else
            run_step "Check diff complexity" python3 .github/scripts/check-complexity.py --base "$BASE_REF"
        fi
    fi


    if [[ "$need_runner_tests" -eq 1 ]]; then
        run_step "Run zipper-runner tests" python3 -m unittest discover -s .zipper-runner/tests -p "test_*.py"
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
        run_step "Verify code formatting" dotnet format --verify-no-changes zipper.sln
        run_step "Validate E2E .sh/.bat parity" bash tests/validate-e2e-parity.sh
        if command -v ruff >/dev/null 2>&1; then
            run_step "Run Python lint (ruff)" ruff check
        else
            echo "Warning: ruff not found on PATH; skipping local Python lint." >&2
        fi
        if command -v shellcheck >/dev/null 2>&1; then
            run_step "Run shell lint (shellcheck)" shellcheck -S warning tests/wait-for-reviews.sh dev.sh setup-hook.sh .github/hooks/pre-commit .github/hooks/pre-push .github/hooks/post-commit
        else
            echo "Warning: shellcheck not found on PATH; skipping local shell lint." >&2
        fi
        ;;
    sonar)
        python3 .github/scripts/check-sonar.py ${EXTRA_ARGS[@]+"${EXTRA_ARGS[@]}"}
        ;;
    hooks)
        run_step "Sync local Git hooks" bash setup-hook.sh
        ;;
    all)
        run_step "Validate E2E .sh/.bat parity" bash tests/validate-e2e-parity.sh
        run_step "Validate build properties contract" bash tests/test-ci-build-props.sh
        run_step "Run CI workflow contract tests" python3 -m unittest discover -s tests/ci -p "test_*.py"
        run_step "Run zipper-runner tests" python3 -m unittest discover -s .zipper-runner/tests -p "test_*.py"
        run_step "Run workflow artifact handling check" bash tests/test-artifact-handling.sh
        run_step "Run coverage gate action tests" bash .github/actions/coverage-gate/test-coverage-gate.sh
        if command -v ruff >/dev/null 2>&1; then
            run_step "Run Python lint (ruff)" ruff check
        else
            echo "Warning: ruff not found on PATH; skipping local Python lint." >&2
        fi
        if command -v shellcheck >/dev/null 2>&1; then
            run_step "Run shell lint (shellcheck)" shellcheck -S warning tests/wait-for-reviews.sh dev.sh setup-hook.sh .github/hooks/pre-commit .github/hooks/pre-push .github/hooks/post-commit
        else
            echo "Warning: shellcheck not found on PATH; skipping local shell lint." >&2
        fi
        if [[ -f .github/scripts/check-complexity.py ]]; then
            run_step "Check diff complexity" python3 .github/scripts/check-complexity.py --base "$BASE_REF"
        fi
        ensure_release_build
        run_step "Verify SDK hook synchronization" env PYTHONPATH=tests/ci python3 -m unittest test_dev_cli.DevCliAndLocalWorkflowTests.test_directory_build_props_msbuild_sync_git_hooks_targets_succeed
        run_step "Verify code formatting" dotnet format --verify-no-changes zipper.sln
        run_step "Run unit + property tests" dotnet test src/Zipper.Tests/Zipper.Tests.csproj --no-build -c Release
        run_step "Run Roslyn analyzer tests" dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj --no-build -c Release
        run_step "Run incremental mutation smoke" bash tests/run-mutation-smoke.sh
        resolve_zipper_binary
        run_step "Run basic E2E smoke" bash tests/run-e2e-basic.sh
        ;;
esac
