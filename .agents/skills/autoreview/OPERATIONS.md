# Operations Log

Curated insights and suppressions for autoreview, plus a small sample of historical run entries. The agent reads the Insights section at review time. Live per-run telemetry is written to the gitignored `OPERATIONS.local.md`, not here.

## Format

Each entry: `### YYYY-MM-DD host:mode:target` (host = claude-code or cursor)

- **findings**: count + summary of what was found
- **outcome**: what happened after (accepted, rejected, partial)
- **lessons**: derived insight — what to do differently next time
- **suppressions**: global rule to add (or none)

## Insights (agent reads these at review time)

> Suppress: "Add error handling for X" when X already has a fallback path 2 lines below the hunk — the fallback is the error handling.
>
> Suppress: Flagging `eval()` in test fixtures — test files are not production code.
>
> Confidence adjustment: Race condition findings in lockfile implementations are almost always P3/INFO — the window is <1ms and operations are idempotent. Cap confidence at 5 unless the operation is non-idempotent.
>
> Variance note: A single review subagent has run-to-run variance — the same diff can yield a different finding count. The parallel specialist set plus the adversarial pass is the mitigation; for high-stakes diffs, re-dispatch the correctness specialist once and union the findings.
>
> Severity: a missing or insufficient test is a **coverage gap → INFO** (appendix if low value), never ACTION. The ACTION belongs to the underlying triggerable bug (owned by correctness/security), not to the absent test. (Observed 2026-06-02: testing specialist rated a missing test ACTION.)
>
> Injected project insights are **context, not findings**: do not emit a finding (or raise severity) merely because an insight names an edge case. Flag it only if the diff actually exercises that edge. Do not let an insight's topic inflate the finding count or confidence on that topic.

### Project facts (zipper) — inject into prompts for this repo; drop if forking the skill elsewhere

