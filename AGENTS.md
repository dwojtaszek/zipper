# AI Agent Instructions for Zipper

**Behavior:** Think step-by-step before coding. Output COMPLETE files (no placeholders/ellipses). Fix root causes, not symptoms. Match existing style. If unsure, ask for clarification – do not assume. Include error handling. Self-review for bugs/security. Suggest incremental changes over massive rewrites.

---

## How To Use This File

- This file is the agent workflow guide. Product behavior → [Requirements.md](Requirements.md). User-facing usage → [README.md](README.md). Domain terms → [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md).
- If an issue body, README.md, Requirements.md, and implementation disagree, stop and identify the conflict explicitly before coding. Do not silently choose one source.

---

## Commands

```bash
dotnet restore zipper.sln                # Restore (after clone or adding packages)
dotnet build zipper.sln                  # Build
dotnet publish -c Release               # Publish
dotnet run --project src/Zipper.csproj -- [args]  # Run

# Tests
dotnet test src/Zipper.Tests/Zipper.Tests.csproj                              # Unit tests (must pass before commit)
dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~ClassName"  # Single test class

# Lint
dotnet format --verify-no-changes src/   # Quick lint (run after every code change)

# E2E (must pass before push)
./tests/run-tests.sh   # Linux/macOS
tests/run-tests.bat    # Windows
```

---

## Critical Rules

> [!IMPORTANT]
> **Domain Language:** Read and follow [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md) for all code, comments, documentation, and reviews. Grep the file before writing docs/PRs; flag non-canonical terms in review.

> [!CAUTION]
> **Requirement IDs are IMMUTABLE.** REQ-XXX and FR-XXX numbers must NEVER be changed or renumbered.

> [!IMPORTANT]
> **Documentation Sync:** Any change to CLI behavior, Load File/Audit File/Production Set formats, or Email domain names must update **all** of:
> 1. `README.md` — Arguments Quick Reference, Argument Interactions, examples
> 2. `Requirements.md` — add or revise requirements (never renumber)
> 3. `UBIQUITOUS_LANGUAGE.md` — if domain terms change
> 4. E2E scripts — both `.sh` and `.bat` for new coverage
>
> Verify behavior changes against Requirements.md before committing. Run `grep -n "REQ-XXX" Requirements.md` for each affected requirement.

---

## Workflow

**Issue priority:** Blockers → Critical → High → Test Coverage → Design/Refactor/KISS (only after relevant test coverage exists).

**Current dependency gate:** #198 (E2E golden fixtures + CI job) blocks most refactor work. Do not start design/refactor/KISS issues blocked on #198 until the golden CI job is active.

**Per-issue workflow:**
1. `git checkout main && git pull`
2. `git checkout -b fix/ISSUE-NNN-short-desc`
3. Read the issue body; refresh labels, comments, and linked blockers before coding
4. Write a failing test first (TDD), then implement the fix
5. Run `dotnet format --verify-no-changes src/` and `dotnet test src/Zipper.Tests/Zipper.Tests.csproj` after every change
6. Commit and create PR
7. Monitor CI until all checks pass; fix failures before requesting review

**Test location:** `src/Zipper.Tests/` (NOT the root `Zipper.Tests/` which is obsolete).

**Pre-commit hook:** Runs lint + auto-format + unit tests on every `git commit`. Bypass: `git commit --no-verify`.

---

## Project Structure

**Solution:** `zipper.sln` — **Warnings as Errors** enabled.

