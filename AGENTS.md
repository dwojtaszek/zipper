# AI Agent Instructions for Zipper

## Principles

1. **Think before coding:** State assumptions, surface tradeoffs, ask if unclear. Fix root causes, not symptoms.
2. **Simplicity first:** No speculative features, no single-use abstractions, minimum code. YAGNI.
3. **Surgical changes:** Don't touch adjacent code, match existing style, no drive-by refactors. Output complete files — no placeholders or ellipses.
4. **Goal-driven execution:** Define verifiable success criteria before starting. Loop until met. Include error handling.

**When Principles conflict, the lower-numbered Principle wins.**

---

## Agent Behavior

### Tone
- Push back on bad ideas, unreasonable expectations, and mistakes. Do not be deferential.
- Flag when you don't know something. Stop and ask for clarification when uncertain.
- If you disagree, even on gut feeling, say so.

**Push-back examples:**
- "You asked for a config flag, but this is a single-use case — YAGNI. Here's the simpler alternative."
- "That refactor touches 12 files but the issue only affects 1. I'll fix just the root cause and note the rest as tech debt."
- "The test you're asking me to delete covers a real edge case. I'll fix the test instead."
- "I can't verify this works without running the E2E suite. I'll run it before marking complete."

### Testing & Debugging
- NEVER test just mocked behavior. Tests must verify real outcomes.
- Fix all tests that fail, even if your change didn't break them.
- Always root cause bugs. Never fix just the symptom. Never implement a workaround.
- If you cannot find the root cause, stop and compile what you've learned.

**See also:** `.claude/skills/test-driven-development/SKILL.md`, `.claude/skills/testing-anti-patterns/SKILL.md`, `.claude/skills/systematic-debugging/SKILL.md`, `.claude/skills/root-cause-tracing/SKILL.md`

### When Stuck

If you've spent significant effort without progress, follow this protocol before asking for help:

1. **State what you're trying to do** in one sentence.
2. **List what you've tried** — approaches, hypotheses, and why each failed.
3. **Identify the gap** — what specifically is unknown or blocking? (Missing domain knowledge? Unclear requirement? Tool limitation?)
4. **Propose next steps** — what would you try if you had to continue alone?

Output format:

```
Goal: <one sentence>
Tried:
- <approach 1> → <why it failed>
- <approach 2> → <why it failed>
Gap: <what's unknown>
Next: <what you'd try next>
```

Do not silently skip blocked work or switch to adjacent tasks. A stuck report is more valuable than a partial solution to the wrong problem.

---

## How To Use This File

- This file is the agent workflow guide. Product behavior → [Requirements.md](Requirements.md). User-facing usage → [README.md](README.md). Domain terms → [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md). CI/CD pipeline map (local hooks → PR checks → release) → [docs/cicd.md](docs/cicd.md); external-check operations → [CI.md](CI.md).
- If an issue body, its comments, README.md, Requirements.md, and implementation disagree, stop and identify the conflict explicitly before coding. Do not silently choose one source. (Exception: a newer issue comment superseding the issue body is not a conflict — see the issue workflow, step 4.)

---

## Commands

```bash
dotnet restore zipper.sln                # Restore NuGet packages
dotnet build zipper.sln                  # Build
dotnet publish -c Release               # Publish
dotnet run --project src/Zipper.csproj -- [args]  # Run

# Tests
dotnet test src/Zipper.Tests/Zipper.Tests.csproj                              # Unit tests (must pass before commit)
dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~ClassName"  # Single test class
dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj          # Analyzer tests (must pass when touching src/Zipper.Analyzers/)

# Lint
dotnet format --verify-no-changes src/   # Format check (run after every code change)

# Build + lint + test combo (run after every change)
dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj && dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj

# E2E (must pass before push)
# The pre-push hook runs the basic E2E smoke against src/bin/Release — build Release first.
dotnet build -c Release
./tests/run-tests.sh   # Linux/macOS
tests/run-tests.bat    # Windows
```

---

## Critical Rules

**When rules conflict, Principles override Critical Rules.** Among Critical Rules, the order below determines priority — higher wins.

1. **Domain Language:** Read and follow [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md) for all code, comments, documentation, and reviews. Grep the file before writing docs/PRs; flag non-canonical terms in review. *Canonical terminology is non-negotiable — even when a non-canonical term seems simpler, Domain Language wins over Simplicity First.*

