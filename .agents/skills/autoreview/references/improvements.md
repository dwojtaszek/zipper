# Improvement Backlog

Proposed improvements to the autoreview skill, ranked by leverage and tagged with status. Evidence references `references/baseline_runs.md` (2026-06-01 run). Update status as items land.

Status key: **DONE** · **TODO** · **SKIP (for now)**

---

## Cost (review ≈ 200–280K tokens)

### C1 — Prompt-cache the static prefix across fix-review cycles — **DONE (2026-06-01)**
The contract allows up to 3 fix→re-review cycles. Each re-sends the same `agents/<name>.md` + `review-policy.md` + repo context to every subagent; only the diff changes. Keep the static prefix byte-identical and place the volatile diff last so the host prompt cache hits on re-reviews.
- Evidence: ~3.5K static prefix × 5 agents × 3 cycles ≈ 52K redundant tokens/review.
- Effort: M (host-dependent). Impact: ~30–40% on iterative reviews.

### C2 — Diff-slice the domain specialists on large diffs — **SKIP (for now)**
Every subagent gets the full diff (N× duplication). Correctness + adversarial need it; security/design/data-migration only need their triggering hunks. Feed domain specialists their relevant slice.
- Evidence: C diff 6.2K × 5 agents = 31K duplicated diff input; worse at 10K+.
- Effort: M. Impact: 20–40% on large diffs. Trade-off: risks cross-file context loss — keep correctness/adversarial full-diff.

### C3 — Explicit read budget per effort level — **SKIP (for now)**
Agents did 6–16 tool calls (~5–7s each). Effort caps findings, not reads. Add to prompt appendix: Low 0 / Medium 3 / High 6 / Max ∞.
- Evidence: maintainability-C 15 reads/100s, adversarial 16/118s — the latency blowups.
- Effort: S. Impact: caps tail latency + tokens. Trade-off: fewer reads = less cross-file verification.

## Speed (full review ≈ 4–4.5 min)

### S1 — Measure concurrency cap, then priority-order dispatch — **DONE (2026-06-02)**
Measured (8-probe wave, see `references/baseline_runs.md`): **no hard parallel cap through N=8** — the limiter is a serial launch-admission ramp ~1.3s/agent, no stall. A 9-subagent review does not split into waves; it carries a ~12s ramp on top of the slowest agent. Implemented priority-order dispatch in SKILL.md: list longest-pole agents (correctness, adversarial) first so they start at ~T0.
- Effort: S+S. Impact: keeps the ~120s adversarial off the critical path.

### S2 — Triage-then-deepen for large diffs — **TODO**
Cheap fast pass flags risky files → full specialists only there, skip boilerplate/generated.
- Effort: L. Impact: large on mixed PRs. Trade-off: orchestration complexity; triage may miss a sleeper file.

## Accuracy (unmeasured beyond planted-bug evals)

### A1 — Make `evals.md` executable + scored — **DONE (2026-06-01)**
Build a known-bug corpus (zipper commits with documented bugs + clean diffs) + a scored runner measuring precision (FP rate) and recall (catch rate). Run periodically.
- Evidence: all baseline findings were advisory INFO with no ground truth — FP rate unknown.
- Effort: M. Impact: turns tuning from guesswork into measurement.

### A2 — Seed `OPERATIONS.md` Insights with baseline domain facts — **DONE (2026-06-01)**
Learning loop starts near-empty. Pre-load: `Author` is a column name not auth; DatWriter is per-row hot path; `þ`/`þ` is the quote sentinel.
- Effort: S. Impact: fewer FPs + faster agents on zipper.

### A3 — Require a `trigger` field on ACTION findings — **DONE (2026-06-01)**
Contract says reject findings with no concrete trigger path, but nothing enforced it per-finding. Require `trigger` (concrete input/path reaching the bug) on ACTION severity; no trigger → demote to INFO.
- Evidence: C-adversarial's ACTION was strong because it named the trigger.
- Effort: S. Impact: cuts expensive false ACTIONs.

## Quality (output usefulness)

### Q1 — AUTO-FIX findings carry a `suggested_fix` patch — **DONE (2026-06-02)**
Fix-First classifies AUTO-FIX but no agent produces the fix. Add optional `suggested_fix` (unified diff) to AUTO-FIX findings.
- Effort: S. Impact: closes the apply loop.

### Q2 — Enforce the synthesis/merge table in the final report — **DONE (2026-06-01)**
Fingerprint dedup + multi-source confidence boost lives in `review-policy.md` but is executed manually. Formalize as a required output block.
- Evidence: line 646 flagged by 3 specialists — boost only happened because the orchestrator noticed.
- Effort: S. Impact: consistent confirmation, less orchestrator-dependent.

---

## Recommended order (impact ÷ effort)
1. ~~A2 — seed Insights~~ **DONE**
2. ~~A3 — trigger field~~ **DONE**
3. ~~C1 — prompt-cache prefix~~ **DONE**
4. ~~A1 — scored evals~~ **DONE**

### From 2026-06-02 multi-PR breadth run
- **Adversarial local-CLI trust-boundary calibration** — **DONE**: adversarial over-escalated operator-path findings to ACTION (#406); added trust-boundary insight to `OPERATIONS.md` so the security calibration (INFO) is applied. Tracked zipper#412.
- **Broaden dispatch signals** — **DONE**: security now keys on `injection/traversal/sanitiz/deserializ/SSRF/PathValidator`; api-contract on `schema/EDRM/.xsd/openapi/swagger` (output format = contract). They under-fired for this CLI/file tool.
- **G1 — bound the fast pass** — **TODO**: effort:min still did 6 reads / 48s on a config file (#379). Add a hard read cap to the fast-pass instruction.
- Product bugs found & filed: zipper#410 (incomplete InvariantCulture migration), #411 (residual memory leak), #412 (path hardening).

Remaining: **G1** (bound fast-pass, S), **S2** (triage-then-deepen, L), **C2/C3** (deferred). Re-run the eval corpus (incl new S5 no-trigger case) after the next prompt/dispatch change.
