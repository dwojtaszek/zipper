# Review-Wait Gate — Design

**Date:** 2026-06-10
**Status:** Approved
**Motivation:** PR #479 was merged on green CI before robot reviewers (Gemini Code Assist, CodeRabbit, Codex) had been read. Gemini findings only surfaced post-merge. AGENTS.md step 11 already mandated the review sweep, but nothing made the wait mechanical or the merge conditional.

## Goals

1. Make "wait for robot reviews, then address them" a single mechanical command an agent cannot misinterpret.
2. Make merging with unaddressed review threads impossible server-side, independent of agent discipline.

## Non-goals

- Blocking PRs until every bot reviews (bots rate-limit and outage; timeout warns instead of failing).
- Required human approvals or other branch-protection tightening.

## Components

### 1. `tests/wait-for-reviews.sh <PR#> [timeout-minutes]`

- Expected reviewers (array at top of script): `gemini-code-assist[bot]`, `coderabbitai[bot]`, `chatgpt-codex-connector[bot]`.
- Polls every 30 s, default timeout 20 minutes. A bot is **accounted for** when:
  - it has posted a review (`GET pulls/<N>/reviews`), or
  - it has posted an issue comment matching a skip pattern (`usage limit | rate limit | review limit | Review skipped | Walkthrough` — the last covers CodeRabbit's walkthrough-only responses, which carry no formal review object), or
  - the timeout expires (prints a warning; does not fail the gate).
- After all bots are accounted for, queries GraphQL `reviewThreads` for resolution state and prints every unresolved inline thread (author, path:line, first lines of body) plus non-empty review summary bodies.
- **Exit codes:** `1` if any unresolved review thread exists (agent must fix or reply-and-resolve, then re-run); `0` when clean. Transient API failures are retried, not fatal.

### 2. Branch protection on `main`

- `required_conversation_resolution: true` — GitHub refuses the merge while any review thread is unresolved.
- No other settings enabled (no required reviewers, no admin enforcement). Side effect: force-pushes to `main` are blocked.
- Applied once via API; the exact command is documented in CI.md for reproducibility.

### 3. Documentation

- AGENTS.md step 11 rewritten to mandate the script after PR creation and after every push, with the manual three-endpoint queries kept as fallback. Step 12 notes the server-side block.
- CI.md gains a "Robot reviews" section: script usage, skip-pattern caveat, protection setup command.

## Testing

- Dry-run against merged PRs with known states: #479 (had findings; threads now replied) and #482 (clean reviews, Codex skipped).
- Shellcheck-clean like the other `tests/*.sh` scripts.

## Accepted gaps

- A bot that never posts anything and never declares a skip → script warns at timeout and proceeds; branch protection still blocks if its threads appear later.
- Thread "resolved" state is GitHub's, not semantic — an agent could resolve without addressing. Mitigated by AGENTS.md requiring a reply (fix or reason) before resolving.
