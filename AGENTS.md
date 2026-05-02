# AI Agent Instructions for Zipper

**Behavior:** Think step-by-step before coding. Output COMPLETE files (no placeholders/ellipses). Fix root causes, not symptoms. Match existing style. If unsure, ask for clarification – do not assume. Include error handling. Self-review for bugs/security. Suggest incremental changes over massive rewrites. **All discussions, documentation, and code must use the ubiquitous language terms defined in UBIQUITOUS_LANGUAGE.md.**

---

## Ubiquitous Language

**REQUIRED:** Read and follow [UBIQUITOUS_LANGUAGE.md](UBIQUITOUS_LANGUAGE.md) for all discussions, documentation, and code reviews. Use canonical terms (Archive, Native File, Load File, Bates Number, etc.) — the file defines each with aliases to avoid.

**Usage:** grep the file before writing docs/PRs; flag non-canonical terms in review.

---


## Commands

```bash
# Restore (run after clone or adding packages)
dotnet restore zipper.sln

# Build
dotnet publish -c Release

# Run
dotnet run --project src/Zipper.csproj -- [args]

# Unit Tests (must pass before commit)
dotnet test src/Zipper.Tests/Zipper.Tests.csproj

# Single test class
dotnet test src/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~ClassName"

# Lint (runs automatically before every commit via pre-commit hook)
dotnet format --verify-no-changes

# Quick lint (run after every code change before staging)
dotnet format --verify-no-changes src/

# E2E Tests (must pass before push - enforced by git hooks)
./tests/run-tests.sh   # Linux/macOS
tests/run-tests.bat    # Windows
```

---

## Critical Rules

> [!IMPORTANT]
> **Domain Language Mandatory**: Refer to UBIQUITOUS_LANGUAGE.md when discussing any Zipper concept. Use canonical terms (Archive, Native File, Load File, Metadata, Folder, Volume, Email, Attachment, Bates Number, Chaos Engine, Loadfile-Only Mode) consistently in all code, comments, and documentation.

> [!CAUTION]
> **Requirement numbers in Requirements.md are IMMUTABLE.** REQ-XXX and FR-XXX numbers must NEVER be changed or renumbered.

> [!IMPORTANT]
> **CLI Documentation Sync**: When modifying CLI args in `CommandLineValidator.cs`, update:
> 1. `README.md` - Arguments Quick Reference table
> 2. `Requirements.md` - Add new requirements
> 3. This file - if adding major features

## Issue Priority Order

**All open issues:** https://github.com/dwojtaszek/zipper/issues

Tackle in this order: **Blockers** → **Critical** → **High** → **Test Coverage** (fill gaps before refactoring) → **Design** (D1-D6, only after test coverage exists).

