# Git Hooks

Version-controlled git hooks for local code quality enforcement.

## Available Hooks

### pre-commit
Runs on `git commit` against the **staged snapshot** (unstaged changes are stashed first):
1. Short-circuits if the commit only touches `*.md` / `*.txt` / `docs/` files.
2. `dotnet format` — auto-fixes style; fails the commit if changes were needed.
3. Unit tests — all ~450 tests (~1 s on a modern machine).
4. On success, records a timestamp marker in `.git/zipper-hooks/pre-commit.ok`.

### pre-push
Runs on `git push`:
1. Unit tests — **skipped** if `pre-commit.ok` was recorded within the last 10 minutes on the same HEAD.
2. Basic E2E smoke suite (5 representative cases, ~6 s) via `tests/run-e2e-basic.sh` / `.bat`.

A PowerShell variant (`pre-push.ps1`) is installed for Windows developers without Git Bash.

Full E2E suite + coverage checks run in CI only.

## Installation

```bash
./setup-hook.sh   # Linux/Mac
setup-hook.bat    # Windows
```

Both scripts copy `.github/hooks/{pre-commit,pre-push,pre-push.ps1}` into `.git/hooks/`.

## Bypass (not recommended)

```bash
git commit --no-verify
git push --no-verify
```