2. **Requirement IDs are IMMUTABLE.** REQ-XXX and FR-XXX numbers must NEVER be changed or renumbered.

3. **Test coverage must not decrease in substance.** Every removed or modified test must be replaced with a test that covers the same or a stricter behavioral contract. If no behavioral contract was being tested (e.g., a test that only asserted line execution), a behavior-coverage replacement is required. Never remove test files to make a test run green. When replacing an implementation behind a stable interface, **retarget** the existing output-asserting tests onto the replacement (swap the construction, e.g. `new OldWriter()` → `new NewWriter()`) so they keep guarding the same contract instead of being deleted. *When this conflicts with Surgical Changes (e.g., fixing a pre-existing failing test requires touching adjacent code), Surgical Changes wins — but flag the conflict.*

4. **Documentation Sync:** Any change to CLI behavior, Load File/Audit File/Production Set formats, or Email domain names must update **all** of:
   1. `README.md` — Arguments Quick Reference, Argument Interactions, examples
   2. `Requirements.md` — add or revise requirements (never renumber)
   3. `UBIQUITOUS_LANGUAGE.md` — if domain terms change
   4. E2E scripts — both `.sh` and `.bat` for new coverage

Verify behavior changes against Requirements.md before committing. Run `grep -n "REQ-XXX" Requirements.md` for each affected requirement. *When this conflicts with Simplicity First (e.g., a trivial code change triggers a 4-doc update cascade), Simplicity First wins — but flag the conflict and note which docs are out of sync.*

5. **Architecture invariants:** [docs/architecture.md](docs/architecture.md) diagrams are the source of truth for system structure — notably the load-file `composer → serializer → emitter` seam and the EDRM-XML carve-out. Any change that deviates from them, or makes a diagram inaccurate, requires **explicit human approval** and a same-PR diagram update. AI agents must stop and ask the maintainer (e.g. via AskUserQuestion) before merging such a change. Rationale: ADR-0006, ADR-0007.

6. **Output parity for refactors:** Before refactoring code that produces Load File / Audit File / Production Set output, capture a **seeded golden baseline** of representative scenarios as a content-hash manifest (timestamp/filename-independent), then diff it after **every** step. Byte-for-byte parity is what makes it safe to delete or restructure writers. Keep the harness local and gitignored. *Preserve historical output quirks (EOL, encoding, path separators) exactly — file a follow-up issue rather than "fixing" them inline, since that changes bytes and breaks parity.*

---

## Workflow for github issues

### Issue selection (mandatory for autonomous / "next issue" work)

Agents must **not** pick issues by gut feel, recency, or title interest. Follow this algorithm every time before branching.

#### 1. Load the living roadmap (if any)

```bash
gh issue list --state open --limit 200 --json number,title,labels \
  --jq '.[] | select(.title | test("^Roadmap:"; "i")) | "#\(.number) \(.title)"'
```