**Workflow per issue:**
1. `git checkout main && git pull`
2. `git checkout -b fix/ISSUE-NNN-short-desc`
3. Read the issue body for file paths, line numbers, and fix guidance
4. Write a failing test first (TDD), then implement the fix
5. **Run lint + tests after EVERY code change:**
   - `dotnet format --verify-no-changes src/` — quick lint check
   - `dotnet test src/Zipper.Tests/Zipper.Tests.csproj --no-build` — quick test (if build hasn't changed)
   - `dotnet test src/Zipper.Tests/Zipper.Tests.csproj` — full test + build (after structural changes)
6. Commit and create PR
7. **Monitor CI after PR:** After creating or updating a PR, monitor GitHub CI status until all checks pass. If CI fails, diagnose and fix before requesting review.

## Requirement-Driven Changes

> [!IMPORTANT]
> **Every code change that alters behavior MUST be verified against Requirements.md and README.md.**
> 
> **Before committing:**
> 1. Identify which REQ-XXX requirements are affected by the change
> 2. Check if the change violates any existing requirement — if so, flag it before implementing
> 3. If a requirement must change, update the requirement text (never change REQ numbers — they are IMMUTABLE per the CAUTION block above)
> 4. Update README.md if the change affects: CLI arguments, format specifications, behavior descriptions, the Arguments Quick Reference table, or the Argument Interactions section
> 5. Run `grep -n "REQ_XXX" Requirements.md` to verify all referenced requirements still exist
> 
> **Review checklist for code review:**
> - [ ] Code behavior matches Requirements.md (no violations)
> - [ ] CLI argument changes reflected in README.md Arguments Quick Reference table
> - [ ] CLI argument changes reflected in Requirements.md argument descriptions
> - [ ] Format changes align with Section 8 (Load File Format Standards)
> - [ ] New options documented in README.md examples section
> - [ ] Argument interactions updated in both README.md and Requirements.md Section 10

**Test Location:** All unit tests go in `src/Zipper.Tests/` (NOT the root `Zipper.Tests/` which is obsolete).

**Pre-commit Hook:** The `.git/hooks/pre-commit` hook runs `dotnet format --verify-no-changes`, auto-formats staged C# files, then runs unit tests on every `git commit`. To bypass: `git commit --no-verify`.

**E2E Tests:** Must verify actual output (file counts, headers, content). All new E2E tests need both `.sh` and `.bat` implementations.

---

## Project Structure

**Solution:** `zipper.sln` (build with `dotnet build zipper.sln`)  
**Warnings as Errors:** Enabled - fix all warnings before commit

| Path | Purpose |
|------|---------|
| `src/Zipper.csproj` | Main application |
| `src/Program.cs` | Entry point, CLI orchestration, mode dispatch |
| `src/CommandLineValidator.cs` | CLI parsing, validation, request assembly |
| `src/FileGenerationRequest.cs` | Configuration object (40+ properties) shared across pipeline |
| `src/ParallelFileGenerator.cs` | Channel-based producer-consumer pipeline (standard mode) |
| `src/ZipArchiveService.cs` | Archive creation + Load File writing (consumer side) |
| `src/LoadfileOnlyGenerator.cs` | Standalone Load File generation (DAT/OPT) + Chaos |
| `src/ProductionSetGenerator.cs` | Production set directory tree + Load Files |
| `src/ChaosEngine.cs` | Chaos Anomaly injection engine (Floyd's algorithm) |
| `src/ChaosAnomaly.cs` | Anomaly record for audit tracking |
| `src/ChaosScenario.cs` | Named, reproducible Chaos configurations |
| `src/LoadfileAuditWriter.cs` | `_properties.json` audit file writer |
| `src/ProductionManifestWriter.cs` | `_manifest.json` production manifest writer |
| `src/OfficeFileGenerator.cs` | DOCX/XLSX Native File generation |
| `src/EmlGenerationService.cs` | Email Native File generation |
| `src/EmailBuilder.cs` | MIME construction for Email Native Files |
| `src/EmailTemplateSystem.cs` | Email template data (categories, bodies, addresses) |
| `src/PlaceholderFiles.cs` | Pre-computed byte content for PDF, JPG, TIFF |
| `src/FileDistributionHelper.cs` | Distribution facade (proportional/gaussian/exponential) |
| `src/TiffMultiPageGenerator.cs` | TIFF page range parsing and generation |
| `src/BatesNumberGenerator.cs` | Bates Number generation |
| `src/PathValidator.cs` | Path traversal protection |
| `src/EncodingHelper.cs` | Encoding name resolution (UTF-8, UTF-16, ANSI) |
| `src/ContentTypeHelper.cs` | MIME type mapping by extension |
| `src/PerformanceMonitor.cs` | Real-time progress, throughput, ETA |
| `src/PerformanceBenchmarkRunner.cs` | Built-in benchmark suite |
| `src/MemoryPoolManager.cs` | `MemoryPool<byte>.Shared` wrapper |
| `src/LoadFiles/` | Load File writers (DAT, OPT, CSV, XML, Concordance) |
| `src/Profiles/` | Column profile system (loader, data generator, built-ins) |
| `src/Zipper.Tests/` | Unit tests |
| `tests/` | E2E test scripts |

**For full CLI arguments and options:** See [README.md](README.md)  
**For requirements and specifications:** See [Requirements.md](Requirements.md)

---

## Architecture

### Three Generation Modes

`Program.Main` dispatches to one of three paths based on CLI flags:

| Mode | Trigger | Entry Point | Output |
|------|---------|-------------|--------|
| **Standard** | default | `ParallelFileGenerator.GenerateFilesAsync()` | Archive (.zip) + Load File |
| **Loadfile-Only** | `--loadfile-only` | `LoadfileOnlyGenerator.GenerateAsync()` | Load File + `_properties.json` audit |
| **Production Set** | `--production-set` | `ProductionSetGenerator.GenerateAsync()` | Directory tree (NATIVES/IMAGES/DATA/TEXT) + Load Files |

**Which mode to use?**

| If you need... | Use... | Flag |
|----------------|--------|------|
| A single Archive with Load File | Standard | (default, no flag needed) |
| Only a Load File, no Archive | Loadfile-Only | `--loadfile-only` |
| Structured production set (NATIVES/IMAGES/DATA/TEXT Folders) with cross-referenced Load Files | Production Set | `--production-set` (requires `--bates-prefix`) |
| Chaos anomaly injection | Loadfile-Only + Chaos | `--loadfile-only --chaos-mode` |
| Output wrapped in Archive | Standard (or Production Set + `--production-zip`) | |

### Standard Pipeline (Channel-Based Producer-Consumer)

`ParallelFileGenerator` uses `System.Threading.Channels` for a 3-stage pipeline:

1. **Work channel** (`CreateWorkChannel`): Produces `FileWorkItem` objects (Folder assignment, file index) using the configured distribution algorithm. Bounded channel provides backpressure.
2. **Generation** (`ProcessFileWorkAsync`): N concurrent producers read from work channel, call `GenerateFileData()` (placeholder content + optional padding), write `FileData` to result channel. Email with Attachments or Extracted Text forces `Concurrency = 1`.
3. **Archive writing** (`ZipArchiveService.CreateArchiveAsync`): Single consumer reads from result channel, writes ZIP entries sequentially, accumulates `processedFiles` list, then writes Load Files via `ILoadFileWriter` implementations.

Memory: `MemoryPool<byte>.Shared` rented via `IMemoryOwner<byte>` for normal files, direct allocation fallback for oversized. Padding uses `RandomNumberGenerator.Fill` for non-compressible data.

### Load File Writers

`LoadFiles/` directory: `ILoadFileWriter` interface + `LoadFileWriterBase` abstract class + `LoadFileWriterFactory`. Each format is a subclass:

- `DatWriter` (in `LoadFileWriterFactory.cs`) — Concordance DAT with configurable delimiters
- `OptWriter` — Opticon comma-delimited
- `CsvWriter` — Standard CSV
- `XmlLoadFileWriter` — EDRM-XML
- `ConcordanceWriter` — Concordance DAT with standard delimiters

**Which format to use?**

| If consumer uses... | Use... | CLI value | Notes |
|---------------------|--------|-----------|-------|
| Concordance (standard) | DAT | `dat` | Configurable delimiters via `--dat-delimiters` / `--delimiter-*` |
| Opticon | OPT | `opt` | Comma-delimited, no-header format |
| Standard CSV | CSV | `csv` | Comma-separated with header |
| EDRM XML | XML | `edrm-xml` | XML schema per EDRM standard |
| Concordance (legacy fixed delimiters) | Concordance | `concordance` | Uses `þ` (254) / `Þ` (222) fixed delimiters |
| Multiple outputs | Any combination | `--load-file-formats dat,opt,csv` | Comma-separated list |

### Chaos Engine (Loadfile-Only Mode only)

`ChaosEngine` intercepts Load File lines using Floyd's algorithm for O(k) exact random sampling of lines to corrupt. Supports DAT Anomaly types (`mixed-delimiters`, `quotes`, `columns`, `eol`, `encoding`) and OPT types (`opt-boundary`, `opt-columns`, `opt-pagecount`, `opt-path`, `opt-batesid`). Injected Anomalies tracked in `_properties.json` via `LoadfileAuditWriter`.

### Distribution Algorithms

`ProportionalDistribution`, `GaussianDistribution`, `ExponentialDistribution` — static classes. `FileDistributionHelper` facade. Each is O(1) per file.

### Column Profile System

`Profiles/` directory: `ColumnProfileLoader` validates and loads JSON profiles (built-in or custom file). `DataGenerator` produces column values by type. `BuiltInProfiles` embeds minimal/standard/litigation/full definitions in C#.

---

## Key Invariants

- **Request immutability:** `FileGenerationRequest` must not be mutated after passing to a generator. Used across concurrent tasks. Callers `Clone()` before modifying. `Clone()` is shallow via `MemberwiseClone` — reference-type properties (ColumnProfile, BatesConfig, LoadFileFormats) are shared between original and clone.
- **MemoryOwner lifecycle:** `MemoryOwner.Dispose()` in `ZipArchiveService:73` happens before Load File writing. Load File writers access `Data.Length` only — accessing `.Span` after disposal is use-after-free (issue #132).
- **Path separators:** Load File paths use backslash `\` (eDiscovery convention). ZIP entry paths use forward slash `/` (ZIP spec). `ProductionSetGenerator` converts via `.Replace(Path.DirectorySeparatorChar, '\\')`.
- **Loadfile-Only vs standard metadata:** `LoadfileOnlyGenerator` and the standard `DatWriter` use separate metadata generation paths. Same seed + same count can produce different metadata rows.

---

## Code Patterns

```csharp
// Email forces sequential processing with attachments
if (request.FileType == "eml" && (request.WithText || request.AttachmentRate > 0))
    request.Concurrency = 1;

// Cross-platform ANSI encoding — MUST register at startup in Program.Main
System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// Caller-owned streams: flush but don't dispose
await writer.FlushAsync();  // NOT: await writer.DisposeAsync();
```

**Performance:** Distribution algorithms must be O(1) per file. Use `Span<T>`, `ArrayPool<T>`, avoid allocations in hot paths.

**Style:** C# 8.0+, file-scoped namespaces, nullable reference types, switch expressions, pattern matching.

**No Copyright Headers:** Do NOT add copyright, license, or file header comments (e.g. `// <copyright ...>`) to any files. This project does not use file-level copyright headers.
