# Git Hooks

Version-controlled git hooks for local code quality enforcement.

## Available Hooks

### pre-commit
Runs on `git commit` against the **staged snapshot** (unstaged changes are stashed first):
1. Short-circuits full build/test if **every** changed file matches a docs pattern (`*.md`, `docs/`, `CHANGELOG.md`), running only `DocsDriftTests`.
2. Stashes unstaged changes (and untracked files under `src/`) so the checks see exactly what's about to land without disturbing untracked notes outside `src/`.
3. `dotnet format` — runs only on staged `*.cs` files (via `--include`). Fails the commit if those staged files need reformatting.
4. Unit tests — runs with `--no-restore` when NuGet assets are present.

### post-commit
Runs on `git commit` **after** the new commit object is created. Verifies that `HEAD^{tree}` matches the staged tree hash recorded in `pre-commit.pending`, then writes a success marker at `$(git rev-parse --git-path zipper-hooks)/pre-commit.ok` containing the timestamp and the new commit's HEAD hash. The pre-push hook uses this marker to skip unit tests if they just ran.

### pre-push
Runs on `git push`:
1. Ref verification — reads standard input refs from `git push`. Verifies that the outgoing tree matches the checked-out working tree (or aborts) and skips checks for remote deletions.
2. Unit tests — **skipped** if the post-commit marker was recorded within the last 10 minutes on all outgoing commit(s) being pushed.
3. Mutation smoke (`tests/run-mutation-smoke.sh`) — passes `MUTATION_SMOKE_SKIP_BASELINE=1` (since step 2 already verified unit tests), checks the SHA-256 content-hash mutant cache in `$(git rev-parse --git-path zipper-hooks/mutation-cache)`, and runs targeted `*Tests` classes first before falling back to the full test suite.
4. Basic E2E smoke suite (6 representative cases) via `tests/run-e2e-basic.sh` / `.bat` (invoked via `cmd.exe` on Windows) — **skipped** when no CLI source or basic E2E script files changed since `merge-base` for the outgoing refs.

All three hooks support git worktrees (they use `git rev-parse --git-common-dir` / `--git-path` rather than hard-coded `.git/…` paths). Both `pre-commit` and `pre-push` require the `dotnet` SDK and exit non-zero (1) if it is missing.

Full E2E suite + coverage checks run in CI only.

## Installation

Hooks are **automatically installed and kept in sync** on local `dotnet build` via the `SyncGitHooks` MSBuild target in `Directory.Build.props` (skipped when `ContinuousIntegrationBuild=true`). You can also install or sync them manually at any time:

```bash
./setup-hook.sh   # Linux/Mac (or: ./dev.sh hooks)
setup-hook.bat    # Windows   (or: dev.bat hooks)
```

Both scripts copy `.github/hooks/{pre-commit,post-commit,pre-push}` into `$(git rev-parse --git-common-dir)/hooks/`.

## Bypass (not recommended)

```bash
git commit --no-verify
git push --no-verify
```
