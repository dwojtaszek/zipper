---
name: autoreview
description: "Auto Review closeout. Default is one nori-code-reviewer inside the current coding agent (Claude Code or Cursor) — no external reviewer CLI."
---

# Auto Review

Run a structured code review as a closeout check. This is code review, not Guardian `auto_review` approval routing.

The review runs entirely **inside the current coding agent** (Claude Code or Cursor). Default is **one** `nori-code-reviewer` subagent. There is no external reviewer CLI, no second-model binary, and no code leaves the machine.

Use when:

- user asks for autoreview / code review / second-pass review
- after non-trivial code edits, before final/commit/ship
- reviewing a local branch or PR branch after fixes
- after each task in multi-step development (review early, review often)
- before merge to main
- when stuck on a problem (fresh perspective)
- before refactoring (baseline check)

## Layout

This skill is split so SKILL.md stays a lean orchestrator:

- **`agents/`** — one dispatchable specialist definition per file. Each is a self-contained subagent prompt (role + checklist + output schema). Dispatch a file's contents as the subagent prompt.
  - `correctness.md` (angles A–K + two-pass), `testing.md`, `maintainability.md`, `security.md`, `performance.md`, `data-migration.md`, `api-contract.md`, `design.md`, `adversarial.md`
- **`references/`** — material too large for SKILL.md.
  - `review-policy.md` — confidence calibration, pre-emit gate, fix-first, verification, suppressions, tone, finding merging/fingerprinting, cross-source synthesis, finding format. Every agent references it for scoring; the orchestrator uses it to merge and report.
  - `evals.md` — basic eval scenarios + how to run them.
- **`OPERATIONS.md`** — curated `## Insights` (learned suppressions) read at review time, plus a sample log. Per-run telemetry goes to the gitignored `OPERATIONS.local.md`.

**Portability:** default dispatch is one `nori-code-reviewer`. Specialist files in `agents/` and `references/review-policy.md` stay generic. Repo/domain coupling lives in `OPERATIONS.md` → `## Project facts` (terminology, hot paths, trust boundary, extra opt-in signals). The opt-in table may name this repo's output-format contracts; that does not change the one-nori default. To fork: keep `agents/` + `review-policy.md` + `SKILL.md`, replace `## Project facts`, and re-baseline `references/baseline_runs.md` + `evals.md`.

## Contract

- Treat review output as advisory. Never blindly apply it.
- Verify every finding by reading the real code path and adjacent files.
- Read dependency docs/source/types when the finding depends on external behavior.
- Reject unrealistic edge cases (no concrete trigger path, no plausible input that reaches the code), speculative risks, broad rewrites, and fixes that over-complicate the codebase.
- Prefer small fixes at the right ownership boundary; no refactor unless it clearly improves the bug class.
- Keep going until the review returns no accepted/actionable findings. Maximum 3 fix-review cycles — after 3, escalate remaining findings to the user.
- If a review-triggered fix changes code, rerun focused tests and rerun the review.
- Be patient. One `nori-code-reviewer` run takes **~1–3 minutes** (see `references/baseline_runs.md` for historical multi-specialist timings). Do not kill a reviewer that is still working.
- Review subagents may use read-only inspection tools (read files, grep) to check dependency contracts and current behavior. They do not need write access or web access.
- Security perspective is always included, but it should not cripple legitimate functionality. Report security findings only when the change creates a concrete, actionable risk or removes an important safety check.
- Do not nest reviewers: a review subagent must not dispatch another review or shell out to any external review CLI. Dispatch `nori-code-reviewer` once, collect, synthesize, stop. Do not also spawn `cavecrew-reviewer` or `generalPurpose` copies of correctness/testing/maintainability in the same wave.
- Stop as soon as the review completes with no accepted/actionable findings. Do not run an extra pass just to get a nicer "clean" line, a second opinion, or clearer closeout wording.
- Do not spin up redundant subagents beyond the single default reviewer (see Reviewer Dispatch).
- If rejecting a finding as intentional/not worth fixing, add a brief inline code comment only when it explains a real invariant or ownership decision that future reviewers should know.
- Do not push just to review. Push only when the user requested push/ship/PR update.
- Never skip review because "it's simple." Simple changes harbor simple bugs.

## Effort Levels