- If a **Roadmap:** issue is open (e.g. [#627](https://github.com/dwojtraszek/zipper/issues/627)):
  - Read it **and all comments** (`gh issue view NNN --comments`).
  - Treat its **phase order and exit criteria** as the sequencing contract for leaf issues it lists.
  - **Never implement the roadmap issue itself** (it is a tracker only).
- If none is open, continue with label ranking only (below).

#### 2. Rank by priority labels (primary key)

Every work issue should carry exactly one of: **`P0`**, **`P1`**, **`P2`**, **`P3`**.

| Label | Meaning | When to take |
|-------|---------|--------------|
| `P0` | Blocker / production broken / data loss | Immediately; only open work until cleared |
| `P1` | High — must fix soon (security, correctness, coverage holes on new critical code) | After all open `P0` |
| `P2` | Medium — this iteration (refactors, product features, docs that unblock agents) | After all open `P0`/`P1` |
| `P3` | Low — nice to have | Only when no open `P0`–`P2`, or user names the issue |

```bash
# List open issues sorted by Pn, then type-label rank, then number
gh issue list --state open --limit 200 --json number,title,labels \
  --jq 'def p: (if any(.labels[].name; .=="P0") then 0 elif any(.labels[].name; .=="P1") then 1 elif any(.labels[].name; .=="P2") then 2 elif any(.labels[].name; .=="P3") then 3 else 9 end);
    def t: (if any(.labels[].name; .=="security") then 1 elif any(.labels[].name; .=="bug") then 2 elif any(.labels[].name; .=="ci-cd") then 3 elif any(.labels[].name; .=="testing" or .=="e2e") then 4 elif any(.labels[].name; .=="performance" or .=="perf") then 5 elif any(.labels[].name; .=="refactor" or .=="architecture" or .=="quality") then 6 elif any(.labels[].name; .=="enhancement" or .=="design") then 7 elif any(.labels[].name; .=="documentation" or .=="docs" or .=="requirements") then 8 else 9 end);
    sort_by(p, t, .number)[] | "P\(p) T\(t) #\(.number) \(.title)"'
```

Issues **without** a `P0`–`P3` label rank last (priority 9). Prefer labeling them before implementing. If an issue carries multiple Pn labels, the highest priority (lowest number) wins. Increase `--limit` if the backlog exceeds 200 open issues.

**Legacy mapping (only if an issue has no Pn label):** treat as secondary signals — `security` / critical `bug` ≈ P0–P1; other `bug` ≈ P1; `testing`/`e2e` ≈ P1–P2 when fixing coverage holes; `refactor`/`architecture`/`design` ≈ P2; `documentation`/`docs` ≈ P3. Prefer adding a Pn label over relying on this fallback.

#### 3. Within the same Pn, apply type-label tie-break

Lower rank number wins:

| Rank | Labels (any of) | Intent |
|------|-----------------|--------|
| 1 | `security` | Path traversal, injection, unsafe paths |
| 2 | `bug` | Incorrect behavior / silent failures |
| 3 | `ci-cd` | Gates that prevent bad merges (coverage, hooks) |
| 4 | `testing`, `e2e` | Missing tests that unlock safe refactors |
| 5 | `performance`, `perf` | Hot-path / allocation |
| 6 | `refactor`, `architecture`, `quality` | Structure without behavior change |
| 7 | `enhancement`, `design` | Product features (large) |
| 8 | `documentation`, `docs`, `requirements` | Docs-only |
| 9 | (none of the above) | Default |

Then sort by **issue number ascending** as the final stable tie-break.

#### 4. Honor blockers and roadmap phase order

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
- Issue is clearly **stale** (see step 5 in per-issue workflow) — comment and stop; do not implement.

**Special sequencing (when roadmap lists them):** prefer explicit phase lists on the Roadmap issue over pure number order. Example pattern from #627:

1. Security / silent-correctness (`security`, `bug` under P1)
2. Tests for the risky module (`testing`)
3. Decompose that module (`refactor`)
4. CI gate so the hole cannot return (`ci-cd`)
5. Broader refactors / goldens (`P2`)
6. Large enhancements (`enhancement` + `design`) only after earlier phases' exit criteria

#### 5. Select exactly one leaf issue

- Announce: issue number, Pn label, type labels, and one-line why it ranked first.
- One issue per branch/PR (except when the issue itself defines PR slices — e.g. "PR1 DTOs only").
- Read **implementation plan comments** on the issue; newest substantive comment supersedes the body.

#### 6. User override

If the user names an issue number or says "skip roadmap," follow the user. Still read that issue's comments and skip if blocked unless the user overrides the blocker too.

---

**Per-issue workflow:**
1. **Select issue** via [Issue selection](#issue-selection-mandatory-for-autonomous--next-issue-work) (or user-named issue).
2. `git checkout main && git pull`
3. `git checkout -b fix/ISSUE-NNN-short-desc` (prefix: `fix/` for bugs, `feat/` for features, `refactor/`, `test/`, `docs/` per issue type)
4. Use Conventional Commits for commit messages (`fix:`, `feat:`, `refactor:`, `test:`, `docs:`, `chore:`, `deps:`)
5. Read the issue body **and all comments** (`gh issue view NNN --comments`); refresh labels and linked blockers before coding:
   - Comments are part of the spec: design decisions, implementation guides, and staleness notes posted after the body supersede it.
   - If the newest substantive comment contradicts the body, follow the comment and say so in the PR.
   - If the issue itself is stale (the code it describes no longer exists, or another change already resolved it), do not implement it as written — comment on the issue with evidence and a recommendation (close or retarget), then stop.
6. Write a failing test first (TDD), then implement the fix
7. Run `dotnet format --verify-no-changes src/` and `dotnet test src/Zipper.Tests/Zipper.Tests.csproj && dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj` after every change
8. Run autoreview before creating PR (see Adversarial Review section below). *Required for any change touching logic, error handling, or public contracts. For docs-only, version-bump, or single-line fixes, a self-review suffices — note the exemption in the PR.*
9. Commit and create PR — include `## Release Notes` per the [Release Notes Mandate](#release-notes-mandate) below. Reference `Fixes #NNN`. If the work advances a Roadmap phase, mention the roadmap issue in the PR body (do not auto-close it).
10. Monitor CI until all checks pass; fix failures before requesting review. Reproduce each gate locally first — see the [docs/cicd.md](docs/cicd.md#quick-reference-for-agents) gate-to-command table so you fail fast instead of waiting on CI minutes. If a CI failure appears flaky (same test passes locally, or failure is in an unrelated component), re-run once. If it fails again, document the flake in the PR and proceed to request review. Push fixes via `git commit --amend --no-edit && git push --force-with-lease`.
11. Check SonarCloud on the PR after CI completes (see [CI.md](CI.md#sonarcloud)). Fix all BLOCKER and MAJOR issues before merge. The quality **gate** can also fail on new-code *conditions* (duplication ≥3%, coverage) with **zero** BLOCKER/MAJOR issues — query the gate conditions, not just the issue list. When adding parallel per-format modules (e.g. a composer/serializer per format), extract a shared base/builder to stay under the duplication threshold.
12. **Run `bash tests/wait-for-reviews.sh <PR-number>` after creating the PR and again after every push.** The script blocks until every robot reviewer (CodeRabbit, Codex) has reviewed or declared a rate-limit skip, then exits non-zero while any review thread is unresolved. A "pass" or "skipped" check status from a review bot is **not** an approval — only the script's exit 0 is. If a bot stays silent past the script's timeout, it produces a **warning** (not a failure) — proceed but watch for late threads, which branch protection will still block.

    For each finding it reports: verify it against current code, fix if still valid, or reply on the thread with a brief skip reason (e.g. conflicts with an explicit design decision), then resolve the thread via GraphQL (`gh api graphql -f query='mutation { resolveReviewThread(input: {threadId: "PRRT_..."}) { thread { isResolved } } }'`) and re-run the script. Before re-running, check `manage_task` (`list`) and kill any prior `wait-for-reviews` background tasks to prevent task clutter. Blocking/major issues must be fixed; nitpicks may be skipped-with-reason but never silently ignored. **A review-driven fix that changes behavior can stale the architecture diagram, ADRs, glossary, or code comments — re-verify those (Critical Rules 1, 4, and 5) before pushing the fix.**

    Fallback if the script is unavailable — bots post to three *separate* endpoints; query all three (the PR web view and `gh pr view` alone miss inline threads):
    - **Inline review comments** (code-anchored): `gh api repos/<owner>/<repo>/pulls/<N>/comments --paginate`
    - **Review summary bodies** (verdict + overview): `gh api repos/<owner>/<repo>/pulls/<N>/reviews --paginate`
    - **Issue-level PR comments** (CodeRabbit walkthrough, SonarCloud gate, perf guard): `gh api repos/<owner>/<repo>/issues/<N>/comments --paginate`
13. Merge after all checks pass, `tests/wait-for-reviews.sh` exits 0, and reviews are addressed. Branch protection on `main` enforces this server-side: GitHub refuses the merge while any review thread is unresolved (see [CI.md](CI.md#robot-reviews)).
14. Post-merge: Run `gh pr merge <PR-number> --squash --delete-branch && git checkout main && git pull && git worktree remove .worktrees/<branch-name> --force && gh issue close <issue-number> -c "Resolved in PR #<PR-number>"` to execute clean post-merge closeout in a single sequence. If a **Roadmap:** issue tracks the work, comment there that the leaf issue closed (phase progress) — do not close the roadmap until its exit criteria say so.

**Feature implementation ladder (order matters):**
1. `src/Cli/ParsedArguments.cs` — add property
2. `src/Cli/CliParser.cs` — add case in switch
3. `src/Cli/CliOptions.cs` — add to appropriate list (ParameterlessFlags, SingleValueArgs, etc.)
4. `src/Config/MetadataConfig.cs` — add property + `ShouldInclude*Columns()` method
5. `src/Cli/RequestBuilder.cs` — wire parsed arg to config
6. `src/Profiles/BuiltInProfiles.cs` — add legacy profile if needed
7. `src/LoadFiles/DatComposer.cs` — `BuildHeaderColumns()` then `StandardRowValues()`
8. `src/LoadFiles/StandardRowComposer.cs` — `BuildOrderedKeys()` then `Resolve()` + call `SyntheticRowValues`
9. `src/LoadFiles/SyntheticRowValues.cs` — add value-generation method
10. `src/Zipper.Tests/DatComposingWriterTests.cs` — add tests
11. `README.md` — usage line, Arguments Quick Reference, Argument Interactions

**Test location:** `src/Zipper.Tests/`.

**Pre-commit hook:** Runs format + unit tests on every `git commit` (short-circuits for docs-only changes — `*.md` and `docs/`). Bypass: `git commit --no-verify`.

---

## Release Notes Mandate

**Every PR must include a `## Release Notes` section in its body.** CI reads this section and publishes it verbatim as the GitHub Release body. One PR = one release.

**Scale to change size:**
- Small change (bug fix, docs, chore, test, single-file refactor): **1-2 sentences.** What changed and its effect.
- Larger change (new feature, multi-system refactor, breaking change): **3-5 sentences.** What changed, why, and key impact.

**Rules:**
- Plain prose. No bullet lists, no nested headers, no markdown decoration (except the fallback string below).
- Key facts only. No fluff, no marketing language, no internal ticket references.
- Use terms from [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md).
- Cannot determine impact? Write exactly `_Auto-generated by CI._` — CI calls GitHub Models API to generate from commits instead.

The `## Release Notes` section is pre-populated in [`.github/pull_request_template.md`](.github/pull_request_template.md) — replace the placeholder text.

---

## Adversarial Review

Run the autoreview skill before creating a PR. This is mandatory for any change touching logic, error handling, or public contracts. For docs-only, version-bump, or single-line fixes, a self-review suffices — note the exemption in the PR.

The skill runs entirely inside the current coding agent (Claude Code or Cursor): it produces the diff with git, then dispatches parallel review subagents via the `Agent` tool. No external reviewer CLI. Default target is local/uncommitted work; point it at a branch or commit diff for pushed/PR work.

See `.agents/skills/autoreview/SKILL.md` for full methodology (target selection, two-pass review, specialist dispatch, confidence calibration, adversarial patterns).

For lightweight subagent review without the full skill:

**Dispatch format:**

```
Review the changes in <branch/file list> against the original request: <request summary>.

Check all of the following:
1. Correctness: Does the code do what was asked? Are there edge cases missed?
2. Spec compliance: Does it match Requirements.md and the issue body exactly?
3. Side effects: Were any adjacent files, formatting, or unrelated code changed?
4. Test quality: Do tests verify real behavior, not mocks? Do they cover edge cases?
5. Security: Any new attack surface, unsafe input handling, or exposed secrets?
6. Performance: Any new allocations in hot paths? O(n) where O(1) was possible?
7. Domain language: Are all terms canonical per UBIQUITOUS_LANGUAGE.md?

Be harsh. I'll be disappointed if you don't find at least one significant problem.
```

**Techniques to increase rigor:**
- **Cross-model:** Use a different model for review than for coding, if available.
- **Competition:** Ask two subagents to review; the one that finds the most serious issues wins.
- **Set expectations:** "I'll be disappointed if they don't find at least N significant problems."

---

## SonarCloud & External Checks

See [CI.md](CI.md) for SonarCloud, CodeRabbit, CodeQL, and golden file procedures. Fix all BLOCKER and MAJOR issues before merge.

---

## Project Structure

**Solution:** `zipper.sln` — **Warnings as Errors** enabled.

| Path | Purpose |
|------|---------|
| `src/Program.cs` | Entry point, CLI orchestration, mode dispatch |
| `src/Cli/` | CLI parsing, validation, help text, request assembly |
| `src/FileGenerationRequest.cs` | Configuration root (9 sub-configs + `LoadfileOnly` flag) |
| `src/IGenerationMode.cs` / `GenerationRunner.cs` | Mode interface + dispatcher |
| `src/StandardMode.cs` / `LoadFileOnlyMode.cs` / `ProductionSetMode.cs` | Three generation mode adapters |
| `src/IFileGenerator.cs` / `FileGeneratorFactory.cs` | File generator interface + factory |
| `src/ParallelFileGenerator.cs` | Standard mode file generation pipeline |
| `src/IArchiveSink.cs` / `src/ZipArchiveSink.cs` | Archive creation + Load File writing (Standard mode consumer) |
| `src/LoadFileOnlyGenerator.cs` | Standalone Load File generation |
| `src/ProductionSetGenerator.cs` | Production Set directory tree + Load Files |
| `src/ChaosEngine.cs` | Chaos anomaly injection |
| `src/ChaosAnomalyTypes.cs` | Canonical anomaly type catalog |
| `src/ProductionSetPlanner.cs` | Production Set path/volume/bates planning |
| `src/LoadFiles/LoadFileRecord.cs` | Format-independent load file row model (raw values as parallel arrays aligned with columns) |
| `src/LoadFiles/ILoadFileComposer.cs` | Column authority: header columns + lazy records (`Dat`/`Opt`/`Csv`/`Concordance` composers) |
| `src/LoadFiles/ILoadFileSerializer.cs` | Render authority: record/header → one escaped line (`Dat`/`Opt`/`Csv`/`Concordance` serializers) |
| `src/LoadFiles/LoadFileEmitter.cs` | I/O + chaos authority: preamble, EOL, batching, chaos pipeline |
| `src/Profiles/Generation/` | Column value generators |
| `src/Emails/` | Email domain model |
| `src/LoadFiles/` | Load File seam: composers, serializers, emitter, thin composing writers + `XmlLoadFileWriter` carve-out |
| `src/Profiles/` | Column profile system (loader, data generator, built-ins) |
| `src/Config/` | Nested request config records (`OutputConfig`, `MetadataConfig`, `LoadFileConfig`, `DelimiterConfig`, `BatesNumberConfig`, `TiffConfig`, `ChaosConfig`, `ProductionConfig`, `HashConfig`) + `HashUtility` |
| `src/Validation/` | Post-generation / Production Set / supplemental validators (`PostGenerationValidator`, `ValidatorRunner`, `ProductionSetPostValidator`, `SupplementalValidator`) |
| `src/ManifestComparison/` | Production Manifest comparison reports (`ProductionManifestComparer`, `--compare-production-manifests`) |
| `src/Utils/` | `NamingConventionHelper` |
| `src/LoadFileAuditWriter.cs` / `ProductionManifestWriter.cs` | Audit + manifest writers |
| `src/Zipper.Tests/` | Unit tests |
| `src/Zipper.Analyzers/` | Roslyn analyzers |
| `tests/` | E2E test scripts |

Individual file generators (`EmlFileGenerator.cs`, `TiffFileGenerator.cs`, `OfficeFileGenerator.cs`, `PlaceholderFileGenerator.cs`) and `BatesSequence.cs` live in `src/` — grep by type.

---

## Architecture

[docs/architecture.md](docs/architecture.md) is the **source-of-truth** architecture reference (mode dispatch, pipeline design, component + load-file-seam diagrams). Read it before any structural change.

**Invariant:** the diagrams there are a contract — notably the load-file `composer → serializer → emitter` seam and the EDRM-XML carve-out. Any deviation, or any change that makes a diagram stale, requires **explicit human approval** plus a same-PR diagram update (see Critical Rule 5 and the PR template's **Architecture** checklist). AI agents must stop and ask the maintainer before merging such a change.

---


## Code Style

- C# 14 (net10.0), file-scoped namespaces, nullable reference types, switch expressions, pattern matching
- Distribution algorithms must be O(1) per file. Use `Span<T>`, `ArrayPool<T>`, avoid allocations in hot paths
- ZIP entry paths always use `/`; normalize to backslashes with `.Replace('/', '\\')`, never `Replace(Path.DirectorySeparatorChar, '\\')` (a no-op on Windows, where the separator is already `\`)
- **No copyright headers** — do not add `// <copyright ...>` to any files
- **Review guardrails:** Before filing a finding, check `docs/code-review-guidelines.md` for project-specific patterns that look like issues but are deliberate. Applies to human review, robot reviewers (CodeRabbit, Codex), and the autoreview skill.

### Test Naming Convention

Test classes and methods follow the pattern:
- **Class:** `{Subject}Tests` (e.g., `BatesSequenceTests`, `ChaosEngineTests`)
- **Method:** `{Method}_{Scenario}_{Expected}` (e.g., `Generate_WithCustomPrefix_ShouldIncludePrefix`)

This convention is enforced by code review and the existing test corpus. All new tests must follow it.
