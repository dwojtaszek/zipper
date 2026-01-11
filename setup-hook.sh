#!/bin/bash

# This script sets up a Git pre-commit hook to run unit tests only.
# E2E tests run on pre-push and CI/CD for faster commit feedback.

HOOK_DIR=".git/hooks"
HOOK_FILE="$HOOK_DIR/pre-commit"

# Create the hooks directory if it doesn't exist.
mkdir -p "$HOOK_DIR"

# Create the pre-commit hook.
cat > "$HOOK_FILE" << EOL
#!/bin/sh
#
# Pre-commit hook: bd sync + unit tests
# E2E tests run on pre-push and CI/CD only (not on commit)
#

# ──────────────────────────────────────────────────────────
# 1. bd sync (flush pending changes to JSONL)
# ──────────────────────────────────────────────────────────
if command -v bd >/dev/null 2>&1; then
    # Determine .beads directory (handles worktrees)
    BEADS_DIR=""
    if git rev-parse --git-dir >/dev/null 2>&1; then
        if [ "\$(git rev-parse --git-dir)" != "\$(git rev-parse --git-common-dir)" ]; then
            # Worktree: .beads is in main repo root
            MAIN_REPO_ROOT="\$(dirname "\$(git rev-parse --git-common-dir)")"
            [ -d "\$MAIN_REPO_ROOT/.beads" ] && BEADS_DIR="\$MAIN_REPO_ROOT/.beads"
        else
            # Regular repo
            [ -d .beads ] && BEADS_DIR=".beads"
        fi
    fi

    if [ -n "\$BEADS_DIR" ]; then
        if ! bd sync --flush-only >/dev/null 2>&1; then
            echo "Error: Failed to flush bd changes to JSONL" >&2
            echo "Run 'bd sync --flush-only' manually to diagnose" >&2
            exit 1
        fi

        # Stage JSONL if modified (only for regular repos, not worktrees)
        if [ -f "\$BEADS_DIR/issues.jsonl" ] && \\
           [ "\$(git rev-parse --git-dir)" = "\$(git rev-parse --git-common-dir)" ]; then
            git add "\$BEADS_DIR/issues.jsonl" 2>/dev/null || true
        fi
    fi
fi

# ──────────────────────────────────────────────────────────
# 2. Run dotnet format (auto-fix formatting)
# ──────────────────────────────────────────────────────────
if command -v dotnet >/dev/null 2>&1; then
    if ! dotnet format --verbosity quiet 2>/dev/null; then
        echo "Error: dotnet format failed" >&2
        exit 1
    fi

    # Fail commit if formatting made changes
    if ! git diff --exit-code --quiet 2>/dev/null; then
        echo "Code formatting changes required. Files have been auto-formatted." >&2
        echo "Please review and commit again." >&2
        git --no-pager diff --stat
        exit 1
    fi
fi

# ──────────────────────────────────────────────────────────
# 3. Run unit tests (fast, local validation)
# ──────────────────────────────────────────────────────────
if command -v dotnet >/dev/null 2>&1; then
    dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj --logger "console;verbosity=quiet" --no-build 2>/dev/null || {
        echo "Unit tests failed. Run 'dotnet test' for details." >&2
        exit 1
    }
fi

exit 0
EOL

# Make the hook executable.
chmod +x "$HOOK_FILE"

echo "Pre-commit hook created successfully (unit tests only, E2E on push)."
