# Evals

Scored regression checks for the autoreview skill. The review runs as in-agent subagents (no CLI harness), so evals are **run and scored by the coding agent**, not a script: for each scenario, follow SKILL.md → "Running the Review" against the scenario's diff, compare the returned findings to the ground-truth labels, and record precision/recall in the ledger.

## How to run

1. Materialize the scenario diff (the fenced `diff` block, or the named git target).
2. Dispatch the listed specialist agent(s) per SKILL.md, appending the diff.
3. Match returned findings against the scenario's **ground-truth labels**.
4. Compute the scores (below) and append a dated row to the **Results ledger**.

## Scoring model

Per scenario, classify each ground-truth label and each emitted finding:

- **TP (true positive)** — a labelled bug the review caught at the expected severity (a finding whose category/line matches the label).
- **FN (false negative)** — a labelled bug the review missed.
- **FP (false positive)** — an **ACTION** finding that does not correspond to any label (noise at action severity). INFO findings beyond the labels are not counted as FP — they are advisory; only spurious ACTIONs are penalized.

Then:

```text
recall    = TP / (TP + FN)          # did it catch the known bugs?
precision = TP / (TP + FP)          # were its ACTIONs real?
fp_rate   = FP / scenarios          # spurious ACTIONs per scenario
```

**Pass thresholds (current targets):** recall ≥ 0.90 on ACTION-labelled bugs, precision ≥ 0.80, fp_rate ≤ 0.2. A "clean" scenario passes iff it emits **zero ACTION** findings.

## Functional checks (skill plumbing)

- **F1 — schema validity.** Every agent returns one JSON object with `findings`, `overall_correctness`, `overall_explanation`, `overall_confidence`; each finding has `severity`, `confidence`, `file`, `line`, `category`, `title`, `detail`, (for ACTION) `trigger`, and (for AUTO-FIX-class) `suggested_fix`.
- **F2 — dispatch detection.** Changed paths select the right specialist set (e.g. `*.sql` → data-migration; `authoriz` → security; `Author` alone does NOT → security).
- **F3 — ACTION discipline.** No ACTION finding lacks a `trigger` (per `review-policy.md` it must be demoted to INFO).

## Corpus (ground-truth labelled)

### Synthetic planted bugs

| ID | Agent | Diff | Label (expected ACTION) | trigger present? |
|----|-------|------|------------------------|------------------|
| S1 | correctness | below | auth predicate inverted (auth.py) | yes — non-admin/non-owner user |
| S2 | security | below | command injection via shell=True (runner.py) | yes — `name` with shell metachars |
| S3 | correctness | below | off-by-one `i<=n` OOB write (buffer.c) | yes — any n-length copy |
| S4 | correctness | below | **none** (clean rename) — must emit 0 ACTION | n/a |
| S5 | correctness | below | **no-trigger demotion** — removed guard is dead (value provably non-null), so no ACTION can be emitted (no reachable trigger); INFO at most | must be absent |

#### S1 — inverted condition
```diff
--- a/auth.py
+++ b/auth.py
@@ def is_allowed(user, resource):
-    if user.role == "admin" or user.owns(resource):
-        return True
-    return False
+    if user.role != "admin" and not user.owns(resource):
+        return True
+    return False
```
#### S2 — command injection
```diff
--- a/runner.py
+++ b/runner.py
@@ def run_report(name):
-    subprocess.run(["report", name], check=True)
+    subprocess.run(f"report {name}", shell=True, check=True)
```
#### S3 — off-by-one
```diff
--- a/buffer.c
+++ b/buffer.c
@@ void copy(char *dst, const char *src, size_t n) {
-    for (size_t i = 0; i < n; i++) dst[i] = src[i];
+    for (size_t i = 0; i <= n; i++) dst[i] = src[i];
```
#### S4 — clean rename (must stay clean)
```diff
--- a/util.py
+++ b/util.py
@@
-def fetch_user_record(id):
+def fetch_user(id):   # renamed for clarity; all call sites updated in this diff
     return db.get(id)
```
#### S5 — no-trigger demotion (removed guard is dead; must NOT be ACTION)
```diff
--- a/report.py
+++ b/report.py
@@ def build():
     name = config.get("name", "default")   # .get with default never returns None
-    if name is None:
-        name = "default"
     return render(name)
```
A naive review flags "removed null guard → ACTION." Correct verdict: there is **no input that makes `name` None** (`dict.get` with a default), so no concrete trigger exists — per `review-policy.md` it must be **INFO at most (dead-guard removal), never ACTION**. Tests A3's trigger-demotion rule.

