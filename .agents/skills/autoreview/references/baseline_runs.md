# Baseline Runs

Measured performance/cost baselines for the subagent-based autoreview skill. Use these to detect regressions (speed, token bloat, lost concurrency) and to calibrate the cost model in SKILL.md. Re-run and append a dated section after any change that touches dispatch, agent prompts, or the run loop.

---

## 2026-06-01 — initial subagent-architecture baseline

### Conditions (to reproduce)

- **Skill commit:** `zipper-autoreview@e2fe6e7` (branch `feat/autoreview-skill`), agent files in `agents/`, policy in `references/review-policy.md`.
- **Host / model:** Claude Code, subagents = Opus-class general-purpose agents dispatched via the `Agent` tool with `run_in_background=true`. Platform: Linux.
- **Target repo:** zipper (`/home/dom/Downloads/repos/zipper`), HEAD `86200d2`. C# CLI generating synthetic legal-discovery `.dat`/`.opt` load files.
- **Effort:** High on every subagent.
- **Scenarios:** two real landed commits, reviewed in `--mode commit`:
  - **B** = `66ad9eb` ("weighted distribution bias" fix) — 4 files, 117 ins, diff ≈ 6.4 KB (~1.8K tok).
  - **C** = `d4e7d5e` ("DatWriter empty QuoteDelimiter") — 2 files, 182/-36, diff ≈ 22 KB (~6.2K tok).
- **Dispatch detection (real):** both diffs triggered correctness/testing/maintainability (always-on) + performance (loops/algorithmic). Security signal was a **false positive** — the substring `auth` matched the column name `Author`; no real auth/crypto present, so security was gated out. No migration/api/frontend signals. Per scenario: 4 specialists + 1 adversarial = **5 subagents**.
- **Per-subagent prompt:** "apply `agents/<name>.md` as instructions, read `references/review-policy.md` for scoring, get the diff via `git -C <zipper> show <commit>`, return one JSON object." Specialists launched together in one message (parallel); adversarial run as a separate (here co-launched) agent.
- **Reproduce a wave:** mark `date +%s.%N`, dispatch the N specialists in a single message, record each completion's `duration_ms`/`subagent_tokens`/`tool_uses` from the notifications, mark `date +%s.%N` after the last completion. Wall-clock = end − start.

### Scenario B — `66ad9eb` (5 subagents)

| Agent | Tokens | Tool calls | Duration | Findings |
|---|---|---|---|---|
| correctness | 37,777 | 11 | 73.5s | 2 INFO |
| testing | 32,333 | 8 | 56.4s | 2 INFO |
| maintainability | 28,924 | 6 | 34.9s | 1 INFO |
| performance | 29,178 | 6 | 25.5s | 0 |
| adversarial | 38,994 | 16 | 118.0s | 3 INFO |
| **total** | **167,206** | 47 | — | **8 (0 ACTION)** |

Verdict: fix correct. Adversarial uniquely caught that `Validate()` never runs for built-in profiles (guard bypassed on that path).

### Scenario C — `d4e7d5e` (5 subagents)

| Agent | Tokens | Tool calls | Duration | Findings |
|---|---|---|---|---|
| correctness | 47,326 | 12 | 90.2s | 3 INFO |
| testing | 57,558 | 9 | 61.8s | 4 INFO |
| maintainability | 45,316 | 15 | 100.9s | 2 INFO |
| performance | 39,915 | 9 | 58.4s | 1 INFO |
| adversarial | 50,960 | 16 | 118.9s | 1 ACTION + 4 INFO |
| **total** | **241,075** | 61 | — | **15 (1 ACTION)** |

Verdict: fix correct as scoped. Adversarial escalated a latent INFO into an ACTION: unquoted mode (`hasQuote=false`) never escapes the column delimiter → embedded-delimiter field values corrupt row structure.

### Concurrency (measured)

| Wave | Agents | Wall-clock | Serial sum | Max single |
|---|---|---|---|---|
| B specialists | 4 | **89.5s** | 190.4s | 73.5s |
| B-adv + C specialists | 5 | **147.2s** | 429.3s | 118.0s |

