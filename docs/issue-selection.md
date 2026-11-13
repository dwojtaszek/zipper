# GitHub issue selection & wave triage

Disclosed from [issue-workflow.md](issue-workflow.md). Read when triaging, labeling, or selecting the next GitHub issue.

## Issue selection (mandatory for autonomous / "next issue" work)

Agents must **not** pick or classify issues by gut feel, recency, or title interest. Follow this algorithm every time when selecting the next issue to implement **or** when classifying/labeling issues in the backlog.

### 1. Load the living roadmap (if any)

```bash
gh issue list --state open --limit 200 --json number,title,labels \
  --jq '.[] | select(.title | test("^Roadmap:"; "i")) | "#\(.number) \(.title)"'
```

- If a **Roadmap:** issue is open:
  - Read it **and all comments** (`gh issue view NNN --comments`).
  - Treat its **phase order and exit criteria** as the sequencing contract for leaf issues it lists.
  - **Never implement the roadmap issue itself** (it is a tracker only).
- If none is open, continue with label ranking and wave classification below.

### 2. Rank by priority labels (`P0`–`P3`) and `.zipper-runner` execution tiers

Every work issue should carry exactly one priority label (**`P0`**, **`P1`**, **`P2`**, **`P3`**) plus type/domain labels that place it in the correct autonomous execution tier.

| Label | Meaning | When to take |
|-------|---------|--------------|
| `P0` | Blocker / production broken / data loss | Immediately; only open work until cleared |
| `P1` | High — must fix soon (security, correctness, CI/hook/coverage gates) | After all open `P0` |
| `P2` | Medium — this iteration (tests, fixtures, refactors, product features, oracles) | After all open `P0`/`P1` |
| `P3` | Low — nice to have (docs, traceability, non-urgent cleanup) | Only when no open `P0`–`P2`, or user names the issue |

**How `.zipper-runner` (`select_next_issue()`) orders the queue:**
Unassigned open issues from `TRUSTED_AUTHORS` (`dwojtaszek`) without an active PR/branch/worktree or prompt-injection marker are bucketed into **six first-match label tiers** (case-insensitive) and sorted by **ascending issue number** within each tier:

| Runner Tier | Matching Labels (first match wins) | Branch Prefix | Intended Work Category |
|-------------|------------------------------------|---------------|------------------------|
| **Tier 1** (`p1_issues`) | `P0`, `P1`, `critical`, `high`, `blocker`, `bug` | `fix/ISSUE-...` (when `bug`, `P1`, or `critical`) | Security, correctness bugs, hot-path regressions, CI/CD & git-hook reliability, coverage/mutation gates |
| **Tier 2** (`p2_issues`) | `testing`, `e2e` | `feat/ISSUE-...` | Unit/property/E2E tests, Archive Test Fixture mutations, differential reader matrices, load-file Chaos Engine cases |
| **Tier 3** (`p3_issues`) | `refactor` | `feat/ISSUE-...` | Structural decompositions and seam extractions (with byte-identical output parity) |
| **Tier 4** (`p4_issues`) | `enhancement` | `feat/ISSUE-...` | Cross-format validators, opt-in artifact realism/fidelity features, and artifact-level Chaos Engine corruptions |
| **Tier 5** (`p5_issues`) | `investigation`, `quality` | `feat/ISSUE-...` | Capstone expectation oracles, audit sidecar verification, and repository-wide quality sweeps |
| **Tier 6** (`p6_issues`) | *(all other open issues)* | `feat/ISSUE-...` | Documentation sync, ADR/architecture diagrams, requirement traceability (`documentation`, `docs`, `requirements`, `P3`) |

> **`P0` precedence note:** Manual/interactive agent selection **must** always take `P0` before any `P1` work (`sort_by` below puts `P0` first). Because `.zipper-runner` pools `P0` and `P1`/`bug`/`critical`/`high`/`blocker` together into Tier 1 and sorts Tier 1 by issue number ascending, if a `P0` ticket is filed with a higher issue number than open `P1`/`bug` tickets, triage must temporarily downgrade or assign the lower-numbered `P1`/`bug` tickets until the `P0` blocker is cleared so the runner picks `P0` first.

