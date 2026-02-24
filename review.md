# Production Readiness Code Review — Zipper

**Reviewer**: Staff SWE, Production Readiness  
**Date**: 2026-02-24  
**Branch**: `review/code-audit-2026-02-24`  
**Scope**: All files in `src/` (38 source files, ~4,300 LOC)

---

## Executive Summary

Zipper is a CLI tool that generates synthetic ZIP archives with load files for legal e-discovery software. The codebase is reasonably well-structured for a project of this size, with clear separation of concerns (parallel generation, archive creation, load file writing, profile-based data generation). However, **it is not production-ready for scale** (millions of users / large datasets) and exhibits several correctness, security, concurrency, and reliability issues that range from critical to moderate.

**Verdict: NOT READY for production at scale.** The most urgent issues are around memory safety (unbounded allocations), non-deterministic output in load files (using `DateTime.Now` and `Random.Shared` in parallel contexts), silent data loss on argument parsing failures, and path traversal bypass potential.

---

## Risk Matrix

### 🔴 Critical (P0 — fix before any production use)

| # | File | Issue |
|---|------|-------|
| C1 | `ParallelFileGenerator.cs` | **Integer overflow**: `(int)Math.Min(totalSize, MaxPoolSize)` overflows when `totalSize > int.MaxValue`. `paddingPerFile` is `long`, content can be 100MB+ per file. `totalSize` can exceed 2GB → `(int)` cast wraps to negative → `Rent(-N)` returns `null` → fallback allocates `new byte[totalSize]` which throws `OverflowException` since `new byte[]` takes `int`. |
| C2 | `ParallelFileGenerator.cs` | **Unbounded memory**: `ConcurrentBag<FileData> processedFiles` in `ZipArchiveService` holds all generated files in memory. For 1M files × ~300B placeholder = ~300MB minimum; with padding or large files, easily OOM. No streaming/batching. |
| C3 | `LoadFileGenerator.cs` | **Non-deterministic metadata**: `DateTime.Now` and `Random.Shared.Next()` are called per-row. In parallel context, `Random.Shared` is thread-safe but `DateTime.Now` produces different values per call. Load file metadata (dates, authors) is non-reproducible even with `--seed`. |
| C4 | `PathValidator.cs` | **Path traversal bypass**: The `..` check happens on the raw string, but `Path.GetFullPath()` has already resolved it. A path like `foo/..%2f..%2fetc/passwd` or OS-specific tricks may bypass. More critically, the validator checks `normalizedPath.Contains("..")` which fails for paths like `C:\Users\admin..backup\data` (legitimate path with `..` substring). |
| C5 | `CommandLineValidator.cs` | **Silent argument swallowing**: If `--count` is followed by a non-numeric value (e.g., `--count abc`), `++i` increments but `TryParse` fails silently — `parsed.Count` stays `null`, and the next argument is lost. No error is shown. |

### 🟠 High (P1 — fix within first sprint)

| # | File | Issue |
|---|------|-------|
| H1 | `ZipArchiveService.cs` | **No error handling on write failures**: If `WriteFileToArchive` throws (e.g., disk full), the `ConcurrentBag` still has partial data but `MemoryOwner` disposal loop at the end won't reach it. Memory leak + corrupt output. |
| H2 | `ParallelFileGenerator.cs` | **Channel producer fire-and-forget**: `CreateWorkChannel` spawns `Task.Run(async () => ...)` but the `Task` is never awaited. If it throws, the exception is swallowed. The `writer.Complete()` may never be called → consumer hangs forever. |
| H3 | `LoadFileGenerator.cs` + `LoadFileWriterBase.cs` | **Duplicate logic**: Metadata generation (`GetMetadataColumns`, `GenerateMetadataValues`) is duplicated between `LoadFileGenerator` (DAT) and `LoadFileWriterBase` (CSV/OPT/XML/Concordance). Values won't match between formats for the same run. |
| H4 | `EmailTemplateSystem.cs` | **Unreferenced template placeholders**: Support templates use `{description}`, `{solution}`, `{priority}` which are never defined in `BuildReplacements()`. These appear literally in output emails, producing invalid test data. |
| H5 | `FileGenerationRequest.cs` | **Mutable DTO in concurrent context**: `FileGenerationRequest` is a mutable class passed to multiple threads. `ParallelFileGenerator.GenerateFilesAsync` mutates `request.Concurrency` (line 34-41). Race condition if same request object is reused. |
| H6 | `PerformanceMonitor.cs` | **Race condition on progress**: `lastProgressUpdate` and `lastDisplayedPercentage` are read/written concurrently without synchronization. `DateTime.UtcNow` comparison with non-volatile field can skip updates or double-update. |
| H7 | `OfficeFileGenerator.cs` | **Identical output for all files**: `GenerateDocx()` and `GenerateXlsx()` return the exact same static byte array for every file. All DOCX/XLSX files in the ZIP are bit-identical. Load file shows different metadata for each, but the actual file content is the same — discoverable by any review tool. |

