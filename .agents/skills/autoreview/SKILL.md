---
name: autoreview
description: "Auto Review closeout. Runs as parallel review subagents inside the current coding agent (Claude Code or Cursor) — no external reviewer CLI."
---

# Auto Review

Run a structured code review as a closeout check. This is code review, not Guardian `auto_review` approval routing.

The review runs entirely **inside the current coding agent** (Claude Code or Cursor) as parallel review subagents dispatched via the native `Agent` tool. There is no external reviewer CLI, no second-model binary, and no code leaves the machine.

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

**Portability:** `agents/`, `references/review-policy.md`, and this file are **generic** (web-app-flavored examples, but the methodology is language/stack-neutral). All repo/domain coupling lives in `OPERATIONS.md` → `## Project facts` (terminology, hot paths, trust boundary, extra dispatch signals). To fork to another repo: keep `agents/` + `review-policy.md` + `SKILL.md` as-is, replace the `## Project facts` block, and re-baseline `references/baseline_runs.md` + `evals.md` (the Z-scenarios). Specialists with no matching signal (e.g. `data-migration` in a repo with no DB, `design` with no frontend) simply never dispatch — that is expected, not a gap.

## Contract

- Treat review output as advisory. Never blindly apply it.
- Verify every finding by reading the real code path and adjacent files.
- Read dependency docs/source/types when the finding depends on external behavior.
- Reject unrealistic edge cases (no concrete trigger path, no plausible input that reaches the code), speculative risks, broad rewrites, and fixes that over-complicate the codebase.
- Prefer small fixes at the right ownership boundary; no refactor unless it clearly improves the bug class.
- Keep going until the review returns no accepted/actionable findings. Maximum 3 fix-review cycles — after 3, escalate remaining findings to the user.
- If a review-triggered fix changes code, rerun focused tests and rerun the review.
- Be patient. A full multi-specialist review takes **~3–5 minutes** end-to-end (measured — see `references/baseline_runs.md`); a single subagent runs ~25–120s depending on how much surrounding code it reads. Wall-clock ≈ the slowest subagent, not the sum (the wave is parallel). Do not kill a review subagent that is still working.
- Review subagents may use read-only inspection tools (read files, grep) to check dependency contracts and current behavior. They do not need write access or web access.
- Security perspective is always included, but it should not cripple legitimate functionality. Report security findings only when the change creates a concrete, actionable risk or removes an important safety check.
- Do not nest reviewers: a review subagent must not dispatch another review or shell out to any external review CLI. Dispatch the detected specialists once, collect, synthesize, stop.
- Stop as soon as the review completes with no accepted/actionable findings. Do not run an extra pass just to get a nicer "clean" line, a second opinion, or clearer closeout wording.
- Do not spin up redundant subagents beyond the set detected by scope signals (see Specialist Dispatch).
- If rejecting a finding as intentional/not worth fixing, add a brief inline code comment only when it explains a real invariant or ownership decision that future reviewers should know.
- Do not push just to review. Push only when the user requested push/ship/PR update.
- Never skip review because "it's simple." Simple changes harbor simple bugs.

## Effort Levels

Adjust review depth based on diff size and risk. Higher effort catches more real bugs at the cost of more time and more false positives. Effort is passed into each subagent prompt.

- **Low** — 1 diff pass, no full-file reads, ≤4 findings. Flag runtime-correctness bugs visible from the hunk alone. Do not flag style, naming, perf, missing tests, or anything outside the hunk. Use for trivial patches or when time-constrained.
- **Medium** — Angles A-J, up to 6 candidates each, ≤8 findings. Precision-biased: every finding should be one a maintainer would act on.
- **High** (default) — All angles A-K, up to 6 candidates each, recall-biased verify, ≤10 findings. Catch every real bug a careful reviewer would catch in one sitting. Err on the side of surfacing.
- **Maximum** — All angles A-K at full depth, up to 8 candidates each, 1-vote verify, ≤15 findings. Catching real bugs matters more than avoiding false positives. A missed bug ships.

Selection rule: default to High. Upgrade to Maximum for diffs touching auth, crypto, data integrity, or payment flows. Caps apply to the correctness specialist; other specialists are additive (see `references/review-policy.md`).

**Use the fast pass (not the full wave) for trivial diffs** — single-file patches under ~20 lines with no security/data-safety surface (typo fixes, comment/doc edits, mechanical renames, version bumps). This is not merely "allowed," it is the expected path: a full multi-specialist review costs ~200K tokens and ~4 minutes; the fast pass costs ~30K tokens and ~30s (see `references/baseline_runs.md`). Spending the full budget on a one-line change is waste. Escalate to the full wave only if the fast pass surfaces something non-trivial.

## Specialist Dispatch

The review is performed by the specialist agents in `agents/`, dispatched in parallel. Always dispatch correctness, testing, and maintainability; add the rest by scope signal. Each subagent has fresh context — no prior review bias.