### Live zipper regression cases

These pin real findings the skill produced in the 2026-06-01 baseline. Review the diff from a zipper checkout (`git -C <zipper> show <commit>`).

| ID | Commit | Specialists | Ground-truth | Pass criteria |
|----|--------|-------------|--------------|---------------|
| Z1 | `d4e7d5e` (DatWriter empty QuoteDelimiter) | corr, test, maint, perf, adversarial | ACTION: unquoted mode never escapes the column delimiter → embedded-delimiter field corrupts the row (trigger: field value containing `` when `QuoteDelimiter=""`). INFO: redundant per-row `ResolveStandardDelimiters` recompute (DatWriter.cs:646). | adversarial emits the column-delimiter ACTION **with trigger**; ≥1 specialist flags the line-646 recompute |
| Z2 | `66ad9eb` (weighted-distribution fix) | corr, test, maint, perf, adversarial | **No ACTION** (the diff is a correct fix). INFO acceptable: built-in profiles skip `Validate()`. | 0 ACTION findings; fix judged correct |

> To extend Z-cases with **bug-present** corpus entries, find a commit that *introduced* a bug, check out its parent, and review the bug-introducing diff as if it were a new PR — label the known bug as the expected ACTION. (This is how to grow recall coverage with real defects rather than synthetic ones.)

## Results ledger

Append one row per eval run. Date · scenario · TP/FN/FP · recall/precision · notes.

| Date | Scenario | TP | FN | FP | Recall | Precision | Notes |
|------|----------|----|----|----|--------|-----------|-------|
| 2026-06-01 | S1 | 1 | 0 | 0 | 1.00 | 1.00 | inverted auth caught, conf 10, De Morgan analysis correct |
| 2026-06-01 | S2 | 1 | 0 | 0 | 1.00 | 1.00 | shell=True injection caught, conf 9 |
| 2026-06-01 | S3 | 1 | 0 | 0 | 1.00 | 1.00 | off-by-one caught, conf 10 |
| 2026-06-01 | S4 | — | — | 0 | — | — | clean: 0 ACTION (1 INFO on unverifiable rename claim — not penalized) |
| 2026-06-01 | Z1 | 1 | 0 | 0 | 1.00 | 1.00 | adversarial emitted column-delimiter ACTION w/ trigger; line-646 recompute flagged by 3 specialists |
| 2026-06-01 | Z2 | — | — | 0 | — | — | clean fix: 0 ACTION; built-in-profile Validate-skip noted as INFO |

Aggregate 2026-06-01: recall 4/4 = 1.00 on ACTION-labelled bugs, precision 4/4 = 1.00, fp_rate 0/6 = 0. All functional checks (F1–F3) passed. **Not yet run:** S5 (no-trigger demotion — added to validate A3's `trigger` rule). Run it on the next eval pass and append the result; pass = 0 ACTION emitted.

## Maintenance

When a review misses a class of bug in production, add a labelled scenario here that reproduces it, then tighten the relevant `agents/*.md` checklist until recall passes. Keep scenarios small and self-contained. Re-run the corpus after any change to dispatch, agent prompts, or `review-policy.md`, and append a fresh dated ledger block.
