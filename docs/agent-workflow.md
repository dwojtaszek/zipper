# Agent workflow reference

Read the section selected by [AGENTS.md](../AGENTS.md). Shared Git policy governs interactive work; runner dispatches explicitly name their own issue worktrees.

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
- Fix failures caused by the change. Report unrelated failures separately, with evidence and their effect on validation; expand scope only with user authorization. If an unrelated failure blocks a required gate, report the gate as blocked rather than bypassing it or claiming success.
- Always root cause bugs. Never fix just the symptom. Never implement a workaround.
- If you cannot find the root cause, stop and compile what you've learned.
- **Property-Based Testing (`FsCheck.Xunit`):** When adding or modifying serializers/parsers, path/metadata sanitizers, Bates/distribution/volume planners, Chaos Engine appliers, or Production Manifest Comparison logic, add or update `[Property]` tests in `src/Zipper.Tests/Properties/` following [docs/property-based-testing.md](../docs/property-based-testing.md). Verify real production seams (never copy-paste production logic into test helpers), draw seed ranges in the arbitraries with `Gen.Choose` (fall back to `Math.Abs((long)seed)` only for raw `int` seeds) to avoid `int.MinValue` overflow during shrinking, and keep each property's 100 cases under 500 ms.
- **Catalog growth:** When proposing or adding a Chaos Engine Anomaly Type or an Archive Test Case Key, follow [Catalog governance](../docs/archive-test-suites.md#catalog-governance): cite a documented real-world failure in the issue, and turn every fixed real-world parse/import defect into a pinned Case Key or Anomaly Type with a minimized regression seed and a test.

**See also:** [docs/property-based-testing.md](../docs/property-based-testing.md), `.claude/skills/test-driven-development/SKILL.md`, `.claude/skills/testing-anti-patterns/SKILL.md`, `.claude/skills/systematic-debugging/SKILL.md`, `.claude/skills/root-cause-tracing/SKILL.md`

### Workflow Selection

- Follow a user-named workflow first; otherwise choose one primary workflow matching the task: diagnosis for an unknown failure, implementation for a bounded change, or review for a requested review. Load supporting skills only for distinct requirements, not synonymous versions of the same workflow. Required host skill triggers and repository gates still apply.
- Use Wayfinder for uncertain work spanning multiple sessions, and Retro after repeated corrections or a costly session. Neither is a mandatory step for routine patches. Keep Wayfinder decision tickets outside runner intake; resolve decisions before handing implementation issues to the runner.
- Caveman and Ponytail behavior comes from the shared host rules. Reuse discovered skills rather than installing additional copies or loading equivalent guidance from multiple sources.

### Runtime Capabilities

- Treat the current tool catalog and schemas as authoritative. Call already-loaded tools directly. Discover missing tools with one non-empty exact-name lookup, then at most one targeted search. If no matching tool appears, report the missing capability and follow the harness's connector/fallback policy.
- Map skill examples to the host's native subagent tool. For Droid `Task`, use a catalog-listed `subagent_type` and its actual schema. Review effort `High` is not a tool argument: `complexity` accepts `light`, `medium`, or `heavy` where exposed. Keep reviewers read-only.
- For GitHub CLI access allowed by the harness, read issue comments with `gh issue view NNN --json title,body,labels,comments` or `gh issue view NNN --comments`. Use one form, not both. `gh pr checks` exit 8 means pending, not failed.
- Batch accepted review findings before the next review. Rerun focused tests while iterating; run every required final gate against the final changes. Re-review behavior-changing fixes, stop when no actionable findings remain, and retain the autoreview cycle limit.

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

## Commands

Prefer `./dev.sh` (`./dev.sh --help` for flags, `dev.bat` on Windows); it builds Release once and selects only diff-affected gates (`check`, `test`, `e2e`, `mutation`, `lint`, `sonar`, `all`).

Non-discoverable gate conventions:
- **Git hook reuse:** `dotnet build` syncs `.github/hooks/*` automatically. `pre-commit` validates staged formatting + unit tests (short-circuiting on docs-only, runner-only, and CI-script-only commits); `pre-push` reuses fresh `pre-commit.ok` markers, skips baseline re-runs in `run-mutation-smoke.sh`, and skips E2E smoke when no CLI/E2E files changed. Never forge markers.
- **Mutation cache:** `tests/run-mutation-smoke.sh` caches killed/compile-error mutants under `.git/zipper-hooks/mutation-cache/` (bypass with `--no-cache` or `MUTATION_SMOKE_CACHE=0`).
- **Python multi-suite logs:** Wrap `.zipper-runner/tests`, `tests/ci`, or `tools/typesafe-audit/tests` with `.github/scripts/run-check.py --log <path> -- ...` so output stays bounded.
- **Coverage gate:** `ci.yml` enforces ≥80% aggregate line, ≥85% aggregate branch, and ≥50% per-file line coverage (≥20 lines) via `.github/actions/coverage-gate/check-per-file-coverage.py`.
- **Full E2E suite:** `./tests/run-tests.sh` (`--serial` or `--jobs N`) / `tests/run-tests.bat`.

---

## GitHub issue & PR workflow

For issue selection, wave prioritization, per-issue steps, release notes, autoreview, and local CodeRabbit review gates, see [issue-workflow.md](issue-workflow.md).

---

## SonarCloud & External Checks

See [CI.md](../CI.md) for SonarCloud, CodeRabbit, CodeQL, and golden file procedures. Fix all BLOCKER, CRITICAL, and MAJOR issues before merge.

---

## Project Structure

**Solution:** `zipper.sln` — **Warnings as Errors** enabled. Use `symbol_search` / `grep` for individual types; top-level ownership:

| Path | Responsibility |
|------|----------------|
| `src/` | Entry point (`Program.cs`), mode adapters, generators, chaos engine, production orchestration |
| `src/Cli/` | `CliModuleSet`, `CrossCuttingRules`, `Pipeline`, and twelve domain CLI modules under `Modules/` |
| `src/Config/` | Immutable request sub-configs (`OutputConfig`, `MetadataConfig`, `LoadFileConfig`, etc.) |
| `src/LoadFiles/` | `composer → serializer → emitter` seam (`ILoadFileComposer`, `ILoadFileSerializer`, `LoadFileEmitter`, `XmlLoadFileWriter` carve-out) |
| `src/Emails/` | Threaded/unthreaded email synthesis and MIME realism |
| `src/Profiles/` | Column profiles and value generators |
| `src/SourceInput/` | Source CSV / directory template loaders |
| `src/Validation/` | Post-generation, reconciliation, and chaos expectation validators |
| `src/ArchiveTests/` & `src/ManifestComparison/` | Archive Test Suite fixtures/mutations and Production Manifest Comparison |
| `src/Zipper.Tests/` | Unit and FsCheck property tests (`Properties/`) |
| `src/Zipper.Analyzers/` & `src/Zipper.Analyzers.Tests/` | Custom Roslyn analyzers (`ZIP001`–`ZIP005`) and tests |
| `.github/`, `.zipper-runner/`, `tests/` | CI workflows/scripts, autonomous runner, and E2E/CI test suites |

---