| Agent file | Dispatch when (changed-diff signal) |
|---|---|
| `agents/correctness.md` | always (the main review — angles A–K + two-pass) |
| `agents/testing.md` | always |
| `agents/maintainability.md` | always |
| `agents/security.md` | `authenticat`, `authoriz`, `credential`, `password`, `\btoken\b`, `secret`, `crypto`, `permission`, `session`, `login`, `injection`, `traversal`, `sanitiz`, `deserializ`, `\bSSRF\b` |
| `agents/performance.md` | `\bfor\b`, `foreach`, `\bwhile\b`, `\.Select`, `\.Where`, `\bquery\b`, `fetch`, `\bmap\b` |
| `agents/data-migration.md` | paths match `migrate/`, `migrations/`, `db/`, or `*.sql` |
| `agents/api-contract.md` | `\broute\b`, `endpoint`, `controller`, `handler`, `serializer`, `schema`, `\.xsd`, `openapi`, `swagger` (for a generator/exporter the **emitted file format is the contract** — output-format writers count) |
| `agents/design.md` | frontend files (`*.css`, `*.scss`, `*.tsx`, `*.vue`, `*.html`) |
| `agents/adversarial.md` | always, in the same parallel wave (see Running the Review) |

**Signal matching:** match these as whole tokens / regex with word boundaries, not bare substrings. Bare `auth` substring-matches ordinary words like `Author` (a real false positive measured in `references/baseline_runs.md`) — use `authenticat`/`authoriz` instead. A spurious specialist costs ~30K tokens and ~60s.

**Project-specific signals:** also honor any extra dispatch triggers listed under `## Project facts` in `OPERATIONS.md` — that is where repo/domain-specific terms live (e.g. a project's path-guard class, or its output-format/standard names), keeping this table generic and portable.

**Adaptive gating:** If a specialist has produced 0 findings across 10+ dispatches, it may be auto-gated. Security and data-migration are insurance — always dispatch regardless of hit rate. Specialists are additive — partial results beat no results.

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

### Steps

1. **Produce the diff** for the chosen target (see Pick Target). Then gather context yourself:
   - **Repo context** — skim `UBIQUITOUS_LANGUAGE.md`, `CLAUDE.md`, `AGENTS.md`, `CONTEXT.md`, `GLOSSARY.md` if present (≤4K each). Note the primary language(s) from `git ls-files` and any domain/glossary terms.
   - **Insights** — read the `## Insights` section of `OPERATIONS.md` for learned suppressions and confidence adjustments.

2. **Detect specialists** from the changed paths using the Specialist Dispatch table.

3. **Dispatch all detected specialists AND the adversarial reviewer in parallel, in a single message.** The adversarial pass depends only on the diff (not on specialist output), so it runs in the same wave — not after. For each, the subagent prompt is the contents of its `agents/<name>.md` file, with the diff and context appended:

   **Order the calls longest-pole first** — `Agent` admission is serial (~1.3s/agent, measured in `references/baseline_runs.md`), so whatever you list first starts first. Put the two slowest (correctness, adversarial) at the top, then security, then the rest:

   ```text
   Agent(description="correctness specialist", run_in_background=True, prompt=<agents/correctness.md + appendix>)
   Agent(description="adversarial reviewer",   run_in_background=True, prompt=<agents/adversarial.md + appendix>)
   Agent(description="security specialist",    run_in_background=True, prompt=<agents/security.md + appendix>)
   Agent(description="testing specialist",     run_in_background=True, prompt=<agents/testing.md + appendix>)
   # + maintainability + any others detected in step 2
   ```

   There is no hard parallel cap (tested to 8 — no stall); the only cost is the ~1.3s/agent admission ramp, so a 9-subagent review does not split into serial waves — it carries a ~12s ramp on top of the slowest agent. Adversarial is the ~120s long pole and depends only on the diff, so launching it **second** (not last, not after the wave) keeps it off the critical path.

   Appendix appended to each agent file:
   ```text
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

4. **Collect responses.** Tag each finding with its specialist. Merge and dedup per `references/review-policy.md` (Finding Merging & Fingerprinting).

5. **Merge + synthesize** once all subagents (specialists + adversarial) return. Emit the required **merge table** (fingerprint dedup + multi-source confidence boost) and the **SYNTHESIS** block, both per `references/review-policy.md`. These are mandatory output, not optional — multi-source agreement must be made visible, not left implicit.

6. **Report** combined findings and the overall verdict (see Final Report). Use the Finding Format from `references/review-policy.md`.

Specialists read code only — no web search. Wall-clock ≈ the slowest subagent, not the sum.

**Large diffs:** order changed paths so auth/security files come first and vendor/generated files last; if the diff is too large to fit, omit the least-relevant (vendor/generated) hunks first and say so.

### Fast pass (effort: min)

The required path for trivial diffs (see Effort Levels selection rule) and an option for large diffs where specialist depth isn't needed. Dispatch a **single** correctness subagent — `agents/correctness.md` restricted to angles A–E, no tools, ≤4 findings — instead of the full parallel set. ~30K tokens, ~30s versus ~200K / ~4 min for the full wave. Escalate to the full wave only if the fast pass surfaces a non-trivial finding.

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

Read the `## Insights` section of `OPERATIONS.md`. Apply any suppressions and confidence adjustments listed there, and inject them into each subagent prompt (step 3 appendix). These are learned rules from past reviews — real patterns where the skill over-flagged or misclassified.

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