| Path | Purpose |
|------|---------|
| `src/Program.cs` | Entry point, CLI orchestration, mode dispatch |
| `src/Cli/` | CLI parsing, validation, help text, request assembly |
| `src/FileGenerationRequest.cs` | Configuration object (40+ properties) shared across pipeline |
| `src/ParallelFileGenerator.cs` | Channel-based producer-consumer pipeline (standard mode) |
| `src/ZipArchiveService.cs` | Archive creation + Load File writing (consumer side) |
| `src/LoadfileOnlyGenerator.cs` | Standalone Load File generation (DAT/OPT) + Chaos |
| `src/ProductionSetGenerator.cs` | Production Set directory tree + Load Files |
| `src/ChaosEngine.cs` | Chaos Anomaly injection engine (Floyd's algorithm) |
| `src/LoadfileAuditWriter.cs` | `_properties.json` audit file writer |
| `src/ProductionManifestWriter.cs` | `_manifest.json` production manifest writer |
| `src/EmlGenerationService.cs` | Email Native File generation |
| `src/EmailBuilder.cs` | MIME construction for Email Native Files |
| `src/OfficeFileGenerator.cs` | DOCX/XLSX Native File generation |
| `src/PlaceholderFiles.cs` | Pre-computed byte content for PDF, JPG, TIFF |
| `src/BatesNumberGenerator.cs` | Bates Number generation |
| `src/LoadFiles/` | Load File writers (DAT, OPT, CSV, XML, Concordance) |
| `src/Profiles/` | Column profile system (loader, data generator, built-ins) |
| `src/Zipper.Tests/` | Unit tests |
| `tests/` | E2E test scripts |


---

## Architecture

### Three Generation Modes

| Mode | Trigger | Entry Point | Output |
|------|---------|-------------|--------|
| **Standard** | default | `ParallelFileGenerator.GenerateFilesAsync()` | Archive (.zip) + Load File |
| **Loadfile-Only** | `--loadfile-only` | `LoadfileOnlyGenerator.GenerateAsync()` | Load File + `_properties.json` audit |
| **Production Set** | `--production-set` | `ProductionSetGenerator.GenerateAsync()` | Directory tree (NATIVES/IMAGES/DATA/TEXT) + Load Files |

### Standard Pipeline

`ParallelFileGenerator` uses `System.Threading.Channels` for a 3-stage pipeline:

1. **Work channel**: Produces `FileWorkItem` objects using the configured distribution algorithm. Bounded channel provides backpressure.
2. **Generation**: N concurrent producers generate file data and write to result channel. Email with Attachments or Extracted Text forces `Concurrency = 1`.
3. **Archive writing**: Single consumer (`ZipArchiveService`) writes ZIP entries, then writes Load Files via `ILoadFileWriter` implementations.

### Chaos Engine (Loadfile-Only Mode only)

`ChaosEngine` uses Floyd's algorithm for O(k) exact random sampling of lines to corrupt. DAT types: `mixed-delimiters`, `quotes`, `columns`, `eol`, `encoding`. OPT types: `opt-boundary`, `opt-columns`, `opt-pagecount`, `opt-path`, `opt-batesid`. Tracked in `_properties.json` via `LoadfileAuditWriter`.

---

## Key Invariants

- **Request immutability:** `FileGenerationRequest` must not be mutated after passing to a generator. Callers `Clone()` before modifying. `Clone()` is shallow — reference-type properties are shared.
- **MemoryOwner lifecycle:** Disposed before Load File writing. Writers access `Data.Length` only — accessing `.Span` after disposal is use-after-free.
- **Path separators:** Load File paths use `\` (eDiscovery convention). ZIP entries use `/` (ZIP spec).
- **Loadfile-Only scope:** Writes DAT or OPT only. Do not add CSV/XML Loadfile-Only scenarios without changing implementation + docs first.
- **Audit File schema:** camelCase throughout — `chaosMode.injectedAnomalies[*].errorType`, `lineNumber`, `recordID`. Not PascalCase, not snake_case.

---

## Code Style

```csharp
// Cross-platform ANSI encoding — MUST register at startup in Program.Main
System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Caller-owned streams: flush but don't dispose
await writer.FlushAsync();  // NOT: await writer.DisposeAsync();
```

- C# 8.0+, file-scoped namespaces, nullable reference types, switch expressions, pattern matching
- Distribution algorithms must be O(1) per file. Use `Span<T>`, `ArrayPool<T>`, avoid allocations in hot paths
- **No copyright headers** — do not add `// <copyright ...>` to any files