Adjust review depth based on diff size and risk. Higher effort catches more real bugs at the cost of more time and more false positives. Effort is passed into each subagent prompt.

- **Low** — 1 diff pass, no full-file reads, ≤4 findings. Flag runtime-correctness bugs visible from the hunk alone. Do not flag style, naming, perf, missing tests, or anything outside the hunk. Use for trivial patches or when time-constrained.
- **Medium** — Angles A-J, up to 6 candidates each, ≤8 findings. Precision-biased: every finding should be one a maintainer would act on.
- **High** (default) — All angles A-K, up to 6 candidates each, recall-biased verify, ≤10 findings. Catch every real bug a careful reviewer would catch in one sitting. Err on the side of surfacing.
- **Maximum** — All angles A-K at full depth, up to 8 candidates each, 1-vote verify, ≤15 findings. Catching real bugs matters more than avoiding false positives. A missed bug ships.

Selection rule: default to High. Upgrade to Maximum for diffs touching auth, crypto, data integrity, or payment flows. nori reports Critical / Important / Minor; apply High's ≤10 cap to that list. Opt-in specialists stay additive only when a second reviewer actually ran.

**Trivial diffs** (docs-only, version-bump, or single-line / mechanical rename with no security/data-safety surface): self-review. Do not spawn a reviewer. A 15-line logic patch is not trivial.

## Reviewer Dispatch

**Default:** one `nori-code-reviewer` (`subagent_type: nori-code-reviewer`, prompt from `.claude/agents/nori-code-reviewer.md`, registry 2.1.1). That is the pre-PR signal.

**Do not dispatch in the same wave:**
- `cavecrew-reviewer` (overlapped nori on the same Important class)
- `generalPurpose` (Cursor) or `Agent` with `.agents/skills/autoreview/agents/{correctness,testing,maintainability}.md` as the prompt (Claude Code)

**Second reviewer (opt-in only):** when the user asks, or after nori returned empty Critical/Important and the diff still touches Load File bytes, CLI flags, or orchestrators. Then pick **one** specialist from the table below by scope signal — not the whole table.

| Agent file | Opt-in when |
|---|---|
| `agents/security.md` | `authenticat`, `authoriz`, `credential`, `password`, `\btoken\b`, `secret`, `crypto`, `permission`, `session`, `login`, `injection`, `traversal`, `sanitiz`, `deserializ`, `\bSSRF\b` |
| `agents/api-contract.md` | Load File / DAT / OPT / Concordance / EDRM writers |
| `agents/adversarial.md` | user asked for a hostile pass |
| `agents/correctness.md` | nori unavailable in this host |
| `agents/testing.md` | user asked for a test-only pass |
| `agents/maintainability.md` | user asked for a maintainability pass |
| `agents/performance.md` | user asked; hot-path loops |
| `agents/data-migration.md` | paths match `migrate/`, `migrations/`, `db/`, or `*.sql` |
| `agents/design.md` | frontend files (`*.css`, `*.scss`, `*.tsx`, `*.vue`, `*.html`) |

**Signal matching:** whole tokens / regex with word boundaries. Bare `auth` matches `Author`. A spurious specialist costs ~30K tokens and ~60s.

**Project-specific signals:** use extra triggers in `OPERATIONS.md` only when choosing the single opt-in second reviewer. Do not dispatch them beside nori.

## Pick Target

Decide what to review, then produce the diff yourself with git. The diff text is the review bundle — you pass it straight into the subagent prompts; there is no bundle file.

**Dirty local work** (use only when the patch is actually unstaged/staged/untracked):

```bash
git diff HEAD                # staged + unstaged tracked changes
git status --porcelain       # surface untracked files; cat any you need to include
```

**Branch / PR work** — diff against the base. If an open PR exists, use its actual base:

```bash
base=$(gh pr view --json baseRefName --jq .baseRefName 2>/dev/null || echo main)
git diff "origin/$base...HEAD"
```

**Committed single change** (already-landed or pushed work on `main`):

```bash
git show <commit>            # or: git diff <commit>^..<commit>
```

For extra review context (notes, evidence), read the relevant files yourself and fold the salient points into the subagent prompts.

## Running the Review (Claude Code + Cursor)

The review runs entirely in the current coding agent. Both Claude Code and Cursor dispatch subagents via the `Agent` tool with `run_in_background=true`. No script, no external engine, no network egress of your code.