> `Author` / `AUTHOR` is a metadata **column name**, not authentication. Do not let it trigger the security specialist (the dispatch signal uses word boundaries for exactly this reason). Same for other domain nouns that embed security-ish substrings.
>
> `src/LoadFiles/DatWriter.cs` (`BuildStandardRow` and its `Append*` helpers) is on the **per-row hot path** — per-row allocations and repeated recomputation matter here. By contrast `DataGenerator.PrecomputeIndices` and `ColumnProfileLoader.Validate` run **once at construction** (single-threaded init); allocations/loops there are not hot-path concerns and there is no race.
>
> Delimiter sentinels: empty `QuoteDelimiter` ⇒ `hasQuote=false` and the resolver returns the fallback quote `þ` (`þ`); empty `ColumnDelimiter` ⇒ fallback ``. `EscapeDatField` doubles the quote char. Known sharp edge: in unquoted mode fields are still escaped against the `þ` sentinel and the **column delimiter is never escaped**, so a field value containing the column delimiter corrupts row structure — flag this, it is a real ACTION-class trigger.
>
> Trust boundary: zipper is a **local single-user CLI**. Operator-supplied args (`--output-path`, counts, profile paths) are **not an untrusted input boundary** — the operator can already do anything those args allow. Findings about operator-supplied paths/values (traversal, "writes outside cwd", absolute paths) are **INFO/hardening, not ACTION**, unless a concrete remote / multi-user / automated-pipeline vector is shown. (Observed 2026-06-02: adversarial rated operator-path findings ACTION while security correctly called them INFO — apply the security calibration. Tracked: zipper#412.)
>
> Extra dispatch signals (project-specific, per SKILL.md "Project-specific signals"): trigger **security** also on `PathValidator` (this repo's path-guard class); trigger **api-contract** also on `EDRM`, `DAT`, `OPT`, `Concordance` (this project's output-format/standard contracts — the emitted load-file format is the API). These are the zipper-specific terms intentionally kept out of the generic SKILL.md dispatch table.

## Log

### 2026-05-31 droid:eval:all-6-scenarios (baseline run)

- **findings**: eval harness 1/6 PASS — ubi-reader path-traversal caught; node-influx failed (droid permissions — missing --auto medium); jinja2-xss missed (genuine recall miss); flask-reuploaded droid permissions; dumbassets bad commit search; changedetection bad hash
- **outcome**: baseline established at 16% recall before fixes
- **telemetry**: skill=2026.05.31 engine=droid os=`Linux 6.17.0` elapsed=~360s
- **lessons**: Droid requires `--auto medium` flag or it fails with "insufficient permission". Eval scenario commit hashes go stale — need exact hash + search fallback. XSS miss on jinja2 suggests REVIEW_ANGLES need explicit template-injection pattern. Python stdout buffering hides progress; add `sys.stdout.reconfigure(line_buffering=True)`.
- **suppressions**: none

### 2026-05-31 droid:eval:run3 (all improvements — 5/6)

- **findings**: eval 5/6 (83%): ubi-reader✓(3/1ACTION), node-influx✓(4/3ACTION), jinja2✗(0 — confirmed genuine miss), flask✓(4/category+keyword), dumbassets✓(5/2ACTION), changedetection✓(2/1ACTION, "escap" keyword hit)
- **outcome**: 83% recall — up from 16% baseline. Only miss: jinja2 one-char regex fix to xmlattr filter (no security-adjacent terms in 12K diff).
- **telemetry**: skill=2026.05.31 engine=droid os=`Linux 6.17.0` bundle=12K-188K elapsed=~25min total
- **lessons**: jinja2 miss — diff is a pure whitelist expansion with no injection-adjacent terms. XSS attr-injection hint added to REVIEW_ANGLES may help next run. changedetection: "escap" keyword hit on "diff_url HTML-escaped" — reasonable proxy for SSTI context. flask: category_match worked without extra_prompt (keyword expansion sufficient).
- **suppressions**: none

### 2026-05-31 droid:commit:zipper/66ad9eb (integration test)

- **findings**: 2 (0 ACTION, 2 INFO) — bug×2
- **outcome**: patch is correct (7/10). Found real root-cause miss: `total = Weights.Sum()` sums all weights including out-of-bounds, making fallback biased; test validates wrong behavior (last-index bias). Both confidence 7. Valid useful findings on a real C# codebase with domain context.
- **telemetry**: skill=2026.05.31 engine=droid os=`Linux 6.17.0` bundle=31110c elapsed=~360s (with recall check)
- **lessons**: Repo context auto-discovery working — UBIQUITOUS_LANGUAGE.md loaded, C#/.NET detected. Recall check correctly triggered (0 ACTION on first pass → reruns). Two-pass review structure surfaced domain-specific analysis of a subtle bias issue. Zipper integration working well.
- **suppressions**: none

### 2026-05-31 droid:eval:run2 (permission + hash fixes)

- **findings**: eval harness 3/6 PASS — ubi-reader✓, node-influx✓ (--auto medium fixed permission error), dumbassets✓ (hash fixed); jinja2 JSON extraction failure (parse_json_candidate `first_brace > 0` bug), flask no security/keyword match, changedetection found FormattableDiff regression but not "template injection" keyword
- **outcome**: 50% (3/6) — improvement from 16% baseline
- **telemetry**: skill=2026.05.31 engine=droid os=`Linux 6.17.0` elapsed=~300s
- **lessons**: parse_json_candidate `if first_brace > 0` should be `>= 0` — when droid returns AI JSON starting with `{` and json.loads fails, brace-walker is skipped, returns None. Also need direct json.loads fast path in extract_json before parse_json_candidate. Flask additive-fix scenarios need extra_prompt context to push engine toward security framing. Eval keywords should be comma-separated OR list, not single word.
- **suppressions**: none

### 2026-05-30 droid:branch:gstack/HEAD~1

- **findings**: 5 (1× security /tmp world-readable, 2× bug null-slug + UTF8 truncation, 2× maintainability lockfile race + sed atomicity)
- **outcome**: patch is correct (8/10)
- **telemetry**: skill=pre-version engine=droid os=`Linux 6.17.0` bundle=186835c elapsed=~150s
- **lessons**: Narrative preamble before JSON breaks helper validation. Added JSON extraction in parse_json_candidate. Recall check added (0 ACTION → rerun once). Schema misaligned with SKILL.md (P0-P3 vs ACTION/INFO) — fixed.
- **suppressions**: none

### 2026-05-30 droid:branch:gstack/HEAD~1 (v2 schema)

- **findings**: 1 (0 ACTION, 1 INFO) — 1×security
- **outcome**: patch is correct (8/10)
- **telemetry**: skill=pre-version engine=droid os=`Linux 6.17.0` bundle=189738c elapsed=~180s
- **lessons**: Review angles in prompt measurably changes output — v2 found the symlink issue which aligns with Pass 1 security patterns. Recall check correctly triggered for 0 ACTION findings.
- **suppressions**: none