### 🟡 Medium (P2 — fix within quarter)

| # | File | Issue |
|---|------|-------|
| M1 | `Program.cs` | **No exit code for generation failures**: `GenerateFiles` catches all exceptions and writes to stderr but `Main` always returns `0` after calling `GenerateFiles`. Should propagate failure. |
| M2 | `CommandLineValidator.cs` | **No unknown argument detection**: Unknown flags (e.g., `--typo`) are silently ignored. No warning, no error. At scale, misconfiguration goes unnoticed. |
| M3 | `ZipArchiveService.cs` | **`ConcurrentBag.ToList()` on hot path**: Called twice (lines 87 and 98). `ConcurrentBag.ToList()` takes a lock and copies all items. For 1M files, this is ~2 extra O(n) copies. |
| M4 | `PlaceholderFiles.cs` | **Missing namespace**: Only file without a namespace declaration (`public static class PlaceholderFiles`). This is in the global namespace, violating project conventions and potentially causing name collisions. |
| M5 | `EmailBuilder.cs` | **StringBuilder allocation**: Every EML file builds the entire content in a `StringBuilder` then converts to `byte[]`. For large attachment content, this means the full base64 string is held in memory as both `char[]` (in StringBuilder) and `byte[]` (UTF8 encoded). Peak memory usage is ~3x the attachment size per file. |
| M6 | `BatesNumberGenerator.cs` | **No overflow protection**: `CalculateValue` computes `config.Start + (currentIndex * config.Increment)`. For large indices and increments, this silently overflows `long`. |
| M7 | All load file writers | **Per-row async writes**: Every row calls `await writer.WriteLineAsync()`. For large files, this creates millions of state machine allocations. Should use buffered writes with periodic flush. |
| M8 | `DataGenerator.cs` | **Non-thread-safe**: Uses `this.documentIndex++` without synchronization. If `GenerateRow` is called from multiple threads simultaneously (which the current code doesn't do, but the API allows), data corruption occurs. |
| M9 | `BuiltInProfiles.cs` | **Expression-bodied property creates new instance per access**: `Minimal`, `Standard`, `Litigation`, `Full` all use `=> new()` — every property access creates a new `ColumnProfile` with new `List<ColumnDefinition>` allocations. Should be `{ get; } = CreateXxx()` or lazy-init. |
| M10 | `TiffMultiPageGenerator.cs` | **New Random per file**: `new System.Random(seed)` allocates a new `Random` for every TIFF file. For 1M files, that's 1M Random objects. Use a cached or pooled approach. |

### 🟢 Low (P3 — tech debt backlog)

| # | File | Issue |
|---|------|-------|
| L1 | `PerformanceBenchmarkRunner.cs` | Benchmark results are printed but not validated programmatically. The "PASS/FAIL" is cosmetic. No regression detection. |
| L2 | `ContentTypeHelper.cs` | Missing TIFF, DOCX/XLSX OOXML, and EML content types. Falls back to `application/octet-stream`. |
| L3 | `MemoryPoolManager.cs` | Wrapper around `MemoryPool<byte>.Shared` adds no value. `Dispose()` is a no-op. Could be inlined. |
| L4 | `EncodingHelper.cs` | ANSI hard-coded to codepage 1252. On non-Windows systems, this may not be the expected ANSI encoding. |
| L5 | `SizeMultipliers` | Missing TB multiplier. `1024 * 1024 * 1024` as `long` literal is fine but would overflow as `int` multiplied inline. |
| L6 | `ConcordanceWriter.cs` | BEGATTY/ENDDATTY fields are always empty — produces `,,...` at line start. Functionally suspicious for Concordance import. |
| L7 | `LoadFileFormat.cs` | `Xml` and `EdrmXml` both map to same `XmlLoadFileWriter` — no differentiation. Misleading API. |

---

## File-by-File Findings

### Core Pipeline

#### `Program.cs` (90 lines)
**Purpose**: Entry point. Parses CLI args, dispatches to benchmark or file generation.

- **Correctness**: `GenerateFiles` swallows exceptions (writes to stderr) but `Main` returns `0`. This means CI or scripts won't detect failures.
- **API Design**: `GenerateFiles` is `private static async Task` — untestable without invoking `Main`. Should be extracted to a service.
- **Readability**: `string.Format` used alongside `$""` interpolation inconsistently.

#### `CommandLineValidator.cs` (730 lines)
**Purpose**: Parses and validates all CLI arguments.

- **Correctness**: Silent argument swallowing (see C5). When `--count` gets a non-numeric next arg, the value is consumed but not parsed.
- **Security**: No length limits on string arguments. A crafted `--bates-prefix` with 10MB string would be accepted.
- **API Design**: Monolithic 730-line static class. Should be broken into parsing, validation, and request building.
- **Readability**: `ParsedArguments` inner class is good, but defaults are duplicated between it and `FileGenerationRequest`.
- **Test Gap**: No tests for unknown arguments, no tests for arguments without values at end of array (e.g., `--type` as last arg).

#### `ParallelFileGenerator.cs` (310 lines)
**Purpose**: Orchestrates parallel file generation with channel-based producer-consumer pattern.

- **Concurrency**: `CreateWorkChannel` spawns an unawaited `Task.Run`. If the producer throws, the consumer hangs on `ReadAllAsync()`.
- **Memory**: `GenerateFileData` casts `long` to `int` for pool rental (C1). Padding can exceed `int.MaxValue`.
- **Performance**: Semaphore pattern is correct but `ProcessFileWorkAsync` awaits the semaphore then immediately does sync work. The semaphore doesn't actually gate memory — it gates task count. Should gate based on memory pressure instead.
- **API Design**: `FileWorkItem` and `FileData` are `internal record` defined in this file — should be in separate files.

#### `ZipArchiveService.cs` (176 lines)
**Purpose**: Creates ZIP archive from channel of file data.

- **Correctness**: `ConcurrentBag<FileData>` is used but all items are added sequentially (single consumer). A `List<FileData>` would be simpler and faster.
- **Memory**: All `FileData` held in memory until load file is written. For large runs, this is the OOM bottleneck.
- **Performance**: `processedFiles.ToList()` called twice — once for load file writing, once for disposal. Should convert once.

#### `LoadFileGenerator.cs` (173 lines)
**Purpose**: Writes DAT format load files (legacy, used by `DatWriter`).

- **Correctness**: Non-deterministic metadata (C3). `Random.Shared.Next()` and `DateTime.Now` produce different values each run regardless of `--seed`.
- **API Design**: Should be merged with `LoadFileWriterBase` or removed (it's only called by `DatWriter`).
- **Performance**: Per-row `StringBuilder` + per-row `await WriteLineAsync`. Both are allocation-heavy.

### EML Pipeline

#### `EmailTemplateSystem.cs` (479 lines)
**Purpose**: Template-based email content generation across 12 categories.

- **Correctness**: `SupportTemplates` use placeholders `{description}`, `{solution}`, `{priority}` not in `BuildReplacements()` — they appear literally in generated emails.
- **Performance**: `BuildReplacements()` is called twice per email (once for subject, once for body) — each creates a new `Dictionary` with 30+ entries. Should build once.
- **API Design**: `GenerateEmailAddress` is `public static` but uses `ToLowerInvariant()` with no validation. Index overflow on `EmailDomains` array is clamped by modulo (fine).

#### `EmailBuilder.cs` (272 lines)
**Purpose**: Constructs RFC 2822 EML files with MIME multipart support.

- **Correctness**: The chunked base64 encoding is well-implemented with `ArrayPool` usage. Good.
- **Performance**: The `StringBuilder` approach means the entire EML (including base64 of attachments) is built as a string in memory before conversion. For multi-MB attachments, this is expensive.
- **Readability**: Clean separation between headers, simple content, and multipart content. Boundary generation uses GUID, which is correct.

#### `EmlGenerationService.cs` (116 lines)
**Purpose**: Orchestrates EML generation with template selection and attachment handling.

- **API Design**: Two overloads (config record vs. raw parameters) — the raw-param overload exists only for backward compat. The config record approach is cleaner but under-used.

### File Generators

#### `OfficeFileGenerator.cs` (144 lines)
**Purpose**: Pre-computes minimal DOCX/XLSX documents at static init.

- **Correctness**: Every generated DOCX and XLSX file is byte-identical (H7). The `FileWorkItem` parameter is accepted but ignored. This means hash-based deduplication in review tools will flag all files as duplicates.
- **API Design**: `IsOfficeFormat` does not include PPTX even though there's a `throw NotImplementedException` for it. Inconsistent.

#### `TiffMultiPageGenerator.cs` (88 lines)
**Purpose**: Returns pre-computed TIFF files with metadata-only page counts.

- **Correctness**: `Generate()` ignores the `pageCount` parameter — always returns the same single-page TIFF. The comment says "for metadata purposes only" but this is misleading — the load file claims different page counts.
- **Performance**: `new System.Random(seed)` per call (M10).

#### `PlaceholderFiles.cs` (100 lines)
**Purpose**: Static byte arrays for placeholder PDF, JPG, TIFF content.

- **Missing Namespace**: Only file in the project without a namespace — lives in global namespace (M4).
- **API Design**: `GetContent` returns `Array.Empty<byte>()` for unknown types — this is then passed to `Buffer.BlockCopy` which succeeds with 0 length. Not a bug but confusing.
- **Correctness**: `GetRandomAttachment()` creates a new `List<string>(FileContentMap.Keys)` per call. Should be cached.

### Load File Writers

#### `LoadFileWriterBase.cs` (151 lines)
**Purpose**: Abstract base class with shared column-building logic.

- **Correctness**: `GenerateMetadataValues` uses `Random.Shared.Next()` and `DateTime.Now` — same non-determinism issue as `LoadFileGenerator` (C3). Different values generated than `LoadFileGenerator` for the same file.
- **API Design**: Good use of template method pattern. `EscapeCsvField` correctly implements RFC 4180.

#### `CsvWriter.cs`, `OptWriter.cs`, `ConcordanceWriter.cs`, `XmlLoadFileWriter.cs`
**Purpose**: Format-specific load file writers.

- **Correctness**: CSV headers are not escaped. Column names containing commas would break the header row.
- **Performance**: All writers iterate with per-row `await WriteLineAsync()`. For 1M rows, this creates ~1M task state machine allocations.
- **API Design**: `ConcordanceWriter` and `DatWriter` both use `.dat` extension — only distinguishable by an explicit `--load-file-format` flag that most users won't set.
- **Specific to XmlLoadFileWriter**: Good use of `XmlWriter` with async settings. Exception handling wraps arbitrary exceptions in `InvalidOperationException` — loses stack trace type information.

### Profiles

#### `ColumnProfile.cs` (256 lines), `ColumnProfileLoader.cs` (167 lines), `BuiltInProfiles.cs` (350 lines), `DataGenerator.cs` (631 lines)
**Purpose**: Column profile system for customizable load file schemas with data generation.

- **Correctness**: `BuiltInProfiles` uses expression-bodied properties (`=> new()`) — every access allocates new objects (M9). If `Full` profile is accessed 3 times, 3 full column lists with 150+ entries are allocated.
- **Security**: `ColumnProfileLoader.LoadFromFile` reads arbitrary JSON from user-specified path. No file size limit. A 10GB JSON file would be read entirely into memory.
- **API Design**: `DataGenerator` is not thread-safe (M8). `documentIndex` is incremented without locks. This is fine since it's currently used single-threaded in load file writers, but the API doesn't communicate this constraint.
- **Performance**: `PrecomputeDistributionIndices` pre-computes 1000-entry arrays — good optimization. `GenerateHash` uses `stackalloc` — good.

### Utilities

#### `PathValidator.cs` (95 lines)
- **Security**: Path traversal check is bypassable (C4). The `..` substring check is both over-restrictive (blocks legitimate paths containing `..`) and under-restrictive (doesn't handle all traversal vectors). Should use canonical path comparison instead.
- **Side Effect**: `IsPathSafe` calls `ValidateAndCreateDirectory` which writes to `Console.Error` — a validation method should not have console output side effects.

#### `FileDistributionHelper.cs`, `GaussianDistribution.cs`, `ExponentialDistribution.cs`, `ProportionalDistribution.cs`
- **Correctness**: Well-implemented distribution algorithms with proper clamping. `GaussianDistribution.InverseNormalCDF` uses the Beasley-Springer-Moro algorithm — correct implementation.
- **Test Coverage**: Distribution algorithms are well-tested.

#### `PerformanceMonitor.cs` (142 lines)
- **Concurrency**: `lastProgressUpdate` and `lastDisplayedPercentage` are read/written from multiple threads without synchronization (H6).
- **API Design**: `Console.Write` with `\r` for progress — hard-coded to console output. Not testable, not redirectable.

#### `PerformanceConstants.cs` (31 lines)
- `DefaultConcurrency = Math.Max(1, Environment.ProcessorCount / 2)` — reasonable default. Using `readonly` instead of `const` is correct since it reads `Environment.ProcessorCount`.

---

## Architectural Concerns

### 1. Memory Model — Unbounded Growth
The pipeline collects ALL generated file data in `ConcurrentBag<FileData>` before writing load files. This means peak memory = sum of all file content. For 100K files × 1MB each = 100GB RAM required. **This is the #1 scaling bottleneck.**

**Fix**: Stream load file data as files are written to the archive. Track only the metadata needed for load files (doc ID, folder, page count, attachment name), not the full file content.

### 2. Non-Deterministic Output
Despite having a `--seed` option, the actual output is non-deterministic because:
- `DateTime.Now` is used for metadata dates (changes between runs)
- `Random.Shared` is used in load file writers (not controlled by seed)
- Template system uses `Random.Shared` for category selection

The seed only controls `DataGenerator` in profile mode. The base metadata path (`LoadFileGenerator`, `LoadFileWriterBase`) ignores it entirely.

### 3. Duplicated Column Logic
Load file column generation exists in THREE places:
1. `LoadFileGenerator.cs` (legacy DAT path)
2. `LoadFileWriterBase.cs` (new writers)
3. `DataGenerator.cs` (profile-based)

These produce **different values** for the same file. If you generate DAT and CSV simultaneously, the custodian names, dates, and authors will differ between formats.

### 4. No Cancellation Support
No `CancellationToken` is threaded through the pipeline. A long-running generation (millions of files) cannot be cleanly cancelled. CTRL+C will leave partial ZIP files on disk.

### 5. Tight Coupling to Console
Progress reporting, error output, and usage display are all hard-coded to `Console.Error` / `Console.Write`. This makes the code untestable and unusable as a library.

---

## Dependency Risks

| Dependency | Version | Risk |
|-----------|---------|------|
| `ClosedXML` | 0.105.0 | ✅ Active project, used only at static init for XLSX template |
| `DocumentFormat.OpenXml` | 3.4.1 | ⚠️ Listed but not directly referenced in user code. Transitive dependency of ClosedXML? |
| `SixLabors.ImageSharp` | 3.1.12 | ✅ used for TIFF generation, active project |
| `System.Drawing.Common` | 10.0.3 | ⚠️ Windows-only APIs, deprecated for cross-platform. Listed but not directly used in source. |
| `System.Text.Encoding.CodePages` | 10.0.3 | ✅ Needed for Windows-1252 ANSI encoding |
| `StyleCop.Analyzers` | 1.2.0-beta.556 | ⚠️ Beta version in production. Pin to stable release when available |

**`System.Drawing.Common` should be removed** — it's not referenced anywhere in the source code and is deprecated for cross-platform use.

---

## Scalability Limits

| Scenario | Limit | Root Cause |
|----------|-------|------------|
| File count > 100K | OOM likely | All `FileData` held in `ConcurrentBag` |
| File size > 2GB total padding | Crash | `long` → `int` cast overflow in `GenerateFileData` |
| Load file > 1M rows | Slow | Per-row `await WriteLineAsync` allocations |
| Concurrent ZIP writes | Corruption | `ZipArchive` is not thread-safe (currently mitigated by single consumer) |
| Profile with 200 columns × 1M files | OOM | `Dictionary<string, string>` per row, never freed until load file complete |

---

## Observability Gaps

1. **No structured logging.** All output is `Console.WriteLine`. No log levels, no correlation IDs, no structured fields.
2. **No metrics.** File generation rate is computed but not emitted to any metrics system.
3. **No tracing.** No OpenTelemetry or similar — impossible to diagnose slow runs.
4. **No health check.** CLI tool, so less relevant, but benchmark mode doesn't save results anywhere machine-readable.
5. **Error messages go to both stderr and stdout inconsistently.** `ShowUsage()` writes to stderr (correct), but progress writes to stdout with `\r` carriage returns that corrupt redirected output.

---

## CI/CD Weaknesses

1. **Coverage gate is Linux-only.** Windows and macOS test runs don't check coverage. A platform-specific bug could be untested.
2. **No integration tests in CI.** The E2E tests (`run-tests.sh`) run the binary but don't verify load file correctness (column counts, delimiter usage, etc.).
3. **Tag-and-release is auto-increment only.** No semantic versioning enforcement. Breaking CLI changes get patch bumps.
4. **No SAST/DAST scanning.** No CodeQL, no dependency vulnerability scanning beyond Dependabot.
5. **Build caching keys don't include source file hashes.** Only `.csproj` and `packages.lock.json` are hashed — source code changes can be served from stale cache.
6. **Duplicate release jobs.** Both `tag-and-release` (push to main) and `release` (tag push) exist. The tag-and-release creates a tag, which triggers the release job. This is redundant — the release from `tag-and-release` already creates the GitHub release.

---

## Security Posture

| Area | Status | Details |
|------|--------|---------|
| Path traversal | 🟡 Partial | `PathValidator` has bypass potential (C4) |
| Input validation | 🟡 Partial | No length limits on string args, silent swallowing of bad args |
| Deserialization | 🟡 Partial | JSON profile loading has no size limit |
| File system safety | ✅ Good | Output confined to specified directory |
| Dependency supply chain | 🟡 Partial | Dependabot configured, but no lock file verification in CI |
| MIME injection | ✅ Good | EML boundaries use GUID, no user-controlled MIME boundaries |

---

## Missing Tests

1. **No tests for `Program.Main` exit codes on failure scenarios**
2. **No tests for `--seed` determinism** (verifying same seed = same output)
3. **No tests for `--load-file-formats` (multi-format) output correctness**
4. **No tests for concurrent access to `PerformanceMonitor`**
5. **No tests for large file counts (>10K)** — only small counts tested
6. **No tests for `ZipArchiveService` error handling** (disk full, IO errors)
7. **No fuzz testing** of `CommandLineValidator` argument parsing
8. **No tests verifying consistency between DAT and CSV/OPT writers for same input**
9. **`IntegrationTests.cs` has a single placeholder test** (1362 bytes, essentially empty)

---

## Tech Debt Hotspots

Ranked by impact × effort-to-fix:

1. **`ZipArchiveService` + `ParallelFileGenerator`** — Memory model needs redesign to stream data
2. **`LoadFileGenerator` vs `LoadFileWriterBase`** — Duplicate metadata logic that produces different results
3. **`CommandLineValidator`** — 730-line monolith, needs decomposition
4. **`EmailTemplateSystem`** — Unreferenced placeholders, non-deterministic category selection
5. **`BuiltInProfiles`** — Allocates new objects on every property access

---

## Suggested Refactor Roadmap (Prioritized)

### Phase 1: Critical Fixes (< 1 week)

1. **Fix integer overflow in `GenerateFileData`** — change pool rental to use `long`-safe path, cap at `int.MaxValue`
2. **Fix argument swallowing in `CommandLineValidator`** — report error when argument value parsing fails
3. **Fix `Program.Main` to propagate failure exit code from `GenerateFiles`**
4. **Remove `System.Drawing.Common` dependency** — unused

### Phase 2: Correctness Improvements (1-2 weeks)

5. **Unify metadata generation** — single source of truth for custodian/date/author columns
6. **Thread `--seed` through all random operations** — including template selection and metadata generation
7. **Fix unreferenced template placeholders** in `EmailTemplateSystem`
8. **Add unknown argument detection** to `CommandLineValidator`
9. **Fix `PathValidator`** — use canonical path comparison instead of substring matching
10. **Cache `BuiltInProfiles`** — use `{ get; } = CreateXxx()` pattern

### Phase 3: Scalability (2-4 weeks)

11. **Stream load file generation** — write load file rows as files are written to ZIP, don't accumulate `ConcurrentBag`
12. **Batch async writes** in load file writers — `StringBuilder` buffer + periodic `FlushAsync`
13. **Add `CancellationToken` support** throughout the pipeline
14. **Add back-pressure monitoring** — track memory usage and throttle generation

### Phase 4: Observability & Testing (ongoing)

15. **Add structured logging** (Microsoft.Extensions.Logging or Serilog)
16. **Add integration tests** that verify load file content correctness
17. **Add determinism tests** (same seed = same output)
18. **Add large-scale stress tests** to CI (10K+ files)

---

## Quick Wins (< 1 day each)

| Fix | File | Effort |
|-----|------|--------|
| Return non-zero exit code on generation failure | `Program.cs` | 5 min |
| Add namespace to `PlaceholderFiles.cs` | `PlaceholderFiles.cs` | 2 min |
| Cache `BuiltInProfiles` (replace `=>` with `=`) | `BuiltInProfiles.cs` | 5 min |
| Replace `ConcurrentBag` with `List` (single consumer) | `ZipArchiveService.cs` | 10 min |
| Remove unused `System.Drawing.Common` reference | `Zipper.csproj` | 2 min |
| Add missing template placeholders to `BuildReplacements` | `EmailTemplateSystem.cs` | 15 min |
| Warn on unknown CLI arguments | `CommandLineValidator.cs` | 20 min |
| Report error on failed numeric argument parsing | `CommandLineValidator.cs` | 15 min |

---

## Long-Term Improvements

1. **Library extraction**: Separate the generation engine from the CLI shell. Enable use as a NuGet package.
2. **Streaming architecture**: Process files in fixed-size batches (e.g., 1000 at a time) to bound memory usage.
3. **Plugin system**: Allow custom file type generators via interface implementation and runtime loading.
4. **Configuration file support**: Move common argument combinations to a config file (YAML/JSON).
5. **Structured output mode**: Add `--output-format json` for machine-parseable results.
6. **Distributed generation**: For truly large datasets (10M+ files), support sharding across multiple processes/machines with merged load files.