```bash
# List unassigned trusted-author leaf issues in .zipper-runner tier order (with P0 pinned first; for manual priority order sort by p before tier).
# Note: .zipper-runner additionally skips issues with an open PR, active remote/worktree branch (issue-NNN or slug), or state/issue-NNN-injection.json marker.
gh issue list --state open --limit 500 --json number,title,labels,assignees,author \
  --jq 'def has_any($arr): ([.labels[].name | ascii_downcase] | any(. as $l | $arr | index($l)));
    def tier: (if has_any(["p1","p0","critical","high","blocker","bug"]) then 1
      elif has_any(["testing","e2e"]) then 2
      elif has_any(["refactor"]) then 3
      elif has_any(["enhancement"]) then 4
      elif has_any(["investigation","quality"]) then 5
      else 6 end);
    def p: (if any(.labels[].name; .=="P0") then 0 elif any(.labels[].name; .=="P1") then 1 elif any(.labels[].name; .=="P2") then 2 elif any(.labels[].name; .=="P3") then 3 else 9 end);
    map(select((.assignees | length) == 0 and (.author.login == "dwojtaszek") and (.title | test("^Roadmap:"; "i") | not)))
    | sort_by((if p == 0 then 0 else tier end), .number)[]
    | "T\(tier) P\(p) #\(.number) [\([.labels[].name] | join(","))] \(.title)"'
```

Issues **without** a `P0`–`P3` label rank last when sorting manually (priority 9). Prefer labeling them before implementing. If an issue carries multiple `Pn` labels, the highest priority (lowest number) wins.

### 3. Backlog classification & wave prioritization rules (mandatory when triaging or labeling)

Because `.zipper-runner` uses **first-match tier bucketing** and sorts within each tier strictly by **ascending issue number**:
- **Cross-wave rule (avoid label bleed):** Adding a higher-tier secondary label (for example, adding `testing` to an `enhancement` or `quality` ticket, or `quality` to a `documentation` ticket) pulls that issue into an earlier execution wave ahead of its prerequisites.
- **Intra-wave prerequisite rule:** Keeping a prerequisite and its dependent in the same tier works automatically only when the prerequisite has a **lower issue number** (`#prereq < #dependent`). If a prerequisite was filed *after* its dependent (`#prereq > #dependent`), same-tier labels cannot order them for `.zipper-runner`: you must either **promote the prerequisite to an earlier tier** or **defer the dependent to a later tier** until the prerequisite merges (in addition to recording `blocked by #N` in the issue body/comments for manual selection).

When classifying or re-labeling issues, enforce this **5-wave dependency order**:

1. **Wave 1 — CI/CD, Git Hooks, Security, Perf & Coverage Gates (Tier 1: `P0`/`P1`, `bug`, `ci-cd`, `security`, `perf`)**
   - Includes silent-correctness bugs, security hardening, hot-path regressions, local hook reliability (`pre-commit` / `pre-push`), CI workflow fixes, and test/coverage/mutation gate enforcement.
   - **Intra-wave ordering:** CI/test-runner fixes that repair broken checks or unblock accurate coverage/mutation measurement must run **before** tickets that ratchet coverage or mutation thresholds upward. If the threshold-ratchet ticket has a lower issue number than the CI/measurement fix, defer the ratchet ticket to Tier 2/Tier 5 until the Tier 1 fix merges.
2. **Wave 2 — Test Coverage, Archive Test Fixtures, Differential Matrices & Load-File Chaos (Tier 2: `P2` + `testing` / `e2e`)**
   - Includes missing unit/property/E2E tests, individual Archive Test Fixture mutations, cross-reader differential test matrices, and Chaos Engine anomalies injected through the existing load-file pipeline.
   - **Intra-wave ordering:** Individual fixture and mutation cases must run **before** aggregate differential-matrix or suite-wide verification tickets that test across the full fixture catalog. When the aggregate matrix ticket has a higher issue number than the individual fixture tickets, keep both in Tier 2 (`P2, testing`, omitting `enhancement` so it does not drop to Tier 4, and omitting `P1`/`bug` so it does not jump to Tier 1); if an individual fixture ticket has a higher issue number than the aggregate matrix ticket, defer the aggregate matrix ticket to a later tier (e.g., Tier 4 `enhancement` or Tier 5 `quality`) until the fixture lands.
