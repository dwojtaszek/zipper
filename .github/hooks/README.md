# Git Hooks

Version-controlled git hooks for local code quality enforcement.

## Available Hooks

### pre-commit
Runs on `git commit` against the **staged snapshot** (unstaged changes are stashed first):
1. Short-circuits if **every** changed file matches a docs pattern (`*.md`, `*.txt`, `docs/`, `CHANGELOG.md`).
2. Stashes unstaged changes so the checks see exactly what's about to land.
3. `dotnet format` — runs only on staged `*.cs` files (via `--include`). Fails the commit if those staged files need reformatting.
4. Unit tests — all ~450 tests (~1 s on a modern machine).

### post-commit
Runs on `git commit` **after** the new commit object is created. Writes a success marker at `$(git rev-parse --git-path zipper-hooks)/pre-commit.ok` containing the timestamp and the new commit's HEAD hash. The pre-push hook uses this marker to skip unit tests if they just ran.

### pre-push
Runs on `git push`:
1. Unit tests — **skipped** if the post-commit marker was recorded within the last 10 minutes on the current HEAD.
2. Basic E2E smoke suite (5 representative cases, ~6 s) via `tests/run-e2e-basic.sh` / `.bat`.

All three hooks support git worktrees (they use `git rev-parse --git-common-dir` / `--git-path` rather than hard-coded `.git/…` paths).

Full E2E suite + coverage checks run in CI only.

## Installation

```bash
./setup-hook.sh   # Linux/Mac
setup-hook.bat    # Windows
```

Both scripts copy `.github/hooks/{pre-commit,post-commit,pre-push}` into `$(git rev-parse --git-common-dir)/hooks/`.

## Bypass (not recommended)

```bash
git commit --no-verify
git push --no-verify
```
