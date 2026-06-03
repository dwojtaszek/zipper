# Review Policy

Shared scoring, classification, and reporting rules. Every specialist agent in `agents/` references this file for confidence scoring; the orchestrator (SKILL.md) uses the merging, synthesis, and format rules when combining results.

## Confidence Calibration

Every finding MUST include a confidence score (1-10):

| Score | Meaning | Display |
|-------|---------|---------|
| 9-10 | Verified by reading specific code. Concrete bug demonstrated. | Show normally |
| 7-8 | High confidence pattern match. Very likely correct. | Show normally |
| 5-6 | Moderate. Could be false positive. | Show with caveat: "Medium confidence — may be a false positive; verify this is actually an issue before fixing" |
| 3-4 | Low confidence. Suspicious but may be fine. | Appendix only |
| 1-2 | Speculation. | Only report if ACTION-severity |

### Pre-emit Verification Gate

Before any finding is promoted:

1. **Quote the specific code line that motivates the finding** — file:line plus the verbatim text. If you cannot quote the motivating line(s), the finding is unverified. Force confidence to 4-5 (appendix only).
2. **Framework-generated symbols:** quote the meta-construct (Meta block, decorator, schema file) instead of expecting the literal name in the class body. For auto-generated code (CRUD scaffolds, ORM models, generated API clients), verify against the generator config or schema, not the generated output — generated code is downstream of its source.
3. **ACTION findings MUST name a concrete trigger** — a specific input, state, or call path that reaches the buggy line and produces the bad outcome. Put it in the finding's `trigger` field. If you cannot name a concrete trigger (only "this looks risky"), the finding is **not** ACTION — demote it to INFO. This enforces the Contract's rejection of speculative findings with no plausible trigger path.

## Verification of Claims

- "This pattern is safe" → cite the specific line proving safety
- "This is handled elsewhere" → read and cite the handling code
- "Tests cover this" → name the test file and method
- Never say "likely handled" or "probably tested" — verify or flag as unknown
- "This looks fine" is not a finding. Either cite evidence or flag as unverified

## Suppressions

Do NOT flag:
- Harmless redundancy that aids readability
- "Add a comment explaining why this threshold was chosen" — thresholds change, comments rot
- "This assertion could be tighter" when it already covers the behavior
- Consistency-only changes with no functional impact
- Regex edge cases when the input is constrained and they never occur
- Harmless no-ops
- ANYTHING already addressed in the diff — read the FULL diff before commenting
- Previously skipped findings where the relevant code hasn't changed. Only suppress `skipped` — never `fixed` or `auto-fixed` (those might regress)

Project-specific learned suppressions live in the `## Insights` section of `OPERATIONS.md` — the orchestrator injects them into each subagent prompt.

## Fix-First Heuristic

Classify each finding:

**AUTO-FIX (agent applies unless the user objects):** Dead code, unused variables, N+1 queries, stale comments contradicting code, magic numbers → named constants, missing LLM output validation, version/path mismatches, inline styles, O(n*m) lookups.

**ASK (needs human judgment):** Security (auth, XSS, injection), race conditions, design decisions, large fixes (>20 lines), enum completeness, removing functionality, anything changing user-visible behavior.

Rule of thumb: mechanical fix a senior engineer would apply without discussion → AUTO-FIX (propose + apply unless objected). Reasonable engineers could disagree → ASK. ACTION findings default toward ASK. INFO findings default toward AUTO-FIX. When an AUTO-FIX item is also an ACTION-severity finding (e.g., missing LLM output validation appears in both Pass 1 and AUTO-FIX), ASK wins — present the fix for approval rather than applying silently.

