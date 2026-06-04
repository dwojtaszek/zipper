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

**When rules conflict, Principles override Critical Rules.** Among Critical Rules, the order below determines priority — higher wins.

1. **Domain Language:** Read and follow [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md) for all code, comments, documentation, and reviews. Grep the file before writing docs/PRs; flag non-canonical terms in review. *Canonical terminology is non-negotiable — even when a non-canonical term seems simpler, Domain Language wins over Simplicity First.*

2. **Requirement IDs are IMMUTABLE.** REQ-XXX and FR-XXX numbers must NEVER be changed or renumbered.

3. **Test coverage must not decrease in substance.** Every removed or modified test must be replaced with a test that covers the same or a stricter behavioral contract. If no behavioral contract was being tested (e.g., a test that only asserted line execution), a behavior-coverage replacement is required. Never remove test files to make a test run green. *When this conflicts with Surgical Changes (e.g., fixing a pre-existing failing test requires touching adjacent code), Surgical Changes wins — but flag the conflict.*

4. **Documentation Sync:** Any change to CLI behavior, Load File/Audit File/Production Set formats, or Email domain names must update **all** of:
1. `README.md` — Arguments Quick Reference, Argument Interactions, examples
2. `Requirements.md` — add or revise requirements (never renumber)
3. `UBIQUITOUS_LANGUAGE.md` — if domain terms change
4. E2E scripts — both `.sh` and `.bat` for new coverage

Verify behavior changes against Requirements.md before committing. Run `grep -n "REQ-XXX" Requirements.md` for each affected requirement. *When this conflicts with Simplicity First (e.g., a trivial code change triggers a 4-doc update cascade), Simplicity First wins — but flag the conflict and note which docs are out of sync.*

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
7. Run autoreview before creating PR (see Adversarial Review section below). *Required for any change touching logic, error handling, or public contracts. For docs-only, version-bump, or single-line fixes, a self-review suffices — note the exemption in the PR.*
8. Commit and create PR
9. Monitor CI until all checks pass; fix failures before requesting review. If a CI failure appears flaky (same test passes locally, or failure is in an unrelated component), re-run once. If it fails again, document the flake in the PR and proceed to request review. Push fixes via `git commit --amend --no-edit && git push --force-with-lease`.
10. Check SonarCloud issues on the PR after CI completes (see [CI.md](CI.md#sonarcloud)). Fix all BLOCKER and MAJOR issues before merge
11. Address review comments from **all** bots/reviewers (CodeRabbit, Gemini Code Assist, Codex, SonarCloud, human). Blocking/major issues required, nitpicks optional. Bots post to three *separate* endpoints — you must query all three to discover every comment (the PR web view and `gh pr view` alone miss inline threads):
    - **Inline review comments** (code-anchored): `gh api repos/<owner>/<repo>/pulls/<N>/comments --paginate`
    - **Review summary bodies** (verdict + overview): `gh api repos/<owner>/<repo>/pulls/<N>/reviews --paginate`
    - **Issue-level PR comments** (CodeRabbit walkthrough, SonarCloud gate, perf guard): `gh api repos/<owner>/<repo>/issues/<N>/comments --paginate`

    For each finding: verify it against current code, fix if still valid, or skip with a brief reason (e.g. conflicts with an explicit design decision). If a suggestion contradicts a deliberate choice, reply on the thread explaining why rather than silently ignoring it.
12. Merge after all checks pass and reviews are addressed

**Test location:** `src/Zipper.Tests/`.

**Pre-commit hook:** Runs lint + auto-format + unit tests on every `git commit`. Bypass: `git commit --no-verify`.

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
| `src/FileGenerationRequest.cs` | Configuration root (8 sub-configs + `LoadfileOnly` flag) |
| `src/IGenerationMode.cs` / `GenerationRunner.cs` | Mode interface + dispatcher |
| `src/StandardMode.cs` / `LoadfileOnlyMode.cs` / `ProductionSetMode.cs` | Three generation mode adapters |
| `src/IFileGenerator.cs` / `FileGeneratorFactory.cs` | File generator interface + factory |
| `src/ParallelFileGenerator.cs` | Standard mode file generation pipeline |
| `src/ZipArchiveService.cs` | Archive creation + Load File writing |
| `src/LoadfileOnlyGenerator.cs` | Standalone Load File generation |
| `src/ProductionSetGenerator.cs` | Production Set directory tree + Load Files |
| `src/ChaosEngine.cs` | Chaos anomaly injection |
| `src/ChaosAnomalyTypes.cs` | Canonical anomaly type catalog |
| `src/ProductionSetPlanner.cs` | Production Set path/volume/bates planning |
| `src/LoadFiles/LoadFileRecord.cs` | Format-independent load file row model |
| `src/LoadFiles/ILoadFileSerializer.cs` | Format-specific record serialization interface |
| `src/LoadFiles/DatSerializer.cs` | DAT format serializer |
| `src/Profiles/Generation/` | Column value generators |
| `src/Emails/` | Email domain model |
| `src/LoadFiles/` | Load File writers (DAT, OPT, CSV, XML, Concordance) |
| `src/Profiles/` | Column profile system (loader, data generator, built-ins) |
| `src/LoadfileAuditWriter.cs` / `ProductionManifestWriter.cs` | Audit + manifest writers |
| `src/Zipper.Tests/` | Unit tests |
| `src/Zipper.Analyzers/` | Roslyn analyzers |
| `tests/` | E2E test scripts |

Individual file generators (`EmlFileGenerator.cs`, `TiffFileGenerator.cs`, `OfficeFileGenerator.cs`, `PlaceholderFileGenerator.cs`, `BatesNumberGenerator.cs`) live in `src/` — grep by type.

---

## Architecture

See [docs/architecture.md](docs/architecture.md) for mode dispatch, pipeline design, and component diagrams.

---


## Code Style

- C# 14 (net10.0), file-scoped namespaces, nullable reference types, switch expressions, pattern matching
- Distribution algorithms must be O(1) per file. Use `Span<T>`, `ArrayPool<T>`, avoid allocations in hot paths
- **No copyright headers** — do not add `// <copyright ...>` to any files

### Test Naming Convention

Test classes and methods follow the pattern:
- **Class:** `{Subject}Tests` (e.g., `BatesNumberGeneratorTests`, `ChaosEngineTests`)
- **Method:** `{Method}_{Scenario}_{Expected}` (e.g., `Generate_WithCustomPrefix_ShouldIncludePrefix`)

This convention is enforced by code review and the existing test corpus. All new tests must follow it.