Agents run genuinely parallel up to at least **N=5**: wall-clock ≈ `max(durations) + ~6s·N` orchestration overhead, nowhere near the serial sum. The "wall-clock ≈ slowest subagent" assumption holds. (The per-N overhead is mostly the orchestrator's per-notification turn latency, not agent contention.)

### End-to-end wall-clock (skill run as specified)

```text
B: context 5s + wave 89.5s + adversarial 118s + synthesis ~30s ≈ 242s (~4 min)
C: context 5s + wave 101s  + adversarial 119s + synthesis ~35s ≈ 260s (~4.5 min)
```

A full multi-specialist review is **~4–4.5 min**, not the ~90s an earlier paper model predicted (the model under-counted real cross-file reading; adversarial alone is ~120s).

### Cost model (calibrated to this run)

```text
per-agent:  tokens   ≈ 22K floor + ~1.5K·tool_calls + reasoning(scales with findings)
            duration ≈ ~6s·tool_calls + 10s        (tool_calls 6–16 typical at High)
wave:       wall ≈ max(agent durations) + ~6s·N
end-to-end: context(5s) + wave + adversarial(~110–120s) + synthesis(30–35s)
tokens:     ~200K–280K per 5-subagent review + orchestrator ~35K
```

Key relationships observed:
- **Latency ∝ tool calls, not diff size** (~5–7s per Read/grep/git round-trip). Reading the enclosing code is the time sink — by design (angle A).
- **Tokens scale sublinearly with diff size:** diff grew 3.4× (B→C) but per-agent tokens grew only 25–78%. Diff duplication across N agents only dominates on very large diffs (10K+ tok).
- **Multi-source confirmation works:** `DatWriter.cs:646` flagged independently by correctness + performance + maintainability (+ adversarial found a 5th copy). The þ-doubling latent bug flagged by 2, escalated to ACTION by adversarial.
- **Adversarial earned its slot in both runs** (findings the specialists missed).
- **False dispatch:** `auth` substring-matched `Author` — a wasted 6th agent (~30K tok, ~60s) if applied naively.

### Caveats

- Two data points per role; the model is a fit, not a law. Reasoning tokens vary with finding count, which is input-dependent.
- `subagent_tokens` is total processed tokens (includes the subagent's own system prompt, ~12–15K baseline), not just review I/O.
- Concurrency measured to N=5 only; higher N unverified.
- Orchestration overhead here includes the driving agent's turn latency between async notifications; a tighter orchestrator would shave it.

---

## 2026-06-02 — concurrency-cap probe (S1)

### Conditions

- 8 minimal probe subagents dispatched in **one message**; each ran a single `date +%s.%N` and returned its own start timestamp (no file reads, no review work — isolates scheduling from work). Skill commit `f054e9d`. Dispatch marker `T0 = 1780375171.715`.

### Result — no hard parallel cap through N=8

| Probe | start offset (s) | Δ prev (s) | duration (s) |
|---|---|---|---|
| P1 | +3.36 | — | 3.56 |
| P2 | +4.90 | 1.54 | 3.82 |
| P3 | +5.98 | 1.08 | 3.51 |
| P4 | +7.50 | 1.52 | 4.06 |
| P5 | +8.94 | 1.44 | 3.73 |
| P6 | +10.07 | 1.14 | 3.87 |
| P7 | +11.49 | 1.42 | 3.73 |
| P8 | +12.46 | 0.97 | 3.58 |

All 8 finished by **+16.0s** (serial sum would be ~29.6s; max single 4.06s). Each probe ≈ 19.5K tokens, 1 tool call.

### Interpretation

- Start times **ramp linearly** (~**1.3s/agent**), with **no step or stall** — there is no hard max-parallel ceiling at least through 8. The limiter is a **serial launch-admission rate**, not a concurrency cap.
- Steady-state concurrency for short agents ≈ duration ÷ launch-interval ≈ 3.7 / 1.3 ≈ **~3 running at once** here; but because nothing stalls, longer agents accumulate far more overlap.
- **For real review subagents (25–120s each)** the ~1.3s/agent admission ramp is small relative to runtime, so they overlap nearly fully. A worst-case 9-subagent review does **not** split into serial waves — it just carries a ~12s launch ramp on top of `max(durations)`. This reconciles the earlier "overhead" (N=4 ≈ 16s, N=5 ≈ 29s = launch ramp + orchestrator turn latency).

### Consequence → priority-order dispatch

Because admission is serial (~1.3s/agent), **list the longest-pole agents first** so they start at ~T0 rather than ~T0+10s. Order: `correctness, adversarial, security, …` then the rest. Adversarial is the ~120s long pole; launching it last would add ~10s to end-to-end on a full wave. Implemented in SKILL.md (Specialist Dispatch / Running the Review).

### Caveats

- Probed to N=8; N>8 not tested (ramp showed no inflection, so a ceiling, if any, is >8).
- Launch rate may vary with host load; treat ~1.3s/agent as order-of-magnitude.

---

## 2026-06-02 — re-run of B/C with the improved skill (comparison)

### Conditions

- Skill commit `bdf905f` (post C1/A3/A2/A1/Q2/Q1/S1). Same targets as the 2026-06-01 baseline: **C = `d4e7d5e`**, **B = `66ad9eb`**, effort High, zipper HEAD. Now run per current SKILL.md: priority-order dispatch (correctness, adversarial first), adversarial in-wave, seeded `OPERATIONS.md` project facts injected into each prompt, `trigger`/`suggested_fix` schema fields active. Security correctly **not** dispatched (word-boundary gate; `Author` ≠ auth). 5 subagents per scenario.

### Per-agent (tokens / tool calls / duration)

| Scenario | corr | adversarial | testing | maint | perf | total tok |
|---|---|---|---|---|---|---|
| C (`d4e7d5e`) | 45.4K/8/75.7s | 49.1K/11/97.0s | 47.0K/10/74.9s | 37.8K/5/40.0s | 40.9K/9/71.9s | **220,321** |
| B (`66ad9eb`) | 40.1K/11/95.7s | 39.2K/12/88.8s | 34.9K/10/67.7s | 29.8K/6/46.8s | 30.7K/8/33.1s | **174,704** |

vs prior totals: C 241,075 (**−8.6%**), B 167,206 (+4.5%). C cheaper — seeded insight cut cross-file digging (maint-C 40s/5 tools vs prior 101s/15 tools). Measured wall: C 135.7s, B 118.5s (includes orchestrator turn latency; pure-agent critical path ≈ slowest agent + ramp ≈ ~100s C / ~96s B).

### Feature validation (all fired on real code)

- **A3 `trigger`**: C — all ACTION findings carried a concrete trigger. B — adversarial explicitly **demoted** to INFO ("no concrete trigger reaches a bad outcome today", built-ins balanced). Demotion works both ways.
- **Q1 `suggested_fix`**: real unified diffs emitted for the recompute fix (C: corr/maint/perf) and the `1000` magic-number (B: maint). Non-AUTO-FIX (test gaps, ASK) correctly null.
- **S1 priority dispatch**: correctness + adversarial were the long poles and started first → critical path at ~T0, no wave-splitting at N=5.
- **A2 seeded insights**: performance agents cited "PrecomputeIndices runs once, not hot path" → 0 false hot-path findings.
- **Multi-source richer**: C col-delimiter ACTION now 3-source (was 1); `DatWriter.cs:646` recompute 4-source. B found a NEW finding (fewer-weights → unreachable values) the prior run missed; 0 ACTION (correct-fix, no regression).

### Issues surfaced → fixed

- **Testing over-escalation**: testing-C rated a *missing test* as ACTION (conf 7). A coverage gap is INFO; the ACTION belongs to the underlying triggerable bug (owned by correctness/security). Fixed at source in `agents/testing.md` + a suppression in `OPERATIONS.md`.
- **Seeded-insight amplification**: an insight that names a specific edge can inflate findings/severity on that topic (C's ACTION was partly insight-told, not purely rediscovered). B (no B-specific bug seeded) is the cleaner discovery signal. Mitigated by the missing-test suppression + a note that insights are context, not findings to echo.

---

## 2026-06-02 — multi-PR breadth run (value/cost across 5 merged PRs)

### Conditions

- Skill commit `1178f51`. 6 recent **merged** PRs chosen for topic spread; per-PR dispatch sized by the skill's own rules (fast-pass for trivial, core + topic specialist for substantive). One PR (#389, XML/EDRM) **did not complete** — its 3 agents hit the account session limit; excluded. 10 agents across 5 PRs completed. Diffs fetched by each subagent via `gh pr diff <n>`.

### Cost / speed

| PR | Topic | Dispatch | Tokens | Agents | Slowest |
|---|---|---|---|---|---|
| #387 | deps bump | fast-pass | 28.6K | 1 | 15.7s |
| #379 | CI YAML | fast-pass | 33.6K | 1 | 48.4s |
| #406 | path-traversal (security) | corr+adv+sec | 102.0K | 3 | 77s |
| #393 | InvariantCulture (i18n) | corr+adv | 81.1K | 2 | 100s |
| #390 | buffer overrun+leak (perf) | corr+adv+perf | 100.8K | 3 | 90s |

Total ~346K tok, 10 agents, **avg 34.6K/agent**. Trivial fast-pass ~30K/single; substantive 2–3 agents ~80–102K. 13-wide launch confirmed no concurrency cap (the 3 non-completions were the usage limit, not contention).

### Value (all targets are MERGED, past human review + green CI)

- **#393 — incomplete fix, multi-source + reproduced (top value).** correctness AND adversarial independently found ≥5 live paths still formatting dates with ambient culture (DatWriter, LoadFileWriterBase, LegacyMetadataGenerators, EmailFactory, ProductionManifestWriter). Adversarial reproduced: `DateTime(2020,6,15).ToString("yyyy-MM-dd")` → `1441-10-23` under UmAlQura. Filed **zipper#410**.
- **#390 — leak persists.** PR titled "resolve memory leak"; correctness+perf said clean, **adversarial** found rented buffers still leak on producer WriteAsync fault + undrained channel buffers. Filed **zipper#411**.
- **#406 — 4 real gaps on a security PR whose own test covered 1 vector** (absolute/symlink/Windows-drive/case). Filed **zipper#412**.
- Trivial PRs (#387/#379): fast, cheap, correctly near-clean. Fast-pass right-sizing works (but see calibration #3).

Hit rate: **2 of 3 substantive PRs had a genuine ACTION-class incompleteness the merge missed**; highest-value pattern = "fix looks done but isn't."

### Calibration issues surfaced → fixed

1. **Adversarial over-escalates local-CLI security** (#406): rated operator-path findings ACTION; security correctly rated INFO (local single-user CLI, operator already controls output). → added a trust-boundary insight to `OPERATIONS.md` (apply security's calibration).
2. **Dispatch gaps**: security signals missed `traversal/injection`; api-contract missed file-format/`schema`. Always-on correctness+adversarial compensated on #406, but domain specialists wouldn't auto-fire. → broadened both signal lists in SKILL.md.
3. **fast-pass not tightly bounded** (#379 did 6 reads/48s on config). Minor; left as-is.

### #389 (XML/EDRM) — completed after session reset; validates the dispatch-broadening

3 agents (corr+adv+api-contract), ~149K tok, slowest 200.7s (correctness did 27 tool calls incl a build; api-contract web-fetched the EDRM spec). **Capstone result: only the api-contract specialist caught the headline issue** — the writer emits `<Fields>/<Field>` but EDRM XML 1.2 uses `<Tags>/<Tag TagName= TagValue=>` (ACTION conf 9, verified against edrm.net). Correctness + adversarial both missed it (correctness even judged the structure "likely more schema-correct"); they found internal-consistency items (hash/filesize parity), a UTF-8 BOM-before-`<?xml>` interop hazard (adversarial), an over-broad `*mixed*` gitignore (both), and MD5-hex triplication.

Two lessons confirmed:
- **The topic specialist + dispatch-broadening paid off**: `EDRM`/`schema` now triggers api-contract, which fetched the external spec and found a conformance failure the always-on agents missed. Pre-broadening this PR would have passed as "correct."
- **Nuance**: the code matches the repo's *own* `Requirements.md` example (`<Fields>`), but that example itself conflicts with REQ-052's "conform to EDRM 1.2". A repo self-contradiction, filed as **zipper#413** (verify against the actual XSD before acting).

**Run total across all 6 PRs:** 13 agents, ~495K tokens; surfaced 3 confirmed product bugs (#410/#411/#412) + 1 spec conflict (#413). Hit rate: 3 of 4 substantive PRs had an ACTION-class issue the merge missed.