**AUTO-FIX findings carry a `suggested_fix`.** When a finding is mechanical (AUTO-FIX class), the specialist supplies a minimal **unified diff** in the finding's `suggested_fix` field — the smallest patch that resolves exactly that finding, nothing else. The orchestrator can then apply the batch of AUTO-FIX patches in one pass after the user's OK (still ASK before applying anything ACTION-severity or behavioral). Omit `suggested_fix` for ASK-class findings (security, race conditions, design decisions, large/behavioral changes) — those need a human decision before any patch. A `suggested_fix` must be minimal and self-contained; if the fix would touch multiple files or needs judgment, it is ASK, not AUTO-FIX.

## Review Tone

Be direct, serious, and demanding about quality. Do not soften major maintainability issues into mild suggestions. If the code is making the codebase messier, say so clearly. Do not be satisfied with cosmetic fixes when the real issue is structural.

Good phrases:
- `this pushes the file past 1k lines. can we decompose this first?`
- `this adds another special-case branch into an already busy flow. can we move this behind its own abstraction?`
- `there's a code-judo move here that makes this much simpler. can we reframe this so these branches disappear?`
- `why does this need a cast / optional here? can we make the boundary more explicit instead?`
- `this looks like a bespoke helper for something we already have elsewhere. can we reuse the canonical one?`

## Finding Merging & Fingerprinting

After all specialists and the adversarial pass return, merge findings:

- Compute fingerprint per finding: `{path}:{line}:{category}` or `{path}:{category}`
- Shared fingerprints: keep highest confidence, tag "MULTI-SPECIALIST CONFIRMED (s1 + s2)", boost confidence +1 (cap 10)
- Apply confidence gates: 7+ normal, 5-6 with caveat, 3-4 appendix, 1-2 suppress (unless ACTION-severity)

**The merge is not optional and not implicit — emit this table in the report** (one row per distinct fingerprint, ACTION rows first, then by confidence desc):

```text
| fingerprint (file:line:category) | severity | conf | sources | trigger (ACTION) |
|---|---|---|---|---|
| writer.go:212:DRY-recompute | INFO | 8 (+1) | correctness, performance, maintainability | — |
| encoder.go:88:row-integrity | ACTION | 8 | adversarial | input value containing the field delimiter is written unescaped |
```

`sources` lists every specialist that emitted the fingerprint; `(+1)` marks a confidence boost from multi-source agreement. A row with 2+ sources is `MULTI-SPECIALIST CONFIRMED`. If every finding is single-source, still emit the table (the absence of agreement is itself signal).

Effort-level finding caps (≤4/8/10/15) apply to the correctness specialist only. Other specialists' findings are additive — a specialist finding that passes the confidence gate is included regardless of the correctness cap. Merge and deduplicate before presenting.

## Cross-Source Synthesis

After merging, emit this block (required — it follows the merge table):

```text
SYNTHESIS:
  High confidence (multi-source agreement): [findings]
  Unique to correctness specialist: [findings]
  Unique to adversarial: [findings]
  Unique to domain specialists: [findings]
```

Prioritize multi-source findings for fixes.

## Finding Format (final report)

`[ACTION|INFO] (confidence: N/10) file:line — description`

Examples:
- `[ACTION] (confidence: 9/10) app/models/user.rb:42 — SQL injection via string interpolation in where clause`
- `[INFO] (confidence: 5/10) app/controllers/api/v1/users_controller.rb:18 — Possible N+1 query, verify with production logs`

ACTION findings additionally state their trigger: `[ACTION] (confidence: 9/10) app/models/user.rb:42 — SQL injection via string interpolation (trigger: request param `name` reaches the where clause unescaped)`. An ACTION without a concrete trigger is demoted to INFO.

Multi-specialist confirmed: `[ACTION] (confidence: 10/10, MULTI-SPECIALIST: security + performance) ...`

Single specialist: `[INFO] (confidence: 7/10, specialist: testing) ...`

## Acting on Feedback

- **ACTION findings** → fix before proceeding
- **INFO findings** → note for later, do not block

If a finding is wrong: push back with technical reasoning. Show code/tests. Request clarification. Do not ignore valid technical feedback.
