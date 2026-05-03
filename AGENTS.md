# AI Agent Instructions for Zipper

**Behavior:** Think step-by-step before coding. Output COMPLETE files (no placeholders/ellipses). Fix root causes, not symptoms. Match existing style. If unsure, ask for clarification – do not assume. Include error handling. Self-review for bugs/security. Suggest incremental changes over massive rewrites.

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

**Issue priority:** Blockers → Critical → High → Test Coverage → Design/Refactor/KISS (only after relevant test coverage exists). Issues tracked as GitHub issues with labels matching priority levels.

**Per-issue workflow:**
1. `git checkout main && git pull`
2. `git checkout -b fix/ISSUE-NNN-short-desc` (prefix: `fix/` for bugs, `feat/` for features, `refactor/`, `test/`, `docs/` per issue type)
3. Use [Conventional Commits](https://www.conventionalcommits.org/) for commit messages (`fix:`, `feat:`, `refactor:`, `test:`, `docs:`, `chore:`, `deps:`)
4. Read the issue body; refresh labels, comments, and linked blockers before coding
5. Write a failing test first (TDD), then implement the fix
6. Run `dotnet format --verify-no-changes src/` and `dotnet test src/Zipper.Tests/Zipper.Tests.csproj` after every change
7. Commit and create PR
8. Monitor CI until all checks pass; fix failures before requesting review

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
| `src/IGenerationMode.cs` | Mode interface — one RunAsync per generation strategy |
| `src/GenerationRunner.cs` | Dispatches IGenerationMode, handles errors → exit codes |
| `src/StandardMode.cs` | Standard mode adapter (wraps ParallelFileGenerator) |
| `src/LoadfileOnlyMode.cs` | Loadfile-Only mode adapter (wraps LoadfileOnlyGenerator) |
| `src/ProductionSetMode.cs` | Production Set mode adapter (wraps ProductionSetGenerator) |
| `src/IFileGenerator.cs` | File generator interface — one implementation per file type |
| `src/FileGeneratorFactory.cs` | Creates IFileGenerator by file type, eliminates string dispatch |
| `src/ParallelFileGenerator.cs` | Channel-based producer-consumer pipeline (standard mode) |
| `src/ZipArchiveService.cs` | Archive creation + Load File writing (consumer side) |
| `src/LoadfileOnlyGenerator.cs` | Standalone Load File generation (DAT/OPT) + Chaos |
| `src/ProductionSetGenerator.cs` | Production Set directory tree + Load Files |
| `src/ChaosEngine.cs` | Chaos Anomaly injection engine (Floyd's algorithm) |
| `src/MetadataRowBuilder.cs` | Assembles metadata rows from profiles + generation context |
| `src/LoadfileAuditWriter.cs` | `_properties.json` audit file writer |
| `src/ProductionManifestWriter.cs` | `_manifest.json` production manifest writer |
| `src/EmlGenerationService.cs` | Email Native File generation |
| `src/EmailBuilder.cs` | MIME construction for Email Native Files |
| `src/EmailTemplateSystem.cs` | Predefined email templates for test data generation |
| `src/EmlFileGenerator.cs` | EML IFileGenerator implementation |
| `src/PlaceholderFileGenerator.cs` | PDF/JPG/TIFF placeholder content generation |
| `src/TiffFileGenerator.cs` | TIFF IFileGenerator implementation with multi-page support |
| `src/TiffMultiPageGenerator.cs` | TIFF page count metadata + placeholder content (static helper) |
| `src/OfficeFileGenerator.cs` | DOCX/XLSX Native File generation |
| `src/OfficeFileGeneratorAdapter.cs` | Adapts OfficeFileGenerator to IFileGenerator |
| `src/PlaceholderFiles.cs` | Pre-computed byte content for PDF, JPG, TIFF |
| `src/BatesNumberGenerator.cs` | Bates Number generation |
| `src/LoadFiles/` | Load File writers (DAT, OPT, CSV, XML, Concordance) |
| `src/Profiles/` | Column profile system (loader, data generator, built-ins) |
| `src/Zipper.Tests/` | Unit tests |
| `tests/` | E2E test scripts |


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

- C# 12 (net8.0), file-scoped namespaces, nullable reference types, switch expressions, pattern matching
- Distribution algorithms must be O(1) per file. Use `Span<T>`, `ArrayPool<T>`, avoid allocations in hot paths
- **No copyright headers** — do not add `// <copyright ...>` to any files