3. **Wave 3 — Structural Refactors (Tier 3: `P2` + `refactor`, omit `testing` and `bug`)**
   - Includes module decompositions and internal seam extractions guarded by Wave 2 tests and Critical Rule 6 golden parity.
   - **Label rule:** Do **not** attach `testing` to a refactor ticket, or it will be promoted into Tier 2 ahead of the tests meant to guard the refactor.
4. **Wave 4 — Cross-Format Validators, Opt-In Artifact Realism & Artifact Chaos (Tier 4: `P2` + `enhancement`, omit `testing` and `bug`)**
   - Includes new CLI/product capabilities, cross-format post-generation validators, opt-in format realism upgrades (e.g., realistic container structure, headers, metadata, attachments), and artifact-level Chaos Engine corruptions that mutate those realistic structures.
   - **Intra-wave ordering:** Prerequisite capabilities and realistic artifact generators must run **before** the validators, load-file columns, or chaos corruptions that depend on them (e.g., base validator/generator structure → richer metadata/headers → opt-in chaos corruption of that structure).
   - **Label rule:** Do **not** attach `testing` to artifact-chaos or validator-enhancement tickets that depend on another `enhancement` ticket. When prerequisites have lower issue numbers than their dependents, keep the chain in Tier 4 (`P2, enhancement`, using `chaos` or `quality` as secondary tags); if a dependent ticket has a lower issue number than its prerequisite, defer the dependent to Tier 5 (`quality`, omitting `enhancement`) until the prerequisite merges.
5. **Wave 5 — Capstone Oracles & Documentation/Traceability Sweep (Tiers 5 & 6)**
   - **Tier 5 (`P2` + `quality` / `investigation`, omit `testing` and `enhancement`):** End-to-end expectation oracles, sidecar/audit anomaly verifiers, and repository-wide quality sweeps that must observe the complete catalog of Wave 2 and Wave 4 features/anomalies.
   - **Tier 6 (`P3` + `documentation` / `docs` / `requirements`, omit `quality`, `testing`, and `enhancement`):** Documentation sync, ADR/architecture diagram reconciliation, `tests/req-traceability.tsv` updates, and contributor/CI guides that should land last after code behavior has settled.

**Manual tie-break within the same `Pn` (when selecting manually outside `.zipper-runner`):**
`1: security` → `2: bug` → `3: ci-cd` → `4: testing, e2e` → `5: performance, perf` → `6: refactor, architecture, quality` → `7: enhancement, design` → `8: documentation, docs, requirements` → `9: other`, then **issue number ascending**.

### 4. Honor blockers and roadmap phase order

Before selecting an issue, load body + comments:

```bash
gh issue view NNN --json title,body,labels,comments
```

Check for blocking relationships via the GitHub API:

```bash
gh api graphql -f query='query { repository(owner: "OWNER", name: "REPO") { issue(number: NNN) { trackedIssues(first: 10) { nodes { number state title } } } } }'
```

Or search body/comments for `blocked by #N`, `depends on #N`, `prerequisite: #N`. Verify each referenced `#N` is **closed** before proceeding.

For roadmap phase ordering: read the Roadmap issue's current phase section, identify which phase lists the candidate issue, and confirm all earlier phases have no open leaf issues (or their exit criteria are met).

**Skip** (try next candidate) if any of:
- Body or comments contain `blocked by #N`, `depends on #N`, `prerequisite: #N` (or GitHub "blocked by" relationship) and that `#N` is still **open**.
- The open **Roadmap:** issue's current phase says this issue is **later** (e.g. Phase 4 product while Phase 0/1 leaves are still open).
- Title is a **Roadmap:** tracker.
- Issue is clearly **stale** — comment with evidence and stop; do not implement.

**Special sequencing (when a roadmap lists phases):** prefer explicit phase lists on the Roadmap issue over pure number order (`security/bug/ci-cd` → `testing/e2e` → `refactor` → `enhancement` → `quality/investigation` → `documentation/requirements`).

### 5. Select exactly one leaf issue & honor user override

- Announce: issue number, Pn label, type labels, and one-line why it ranked first.
- One issue per branch/PR (except when the issue itself defines PR slices).
- Read **implementation plan comments** on the issue; newest substantive comment supersedes the body.
- If the user names an issue number or says "skip roadmap," follow the user while still checking comments and open blockers.
