# AI Agent Instructions for Zipper

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

**CRITICAL:** Always reference @CLAUDE.md for architecture details and @AGENTS.md for agent-specific instructions.

Use 'bd' for task tracking.

---

## Quick Reference (Build/Test Commands)

```bash
# Build
dotnet publish -c Release
# Output: Zipper/bin/Release/net8.0/<platform>/publish/

# Run
dotnet run --project Zipper/Zipper.csproj -- [args]

# Unit Tests
dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj

# Single Test
dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~TestName"

# E2E Tests (Linux/macOS | Windows)
./tests/run-tests.sh | tests/run-tests.bat

# EML Tests
./tests/test-eml-comprehensive.sh | tests/test-eml-comprehensive.bat
```

**CRITICAL:** ALL tests (Unit and E2E) must pass before any commit. E2E tests MUST verify actual output correctness (file counts, headers, content), not just successful execution. ALL new E2E tests MUST have both .bat (Windows) and .sh (Unix) implementations.

---

## Architecture Overview

**Entry Point:** `Program.cs` - CLI parsing, validation, orchestration

**Core Pipeline (Producer-Consumer):**
```
Program.cs
  → FileGenerationRequest (DTO with all parameters)
  → ParallelFileGenerator.GenerateFilesAsync()
  → Channel<FileWorkItem> (work distribution)
  → Multiple producer tasks (generate FileData)
  → Channel<FileData> (results)
  → ZipArchiveService.CreateArchiveAsync()
  → ZIP archive + load file
```

**Key Components:**
| Component | Purpose |
|-----------|---------|
| `CommandLineValidator.cs` | Pre-process validation of all CLI args |
| `FileDistributionHelper.cs` | O(1) distribution algorithm dispatcher |
| `ProportionalDistribution.cs` | Round-robin: `(i-1) % folders + 1` |
| `GaussianDistribution.cs` | Bell curve with pre-calculated mean/stddev |
| `ExponentialDistribution.cs` | Exponential decay with pre-calculated lambda |
| `PlaceholderFiles.cs` | Binary templates (PDF/JPG/TIFF) for max compression |
| `OfficeFileGenerator.cs` | DOCX/XLSX generation using OpenXml/ClosedXML |
| `EmlGenerationService.cs` | Email generation with attachment support |
| `TiffMultiPageGenerator.cs` | Multipage TIFF with variable page counts |
| `BatesNumberGenerator.cs` | Legal document numbering (PREFIX + zero-pad) |
| `MemoryPoolManager.cs` | ArrayPool<byte> for large buffer pooling |
| `LoadFileWriterFactory.cs` | Creates format-specific load file writers |

**Load File Writers (Strategy Pattern):**
- `ILoadFileWriter` interface, `LoadFileWriterBase` common implementation
- `DatWriter` - Caret (^) delimiters, ASCII 20/254 (Concordance standard)
- `OptWriter` - Tab delimiters (Opticon format)
- `CsvWriter` - RFC 4180 CSV with proper escaping
- `XmlWriter` - Structured XML markup
- `ConcordanceWriter` - Comma delimiters with CSV escaping

**Supporting Types:**
- `FileGenerationRequest.cs` - Request DTO
- `LoadFileFormat.cs` - Format enum (Dat, Opt, Csv, Xml, Concordance)
- `DistributionType.cs` - Distribution enum (Proportional, Gaussian, Exponential)
- `BatesNumberConfig.cs` - Bates configuration (prefix, start, digits)

---

## Command-Line Arguments

**Required:**
- `--type <pdf|jpg|tiff|eml|docx|xlsx>` - File type to generate
- `--count <number>` - Total files to generate
- `--output-path <directory>` - Output directory for ZIP and load file

**Optional:**
- `--folders <number>` - Number of folders (1-100, default: 1)
- `--distribution <proportional|gaussian|exponential>` - File distribution pattern
- `--encoding <UTF-8|UTF-16|ANSI>` - Load file encoding (default: UTF-8)
- `--with-metadata` - Add Custodian, Date Sent, Author, File Size columns
- `--with-text` - Generate .txt files for each document
- `--attachment-rate <0-100>` - EML attachment percentage (default: 0)
- `--target-zip-size <size>` - Target ZIP size (e.g., 500MB, 10GB)
- `--include-load-file` - Include load file inside ZIP archive
- `--load-file-format <dat|opt|csv|xml|concordance>` - Load file format
- `--bates-prefix <prefix>` - Bates number prefix
- `--bates-start <number>` - Bates starting number (default: 1)
- `--bates-digits <number>` - Bates digit count (default: 8)
- `--tiff-pages <min-max>` - TIFF page count range (default: "1-1")

---

## Load File Columns