**Headless / resumed CLI fallback:** In headless or resumed CLI sessions (such as `codex exec resume`) where subagent spawning fails (e.g., `collab spawn failed` / `invalid thread-store request: no rollout found`), do NOT retry subagent spawns. Run `.claude/agents/nori-code-reviewer.md` inline. Do not run `agents/correctness.md`, `testing.md`, `maintainability.md`, or `adversarial.md` as a substitute wave.

### Steps

1. **Produce the diff** for the chosen target (see Pick Target). Then gather context yourself:
   - **Repo context** — skim `UBIQUITOUS_LANGUAGE.md`, `CLAUDE.md`, `AGENTS.md`, `CONTEXT.md`, `GLOSSARY.md` if present (≤4K each). Note the primary language(s) from `git ls-files` and any domain/glossary terms.
   - **Insights** — read the `## Insights` section of `OPERATIONS.md` for learned suppressions and confidence adjustments.

2. **Dispatch one `nori-code-reviewer`.** Cursor: `Task` with `subagent_type: nori-code-reviewer`. Claude Code: `Agent` with the contents of `.claude/agents/nori-code-reviewer.md`. Append the diff and the Zipper overlay already in that file. Do not add `cavecrew-reviewer` or `generalPurpose` specialists in the same message.

   ```text
   Agent(description="nori PR review", subagent_type="nori-code-reviewer", prompt=<purpose + base SHA + head SHA + git diff>)
   ```

   Appendix appended to the nori prompt:
   ```text
   ## Hard constraints — you are a READ-ONLY reviewer
   You share one working tree with the orchestrator. Anything you write
   corrupts in-flight work, and anything you revert destroys work
   the orchestrator did after your snapshot.

   - Do NOT edit, create, delete, stage, stash, checkout, restore, or revert ANY file.
   - Do NOT run `git add`/`commit`/`checkout`/`restore`/`reset`/`stash`/`clean`/`apply`.
   - Read-only shell (cat, grep, rg, git diff/log/show/status) is fine.
   - To PROVE a test bites, do not apply the mutation. Describe it in the finding:
     "adding `FuzzSuite` to SupportedSuites makes <test> fail on <assertion>".
     Reason about the assertion and cite the line — that is sufficient proof.
   - If you want to execute something, copy the repo to a scratch dir outside
     the working tree first and work only there.
   - The orchestrator diff-verifies the tree after the review. If you touched it,
     say so explicitly in your final line so a revert can be traced.

   ## Effort
   <Low|Medium|High|Maximum — see SKILL.md Effort Levels>

   ## Project insights (suppressions / confidence adjustments)
   <the relevant ## Insights bullets from OPERATIONS.md>

   ## Repo context
   <primary language(s), glossary / domain terms>

   <diff>
   ...the diff text from step 1...
   </diff>
   ```

   **Cache discipline (cost):** assemble the prompt as a stable prefix + volatile suffix — the agent file, effort, insights, and repo context come first (identical across runs of the same repo), and the `<diff>` block comes **last**. Across the fix→re-review cycles (up to 3 per the Contract) keep that prefix **byte-identical** so the host prompt cache hits and only the changed diff is re-billed. Do not reorder or reword the prefix between cycles. See `references/improvements.md` (C1).

3. **Collect responses.** Tag each finding with its source. Pass the structured JSON findings through `validators/line-validator.sh` to deterministically verify that the referenced files and lines exist. Merge and dedup per `references/review-policy.md` (Finding Merging & Fingerprinting).

   **Tree-integrity gate (mandatory, before acting on any finding).** Subagents share the orchestrator's working tree and have been observed writing to it despite read-only dispatch — applying "verification" mutations mid-review and then reverting files the orchestrator had since edited. Never trust a subagent's self-report that it left the tree alone; verify:

   ```bash
   git status --porcelain    # must match the set you expect from step 1
   git diff HEAD --stat      # must match your in-flight patch
   ```

   Compare against the snapshot you took in step 1. If the tree diverged:
   1. Read the actual diff — do not assume contamination is junk. A subagent may have left a *correct* fix (observed: a valid DRY fix survived a botched restore).
   2. Re-apply anything of yours that was reverted; drop any mutation a subagent introduced (`FuzzSuite`, altered constants, reverted lines).
   3. Re-run the focused tests before continuing.
   4. Note it in the final report so the maintainer knows the wave touched the tree.

   Then apply the AUTO-FIX batch in a single pass and verify the resulting diff line by line against the merge table.

