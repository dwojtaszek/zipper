#!/bin/bash

# Installs the version-controlled git hooks from .github/hooks/ into .git/hooks/
#
# Hooks:
#   pre-commit  - dotnet format + unit tests (staged snapshot, skips docs-only)
#   pre-push    - unit tests + basic E2E smoke (skips unit tests if pre-commit just ran)
#
# Usage: bash setup-hook.sh

set -eu

HOOK_DIR=".git/hooks"
TEMPLATE_DIR=".github/hooks"

if [ ! -d .git ]; then
    echo "Error: not a git repository (no .git directory)." >&2
    exit 1
fi

mkdir -p "$HOOK_DIR"

install_hook() {
    local name="$1"
    local src="$TEMPLATE_DIR/$name"
    local dst="$HOOK_DIR/$name"
    if [ ! -f "$src" ]; then
        echo "Warning: template $src not found — skipping $name" >&2
        return 0
    fi
    cp "$src" "$dst"
    chmod +x "$dst"
    echo "Installed: $dst"
}

install_hook "pre-commit"
install_hook "pre-push"

# Optional PowerShell variant for Windows dev envs without Git Bash.
if [ -f "$TEMPLATE_DIR/pre-push.ps1" ]; then
    cp "$TEMPLATE_DIR/pre-push.ps1" "$HOOK_DIR/pre-push.ps1"
    echo "Installed: $HOOK_DIR/pre-push.ps1 (PowerShell fallback)"
fi

echo ""
echo "Done. Hooks installed from $TEMPLATE_DIR/ into $HOOK_DIR/"
echo "  pre-commit -> format + unit tests (stashed staged snapshot)"
echo "  pre-push   -> unit tests (skippable) + basic E2E (5 cases)"
echo ""
echo "Bypass with: git commit --no-verify / git push --no-verify"
