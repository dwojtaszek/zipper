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

```bash
dotnet restore zipper.sln                # Restore NuGet packages
dotnet build zipper.sln                  # Build (~40-65s cold)
dotnet publish -c Release               # Publish
dotnet run --project src/Zipper.csproj -- [args]  # Run

# Tests
dotnet test src/Zipper.Tests/Zipper.Tests.csproj                              # Unit + property-based tests (~20-40s; must pass before commit)
dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "Category=Property" # Property-based tests only (FsCheck.Xunit)
dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~ClassName"  # Single test class
dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj          # Analyzer tests (must pass when touching src/Zipper.Analyzers/)
bash tests/run-mutation-smoke.sh                                             # Mutation smoke on changed source lines (~30-90s; must pass before push)
# Multi-suite Python checks: wrap with run-check.py --log so output stays bounded (e.g. .zipper-runner/tests + tests/ci take ~2-5m combined)
python3 .github/scripts/run-check.py --log /tmp/zipper-py.log -- python3 -m unittest discover -s tools/typesafe-audit/tests -p 'test_*.py'
dotnet test src/Zipper.Tests/Zipper.Tests.csproj --collect:"XPlat Code Coverage" --results-directory ./TestResults && dotnet reportgenerator -reports:TestResults/*/coverage.cobertura.xml -targetdir:TestResults/coverage-report -reporttypes:TextSummary && python3 .github/actions/coverage-gate/check-per-file-coverage.py --reports "TestResults/*/coverage.cobertura.xml" --min-coverage 50 --min-lines 20 # Coverage report generation and per-file gate (per-file ≥ 50%; aggregate line ≥ 80% and branch ≥ 85% enforced in CI Summary.txt)

# Lint
dotnet format --verify-no-changes src/   # Required final format gate

# Smart local developer CLI (recommended — builds Release once and runs only diff-affected gates)
./dev.sh                                 # Inspect git diff vs main and run only affected gates (check)
./dev.sh --dry-run                       # Preview which gates would run for current changes
./dev.sh all                             # Run all required pre-PR gates (build once, lint, unit/analyzer, mutation, E2E smoke)
dev.bat                                  # Windows equivalent

# Local iteration vs. Git hook gates:
# - Local builds (`dotnet build`) automatically install/sync `.github/hooks/*` via `Directory.Build.props` (`SyncGitHooks` target, skipped in CI).
# - Preview affected gates with `./dev.sh --dry-run`, then run `./dev.sh`; use focused tests for the changed behavior while iterating.
# - `run-mutation-smoke.sh` caches killed and compile-error mutants by SHA-256 content hash under `.git/zipper-hooks/mutation-cache/` (bypass with `--no-cache` or `MUTATION_SMOKE_CACHE=0`).
# - Do NOT manually run the full unit test suite right before `git commit` or `git push`:
#   * `pre-commit` automatically runs `dotnet format --verify-no-changes src/` + unit tests (or `DocsDriftTests` on docs-only commits) and writes `.git/zipper-hooks/pre-commit.ok`.
#   * `pre-push` reuses a fresh `pre-commit.ok` to skip re-running unit tests, skips the redundant baseline test run in `run-mutation-smoke.sh` (and reuses cached mutant results), and skips `run-e2e-basic.sh` when no CLI/E2E files changed.
# - Let hooks validate reuse; do not create or copy success markers. Changes after a successful check require rerunning the affected gates.
# - Without active hooks, or when validating without committing, use the affected gates above; use the full manual gate below when the change requires the full suite:
dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj --no-build && dotnet test src/Zipper.Analyzers.Tests/Zipper.Analyzers.Tests.csproj --no-build && MUTATION_SMOKE_SKIP_BASELINE=1 bash tests/run-mutation-smoke.sh

# Full E2E suite (`_zipper-cli` builds Release automatically once per suite; standalone suites run in parallel on Unix, override with `--serial` or `--jobs N`)
./tests/run-tests.sh   # Linux/macOS
tests/run-tests.bat    # Windows
```

---

## GitHub issue & PR workflow

For issue selection, wave prioritization, per-issue steps, release notes, autoreview, and local CodeRabbit review gates, see [issue-workflow.md](issue-workflow.md).

---

## SonarCloud & External Checks

See [CI.md](../CI.md) for SonarCloud, CodeRabbit, CodeQL, and golden file procedures. Fix all BLOCKER and MAJOR issues before merge.

---

## Project Structure

**Solution:** `zipper.sln` — **Warnings as Errors** enabled.

| Path | Purpose |
|------|---------|
| `src/Program.cs` | Entry point, `CliModuleSet.Parse`, Production Manifest Comparison and Archive Test Suite short-circuits, mode dispatch |
| `src/Cli/` | `CliModuleSet.Parse`, `CrossCuttingRules`, help text, `Pipeline.Build` request assembly |
| `src/Cli/Modules/` | Twelve domain modules (`Hash`/`Delimiter`/`Tiff`/`Chaos`/`Bates`/`Metadata`/`LoadFile`/`Production`/`SourceInput`/`Output`/`Comparison`/`ArchiveTest`) |
| `src/FileGenerationRequest.cs` | Configuration root (9 sub-configs + `LoadfileOnly` flag + `SourceRecords`) |
| `src/IGenerationMode.cs` / `GenerationRunner.cs` | Mode interface + dispatcher |
| `src/StandardMode.cs` / `LoadFileOnlyMode.cs` / `ProductionSetMode.cs` | Three generation mode adapters |
| `src/IFileGenerator.cs` / `FileGeneratorFactory.cs` | File generator interface + factory |
| `src/ParallelFileGenerator.cs` | Standard mode file generation pipeline |
| `src/IArchiveSink.cs` / `src/ZipArchiveSink.cs` | Archive creation (Standard mode consumer); Load File emission delegated to `LoadFileOrchestrator` |
| `src/LoadFileOnlyGenerator.cs` | Standalone Load File generation |
| `src/ProductionSetGenerator.cs` | Production Set directory tree + Load Files |
| `src/ProductionSet*.cs` | Production Set orchestration (`ProductionSetOrchestrator`, `ProductionMetadataBudget`, `ProductionNativeFilePlan`) |
| `src/ChaosEngine.cs` | Chaos anomaly injection |
| `src/ChaosAnomalyTypes.cs` | Canonical anomaly type catalog |
| `src/ProductionSetPlanner.cs` | Production Set path/volume/bates planning |
| `src/SourceInput/` | Source-Driven Generation loaders (`SourceCsvLoader`, `DirectoryTemplateScanner`, `SourceRecord`) |
| `src/ArchiveTests/` | Archive Test Fixture and Expectation File generation (`ArchiveTestRunner`, `ArchiveTestSuiteGenerator`, `ArchiveFixtureBuilder`, `ArchiveTestMutator`) |
| `src/LoadFiles/LoadFileRecord.cs` | Format-independent Load File Record model (raw values as parallel arrays aligned with columns) |
| `src/LoadFiles/ILoadFileComposer.cs` | Column authority: header columns + lazy records (`Dat`/`Opt`/`Csv`/`Concordance` composers) |
| `src/LoadFiles/ILoadFileSerializer.cs` | Render authority: record/header → one escaped line (`Dat`/`Opt`/`Csv`/`Concordance` serializers) |
| `src/LoadFiles/LoadFileEmitter.cs` | I/O + chaos authority: preamble, Line Ending, batching, chaos pipeline |
| `src/Profiles/Generation/` | Column value generators |
| `src/Emails/` | Email domain model |
| `src/LoadFiles/` | Load File seam: composers, serializers, emitter, thin composing writers + `XmlLoadFileWriter` carve-out; `LoadFileOrchestrator` owns format dispatch for Standard + Production Set modes |
| `src/Profiles/` | Column profile system (loader, data generator, built-ins) |
| `src/Config/` | Nested request config records (`OutputConfig`, `MetadataConfig`, `LoadFileConfig`, `DelimiterConfig`, `BatesNumberConfig`, `TiffConfig`, `ChaosConfig`, `ProductionConfig`, `HashConfig`) + `HashUtility` |
| `src/Validation/` | Post-generation / Production Set / supplemental validators (`ValidationOrchestrator`, `PostGenerationValidator`, `ValidatorRunner`, `ProductionSetPostValidator`, `SupplementalValidator`) |
| `src/ManifestComparison/` | Production Manifest Comparison reports (`ProductionManifestComparer`, `--compare-production-manifests`) |
| `src/Utils/` | `NamingConventionHelper` |
| `src/LoadFileAuditWriter.cs` / `ProductionManifestWriter.cs` | Audit File + Production Manifest writers |
| `src/Zipper.Tests/` | Unit and property-based tests |
| `src/Zipper.Analyzers/` / `src/Zipper.Analyzers.Tests/` | Roslyn analyzers and analyzer unit tests |
| `tests/` | E2E test scripts |

Individual file generators (`EmlFileGenerator.cs`, `TiffFileGenerator.cs`, `OfficeFileGenerator.cs`, `PlaceholderFileGenerator.cs`) and `BatesSequence.cs` live in `src/` — grep by type.

---
