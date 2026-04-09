#!/bin/bash

# Sets up Git hooks for the zipper project:
# - pre-commit: dotnet format + unit tests
# - pre-push:   unit tests + basic E2E smoke suite
#
# Usage: bash setup-hook.sh

HOOK_DIR=".git/hooks"
mkdir -p "$HOOK_DIR"

# ────────────────────────────────────────────────
# 1. Pre-commit hook (generated inline)
# ────────────────────────────────────────────────
HOOK_FILE="$HOOK_DIR/pre-commit"

cat > "$HOOK_FILE" << 'EOL'
#!/bin/sh
#
# Pre-commit hook: dotnet format + unit tests
# E2E tests run on pre-push only (not on commit)
#

# ──────────────────────────────────────────────────────────
# 1. Run dotnet format (auto-fix formatting)
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
# 2. Run unit tests (fast, local validation)
# ──────────────────────────────────────────────────────────
if command -v dotnet >/dev/null 2>&1; then
    dotnet test src/Zipper.Tests/Zipper.Tests.csproj --logger "console;verbosity=quiet" 2>/dev/null || {
        echo "Unit tests failed. Run 'dotnet test' for details." >&2
        exit 1
    }
fi

exit 0
EOL

chmod +x "$HOOK_FILE"
echo "✅ Pre-commit hook installed (format + unit tests)"

# ────────────────────────────────────────────────
# 2. Pre-push hook (copied from template)
# ────────────────────────────────────────────────
PUSH_HOOK_FILE="$HOOK_DIR/pre-push"
PUSH_HOOK_TEMPLATE=".github/hooks/pre-push"

if [ -f "$PUSH_HOOK_TEMPLATE" ]; then
    cp "$PUSH_HOOK_TEMPLATE" "$PUSH_HOOK_FILE"
    chmod +x "$PUSH_HOOK_FILE"
    echo "✅ Pre-push hook installed (unit tests + basic E2E smoke suite)"
else
    echo "⚠️  Pre-push hook template not found at $PUSH_HOOK_TEMPLATE"
fi

echo ""
echo "Done! Hooks installed in $HOOK_DIR/"
echo "  pre-commit → format + unit tests"
echo "  pre-push   → unit tests + basic E2E (5 cases)"
echo ""
echo "Bypass with: git commit --no-verify / git push --no-verify"
