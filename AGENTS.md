# AI Agent Instructions for Zipper

## Principles

1. **Think before coding:** State assumptions, surface tradeoffs, ask if unclear. Fix root causes, not symptoms.
2. **Simplicity first:** No speculative features, no single-use abstractions, minimum code. YAGNI.
3. **Surgical changes:** Don't touch adjacent code, match existing style, no drive-by refactors. Output complete files — no placeholders or ellipses.
4. **Goal-driven execution:** Define verifiable success criteria before starting. Loop until met. Include error handling.

---

## Agent Behavior

### Tone
- Push back on bad ideas, unreasonable expectations, and mistakes. Do not be deferential.
- Flag when you don't know something. Stop and ask for clarification when uncertain.
- If you disagree, even on gut feeling, say so.

### Testing & Debugging
- NEVER test just mocked behavior. Tests must verify real outcomes.
- Fix all tests that fail, even if your change didn't break them.
- Always root cause bugs. Never fix just the symptom. Never implement a workaround.
- If you cannot find the root cause, stop and compile what you've learned.

**See also:** `.claude/skills/test-driven-development/SKILL.md`, `.claude/skills/testing-anti-patterns/SKILL.md`, `.claude/skills/systematic-debugging/SKILL.md`, `.claude/skills/root-cause-tracing/SKILL.md`

---

## How To Use This File

- This file is the agent workflow guide. Product behavior → [Requirements.md](Requirements.md). User-facing usage → [README.md](README.md). Domain terms → [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md).
- If an issue body, README.md, Requirements.md, and implementation disagree, stop and identify the conflict explicitly before coding. Do not silently choose one source.

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

# Lint
dotnet format --verify-no-changes src/   # Format check (run after every code change)

# Build + lint + test combo (run after every change)
dotnet build zipper.sln && dotnet format --verify-no-changes src/ && dotnet test src/Zipper.Tests/Zipper.Tests.csproj

# E2E (must pass before push)
./tests/run-tests.sh   # Linux/macOS
tests/run-tests.bat    # Windows
```

---

## Critical Rules

### IMPORTANT
**Domain Language:** Read and follow [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md) for all code, comments, documentation, and reviews. Grep the file before writing docs/PRs; flag non-canonical terms in review.

### CAUTION
**Requirement IDs are IMMUTABLE.** REQ-XXX and FR-XXX numbers must NEVER be changed or renumbered.

### CAUTION
**Test coverage must never decrease.** The only thing worse than a failing test is a reduction in test coverage. Fix failing tests — don't delete them. If a test is wrong, replace it with a correct one covering the same behavior. Never remove test files to make a test run green.

### IMPORTANT
**Documentation Sync:** Any change to CLI behavior, Load File/Audit File/Production Set formats, or Email domain names must update **all** of:
1. `README.md` — Arguments Quick Reference, Argument Interactions, examples
2. `Requirements.md` — add or revise requirements (never renumber)
3. `UBIQUITOUS_LANGUAGE.md` — if domain terms change
4. E2E scripts — both `.sh` and `.bat` for new coverage

Verify behavior changes against Requirements.md before committing. Run `grep -n "REQ-XXX" Requirements.md` for each affected requirement.

---

## Workflow for github issues

**Issue priority:** Blockers → Critical → High → Test Coverage → Design/Refactor/KISS (only after relevant test coverage exists). 

**Per-issue workflow:**
1. `git checkout main && git pull`
2. `git checkout -b fix/ISSUE-NNN-short-desc` (prefix: `fix/` for bugs, `feat/` for features, `refactor/`, `test/`, `docs/` per issue type)
3. Use Conventional Commits for commit messages (`fix:`, `feat:`, `refactor:`, `test:`, `docs:`, `chore:`, `deps:`)
4. Read the issue body; refresh labels, comments, and linked blockers before coding
5. Write a failing test first (TDD), then implement the fix
6. Run `dotnet format --verify-no-changes src/` and `dotnet test src/Zipper.Tests/Zipper.Tests.csproj` after every change
7. Run adversarial review before marking work complete (see Adversarial Review section below)
8. Commit and create PR
9. Monitor CI until all checks pass; fix failures before requesting review
10. Check SonarCloud issues on the PR after CI completes (see SonarCloud & External Checks below)

**Test location:** `src/Zipper.Tests/`.

**Pre-commit hook:** Runs lint + auto-format + unit tests on every `git commit`. Bypass: `git commit --no-verify`.

---

## Adversarial Review

Always use a subagent to perform adversarial review.

**Techniques:**

- **Fresh eyes:** "Look at this again with fresh eyes."
- **Subagent review:** Dispatch a review subagent with no knowledge of how the code was built.
- **Cross-model:** Use a different, if available, model for review than for coding.
- **Competition:** Ask two subagents to review the work. Tell them whomever finds the most serious issues gets five points (or a cookie). The reward details don't matter — the competitive framing does.
- **Set expectations:** "I'll be disappointed if they don't find at least N significant problems."

Run adversarial review before marking work as complete. Treat findings as bugs, not suggestions.

---

## SonarCloud & External Checks

SonarCloud issues are NOT surfaced as GitHub check failures — fetch manually (no auth required):

```bash
curl -s "https://sonarcloud.io/api/issues/search?componentKeys=dwojtaszek_zipper&pullRequest=NNN&statuses=OPEN,CONFIRMED&ps=50" | python3 -c "
import json,sys
data = json.load(sys.stdin)
for i in data['issues']:
    f = i['component'].split(':')[1]
    print(f'{i[\"severity\"]:10s} {i[\"rule\"]:25s} L{i[\"line\"]:4d}  {f}')
    print(f'  {i[\"message\"]}')
    print()