4. **Merge + synthesize** nori's findings (and any single opt-in specialist) per `references/review-policy.md`. Emit the **merge table** and **SYNTHESIS** block when more than one reviewer ran.

5. **Report** combined findings and the overall verdict (see Final Report). Use the Finding Format from `references/review-policy.md`.

Specialists read code only — no web search. Wall-clock ≈ the slowest subagent, not the sum.

**Large diffs:** order changed paths so auth/security files come first and vendor/generated files last; if the diff is too large to fit, omit the least-relevant (vendor/generated) hunks first and say so.

### Self-review (trivial diffs)

The required path for trivial diffs (see Reviewer Dispatch). Self-review. Do not spawn `nori-code-reviewer` for a typo or comment-only patch.

## Parallel Closeout

Format first if formatting can change line locations. Then run tests and the review concurrently: launch the focused test command in the background (your Bash tool with `run_in_background=true`) while you dispatch the review subagents. If tests or review lead to code edits, rerun affected tests and rerun the review until no accepted/actionable findings remain. Once a rerun comes back clean, stop.

## Context Efficiency

Dispatch the review subagents once and synthesize their returned findings yourself. If a subagent's output is noisy, summarize it after it returns; do not spawn yet another subagent to rerun the review.

## Final Report

Include: target reviewed and how the diff was produced, effort level applied, specialists dispatched, the **merge table** + **SYNTHESIS** block (per `references/review-policy.md`), tests/proof run, findings accepted/rejected with confidence scores, the clean review result or why a remaining finding was consciously rejected. Format findings per `references/review-policy.md`.

AUTO-FIX-class findings come with a `suggested_fix` (minimal unified diff). After the user's OK, apply the batch of AUTO-FIX patches in one pass; never auto-apply ASK-class (security/behavioral) findings — present those for a decision first (see `references/review-policy.md`, Fix-First).

Do not run another review solely to improve final-report wording. If the review completed with no accepted/actionable findings, report that as clean.

## Self-Improvement

Curated insights and suppressions live in the tracked `OPERATIONS.md`; per-run telemetry goes to the gitignored `OPERATIONS.local.md`. Read `OPERATIONS.md` before review; append a run entry to `OPERATIONS.local.md` yourself after.

### Before review: read insights

Read the `## Insights` section of `OPERATIONS.md`. Apply any suppressions and confidence adjustments listed there, and inject them into the nori prompt (step 2 appendix). These are learned rules from past reviews — real patterns where the skill over-flagged or misclassified.

### After review: log the operation

Append an entry to `OPERATIONS.local.md` under `## Log`:

```text
### YYYY-MM-DD host:mode:target-shorthand

- **findings**: count (breakdown by severity + category)
- **outcome**: accepted / rejected / partial — brief note on what the user did with the findings
- **telemetry**: host=claude-code|cursor mode=MODE specialists=N bundle=SIZEc
- **lessons**: what this run taught us (or "none")
- **suppressions**: global suppression rule to add, or "none"
```

Fill telemetry yourself: `host` = the coding agent you are running in (claude-code or cursor), `mode` = the target mode (local / branch / commit), `specialists` = how many subagents you dispatched, `bundle` = the diff char count.

**When to add a suppression**: you flag something, the user rejects it as a false positive, and the rejection reveals a general pattern (not a one-off). Write the suppression as a one-line rule in the Insights section.

**When to add a confidence adjustment**: a category of finding is consistently over- or under-confident across runs. Write it as a one-line rule.

**When to add a lesson**: the run revealed something about subagent behavior, specialist effectiveness, or prompt interaction that isn't a suppression but is worth remembering.

### Synthesis (periodic, by developer)

`OPERATIONS.local.md` is append-only during reviews. Periodically (not on every run), a developer reads the local log, identifies recurring patterns, and:
- Promotes lessons into SKILL.md rules or the relevant `agents/*.md` checklist
- Promotes suppressions into the Insights section
- Removes stale entries from the log (keep last 30 entries)
- Adjusts specialist dispatch rules or confidence weights based on accumulated data

This is not automated — it's part of skill maintenance, like calibrating an instrument.
