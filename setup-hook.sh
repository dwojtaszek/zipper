#!/bin/bash

# Installs the version-controlled git hooks from .github/hooks/ into the
# correct hooks directory for the current checkout (supports worktrees).
#
# Hooks:
#   pre-commit   - dotnet format + unit tests (staged snapshot, skips docs-only)
#   post-commit  - records success marker for pre-push skip-if-recent
#   pre-push     - unit tests + basic E2E smoke (skips unit tests if pre-commit just ran)
#
# Usage: bash setup-hook.sh

set -eu

TEMPLATE_DIR=".github/hooks"

# Works for both normal repos (returns .git) and worktrees (returns the
# shared common dir under the main repo). Falls back gracefully.
if ! HOOK_DIR=$(git rev-parse --git-common-dir 2>/dev/null)/hooks; then
    echo "Error: not inside a git repository."
    exit 1
fi

mkdir -p "$HOOK_DIR"

install_hook() {
    local name="$1"
    local src="$TEMPLATE_DIR/$name"
    local dst="$HOOK_DIR/$name"
    if [[ ! -f "$src" ]]; then
        echo "Warning: template $src not found — skipping $name"
        return 0
    fi
    cp "$src" "$dst"
    chmod u+x "$dst"
    echo "Installed: $dst"
}

install_hook "pre-commit"
install_hook "post-commit"
install_hook "pre-push"

echo ""
echo "Done. Hooks installed from $TEMPLATE_DIR/ into $HOOK_DIR/"
echo "  pre-commit  -> format + unit tests (stashed staged snapshot)"
echo "  post-commit -> writes success marker for pre-push skip optimization"
echo "  pre-push    -> unit tests (skippable) + basic E2E (5 cases)"
echo ""
echo "Bypass with: git commit --no-verify / git push --no-verify"