"
```

Issues ordered: `BLOCKER` → `MAJOR` → `MINOR` → `INFO`.

- **CodeRabbit**: address blocking issues, nitpicks optional.
- **factory-droid**: bot infra errors — retry, don't block merge.
- **CodeQL**: failures block merge — must fix.
- **Goldens**: `ZIPPER_CLI=$(pwd)/publish-bin/Zipper bash tests/goldens/run-goldens.sh --capture` to regenerate.

---

## Project Structure

**Solution:** `zipper.sln` — **Warnings as Errors** enabled.

| Path | Purpose |
|------|---------|
| `src/Program.cs` | Entry point, CLI orchestration, mode dispatch |
| `src/Cli/` | CLI parsing, validation, help text, request assembly |
| `src/FileGenerationRequest.cs` | Configuration root — 8 sub-configs (`Output`, `Metadata`, `LoadFile`, `Delimiters`, `Bates`, `Tiff`, `Chaos`, `Production`) + `LoadfileOnly` flag. Flat access guarded by `FGR_FLAT_ACCESS` analyzer |
| `src/IGenerationMode.cs` / `GenerationRunner.cs` | Mode interface + dispatcher (errors → exit codes) |
| `src/StandardMode.cs` / `LoadfileOnlyMode.cs` / `ProductionSetMode.cs` | Three generation mode adapters |
| `src/IFileGenerator.cs` / `FileGeneratorFactory.cs` | File generator interface + factory |
| `src/ParallelFileGenerator.cs` | Channel-based producer-consumer pipeline (standard mode) |
| `src/ZipArchiveService.cs` | Archive creation + Load File writing (consumer side) |
| `src/LoadfileOnlyGenerator.cs` | Standalone Load File generation (DAT/OPT) + Chaos |
| `src/ProductionSetGenerator.cs` | Production Set directory tree + Load Files |
| `src/ChaosEngine.cs` | Chaos Anomaly injection engine (Floyd's algorithm) |
| `src/Profiles/Generation/` | Column value generators — `IColumnValueGenerator`, per-Kind generators (see ADR-0004) |
| `src/Emails/` | Email domain model (`Email`, `EmailContext` records) |
| `src/LoadFiles/` | Load File writers (DAT, OPT, CSV, XML, Concordance) |
| `src/Profiles/` | Column profile system (loader, data generator, built-ins) |
| `src/LoadfileAuditWriter.cs` / `ProductionManifestWriter.cs` | Audit + manifest writers |
| `src/Zipper.Tests/` | Unit tests |
| `src/Zipper.Analyzers/` | Roslyn analyzer — `FGR_FLAT_ACCESS` diagnostic |
| `tests/` | E2E test scripts |

Individual file generators (`EmlFileGenerator.cs`, `TiffFileGenerator.cs`, `OfficeFileGenerator.cs`, `PlaceholderFileGenerator.cs`, `BatesNumberGenerator.cs`) live in `src/` — grep by type.

---

## Architecture

### Three Generation Modes

`Program.cs` uses `SelectMode(request)` → `IGenerationMode` → `GenerationRunner.RunAsync()` to dispatch to one of three strategies:

| Mode | Trigger | Adapter | Generator |
|------|---------|---------|-----------|
| **Standard** | default | `StandardMode` | `ParallelFileGenerator.GenerateFilesAsync()` → Archive (.zip) + Load File |
| **Loadfile-Only** | `--loadfile-only` | `LoadfileOnlyMode` | `LoadfileOnlyGenerator.GenerateAsync()` → Load File + `_properties.json` audit |
| **Production Set** | `--production-set` | `ProductionSetMode` | `ProductionSetGenerator.GenerateAsync()` → Directory tree (NATIVES/IMAGES/DATA/TEXT) + Load Files |

### Standard Pipeline

`ParallelFileGenerator` uses `System.Threading.Channels` for a 3-stage pipeline:

1. **Work channel**: Produces `FileWorkItem` objects using the configured distribution algorithm. Bounded channel provides backpressure.
2. **Generation**: N concurrent producers generate file data and write to result channel. Email with Attachments or Extracted Text forces `Concurrency = 1`.
3. **Archive writing**: Single consumer (`ZipArchiveService`) writes ZIP entries, then writes Load Files via `ILoadFileWriter` implementations.

### Chaos Engine (Loadfile-Only Mode only)

`ChaosEngine` uses Floyd's algorithm for O(k) exact random sampling of lines to corrupt. DAT types: `mixed-delimiters`, `quotes`, `columns`, `eol`, `encoding`. OPT types: `opt-boundary`, `opt-columns`, `opt-pagecount`, `opt-path`, `opt-batesid`. Tracked in `_properties.json` via `LoadfileAuditWriter`.

---


## Code Style

- C# 12 (net8.0), file-scoped namespaces, nullable reference types, switch expressions, pattern matching
- Distribution algorithms must be O(1) per file. Use `Span<T>`, `ArrayPool<T>`, avoid allocations in hot paths
- **No copyright headers** — do not add `// <copyright ...>` to any files
