#!/bin/bash

# Installs the version-controlled git hooks from .github/hooks/ into the
# correct hooks directory for the current checkout (supports worktrees).
#
# Hooks:
#   pre-commit   - dotnet format + unit tests (staged snapshot, DocsDriftTests fast-path for docs-only)
#   post-commit  - records tree-verified success marker for pre-push skip-if-recent
#   pre-push     - unit tests + mutation smoke + conditional basic E2E smoke
#
# Usage: bash setup-hook.sh [--quiet]

set -eu

QUIET=0
if [[ "${1:-}" == "--quiet" || "${1:-}" == "-q" ]]; then
    QUIET=1
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$REPO_ROOT"

TEMPLATE_DIR=".github/hooks"

# Works for both normal repos (returns .git) and worktrees (returns the
# shared common dir under the main repo). Falls back gracefully.
if ! GIT_COMMON_DIR="$(git rev-parse --git-common-dir 2>/dev/null)" || [[ -z "$GIT_COMMON_DIR" ]]; then
    [[ "$QUIET" -eq 0 ]] && echo "Error: not inside a git repository." >&2
    exit 1
fi
HOOK_DIR="$GIT_COMMON_DIR/hooks"

if ! mkdir -p "$HOOK_DIR" 2>/dev/null; then
    [[ "$QUIET" -eq 0 ]] && echo "Error: failed to create $HOOK_DIR" >&2
    [[ "$QUIET" -eq 1 ]] && exit 0 || exit 1
fi

install_hook() {
    local name="$1"
    local src="$TEMPLATE_DIR/$name"
    local dst="$HOOK_DIR/$name"
    if [[ ! -f "$src" ]]; then
        [[ "$QUIET" -eq 0 ]] && echo "Warning: template $src not found — skipping $name" >&2
        return 0
    fi
    if [[ -f "$dst" && -x "$dst" ]] && cmp -s "$src" "$dst"; then
        [[ "$QUIET" -eq 0 ]] && echo "Up-to-date: $dst"
        return 0
    fi
    if ! cp "$src" "$dst" 2>/dev/null; then
        [[ "$QUIET" -eq 0 ]] && echo "Error: failed to copy $src -> $dst" >&2
        [[ "$QUIET" -eq 1 ]] && return 0 || exit 1
    fi
    if ! chmod u+x "$dst" 2>/dev/null; then
        [[ "$QUIET" -eq 0 ]] && echo "Error: failed to make $dst executable" >&2
        [[ "$QUIET" -eq 1 ]] && return 0 || exit 1
    fi
    [[ "$QUIET" -eq 0 ]] && echo "Installed: $dst"
    return 0
}

install_hook "pre-commit"
install_hook "post-commit"
install_hook "pre-push"

if [[ "$QUIET" -eq 0 ]]; then
    echo ""
    echo "Done. Hooks installed from $TEMPLATE_DIR/ into $HOOK_DIR/"
    echo "  pre-commit  -> format + unit tests (DocsDriftTests fast-path for docs-only)"
    echo "  post-commit -> writes tree-verified marker for pre-push skip optimization"
    echo "  pre-push    -> unit tests (skippable) + mutation smoke + conditional E2E (6 cases)"
    echo ""
    echo "Bypass with: git commit --no-verify / git push --no-verify"
fi