| Condition | Columns Added |
|-----------|---------------|
| **Always** | Control Number, File Path |
| `--with-metadata` | Custodian, Date Sent, Author, File Size |
| **EML type** (always) | To, From, Subject, Sent Date, Attachment |
| `--bates-prefix` | Bates Number |
| `--tiff-pages` (TIFF only) | Page Count |
| `--with-text` | Extracted Text (path to .txt) |

**Note:** EML files always include metadata columns (To, From, Subject, Sent Date, Attachment) regardless of `--with-metadata` flag.

---

## Critical Implementation Patterns

**EML Sequential Processing:**
```csharp
// EML forces concurrency=1 when attachments or text enabled
// to avoid ZIP entry creation conflicts
if (request.FileType.ToLower() == "eml" && (request.WithText || request.AttachmentRate > 0))
{
    request.Concurrency = 1;
}
```

**ANSI Encoding (Cross-Platform):**
```csharp
// Required for Windows-1252 support on Linux
System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
```

**Distribution O(1) Requirement:**
- All algorithms MUST be O(1) per file - no iteration over previous files
- Pre-calculate parameters where possible
- Handle edge cases (single folder, etc.)

**Stream Ownership Pattern:**
```csharp
// Caller-owned streams: flush but don't dispose
await writer.FlushAsync();
// NOT: await writer.DisposeAsync();
```

---

## File Type Support

| Type | Generator | Notes |
|------|-----------|-------|
| `pdf`, `jpg`, `tiff` | `PlaceholderFiles.cs` | Binary templates, max compression |
| `eml` | `EmlGenerationService.cs` | Dynamic generation, attachment support |
| `docx`, `xlsx` | `OfficeFileGenerator.cs` | DocumentFormat.OpenXml, ClosedXML |

---

## Code Style Conventions

- **C# 8.0+**: Implicit usings, nullable reference types, file-scoped namespaces
- **Modern patterns**: `switch` expressions, records, `using var`, pattern matching
- **Performance**: `Span<T>`, `Memory<T>`, `ArrayPool<T>`, zero-allocation in hot paths
- **Async**: Always pass `CancellationToken`, use `ConfigureAwait(false)` in library code
- **Naming**: PascalCase (types/methods), camelCase (locals), UPPER_CASE (constants)

---

## Performance Critical Paths

1. `ParallelFileGenerator.ProcessFileWorkAsync` - File generation loop
2. `FileDistributionHelper.GetFolderNumber` - Distribution calculation
3. `ILoadFileWriter.WriteAsync` implementations - Load file writing
4. `ZipArchiveService.CreateArchiveAsync` - ZIP entry creation

**Anti-Patterns to Avoid:**
- String concatenation in loops → use `StringBuilder`
- LINQ in hot paths → use foreach loops
- Unnecessary allocations → use spans and memory pools
- Blocking async → use `await` properly

---

## Testing Requirements

**CRITICAL CONSTITUTIONAL REQUIREMENTS:**

1. **ALL tests must pass** before any commit (unit + E2E)
2. **E2E tests MUST verify actual output** (file counts, headers, content) - NOT just successful execution
3. **ALL new E2E tests MUST have both .bat (Windows) and .sh (Unix) implementations**
4. **Both test versions MUST pass on both platforms** before deployment
5. **No feature is complete** until tests pass on Windows AND Linux/macOS

**E2E Test Locations:**
- Main: `tests/run-tests.sh` / `tests/run-tests.bat`
- EML: `tests/test-eml-comprehensive.sh` / `tests/test-eml-comprehensive.bat`

---

## Dependencies

- **Framework:** .NET 8.0 SDK
- `SixLabors.ImageSharp` - TIFF generation
- `ClosedXML` - XLSX generation
- `DocumentFormat.OpenXml` - DOCX generation
- `System.Drawing.Common` - Image processing
- `System.Text.Encoding.CodePages` - ANSI encoding support

---

## Versioning

- Format: `MAJOR.MINOR.BUILD`
- `MAJOR.MINOR`: Managed manually in `.version` file
- `BUILD`: Auto-generated from GitHub Actions run_number
- Full version: `<major>.<minor>.<run_number>`
- Auto-release on each successful `master` branch build

---

## Session Completion (Landing the Plane)

**When ending a work session**, you MUST complete ALL steps. Work is NOT complete until `git push` succeeds.

1. **File issues** for remaining work
2. **Run quality gates** (if code changed): tests, linters, builds
3. **Update issue status**: close finished, update in-progress
4. **PUSH TO REMOTE** (MANDATORY):
   ```bash
   git pull --rebase
   bd sync
   git push
   git status  # MUST show "up to date with origin"
   ```
5. **Clean up**: clear stashes, prune remote branches
6. **Verify**: all changes committed AND pushed
7. **Hand off**: provide context for next session

**CRITICAL RULES:**
- Work is NOT complete until `git push` succeeds
- NEVER stop before pushing - that leaves work stranded locally
- NEVER say "ready to push when you are" - YOU must push
- If push fails, resolve and retry until it succeeds

Use 'bd' for task tracking
