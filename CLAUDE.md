# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

always reference @AGENTS.md

Use 'bd' for task tracking

## Build/Test Commands

- **Build**: `dotnet publish -c Release` (output: `Zipper/bin/Release/net8.0/<platform>/publish/`)
- **Run**: `dotnet run --project Zipper/Zipper.csproj -- [args]`
- **Unit Tests**: `dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj`
- **Single Test**: `dotnet test Zipper/Zipper.Tests/Zipper.Tests.csproj --filter "FullyQualifiedName~TestName"`
- **E2E Tests**: `tests/run-tests.sh` (Linux/macOS) or `tests/run-tests.bat` (Windows)
- **EML Tests**: `tests/test-eml-comprehensive.sh` or `.bat`
- **Stress Tests**: `tests/stress/run-stress-tests.sh` (manual invocation only)

CRITICAL: ALL tests (Unit and E2E) must pass before any commit. E2E tests MUST verify actual output correctness (file counts, headers, content), not just successful execution. ALL new E2E tests MUST have both .bat (Windows) and .sh (Unix) implementations that pass on both platforms.

## Architecture Overview

**Entry Point & Orchestration:**
- `Program.cs` - CLI argument parsing, validation, orchestration
- `CommandLineValidator.cs` - Validates all CLI arguments before processing

**Core Generation Pipeline:**
```
Program.cs
    ↓ (creates FileGenerationRequest)
ParallelFileGenerator.GenerateFilesAsync()
    ↓ (creates work distribution via Channel<FileWorkItem>)
Multiple producer tasks → Generate file content (FileData)
    ↓ (via Channel<FileData>)
ZipArchiveService.CreateArchiveAsync() → Writes ZIP + load file
```

**File Generation:**
- `PlaceholderFiles.cs` - Binary templates for PDF/JPG/TIFF (minimal, identical placeholders for max compression)
- `OfficeFileGenerator.cs` - Generates DOCX/XLSX using DocumentFormat.OpenXml and ClosedXML
- `EmlGenerationService.cs` - Email generation with template system
- `TiffMultiPageGenerator.cs` - Multipage TIFF generation with variable page counts
- `BatesNumberGenerator.cs` - Legal document numbering (PREFIX + zero-padded number)

**Distribution Algorithms (O(1) per file):**
- `FileDistributionHelper.cs` - Entry point that delegates to specialized algorithms
- `ProportionalDistribution.cs` - Round-robin: `(fileIndex - 1) % totalFolders + 1`
- `GaussianDistribution.cs` - Bell curve using pre-calculated mean/stddev
- `ExponentialDistribution.cs` - Exponential decay using pre-calculated lambda

**Load File Writers (Strategy Pattern):**
- `ILoadFileWriter.cs` - Interface for all load file formats
- `LoadFileWriterFactory.cs` - Creates appropriate writer based on LoadFileFormat enum
- `LoadFileWriterBase.cs` - Base class with common column generation (metadata, Bates, text paths)
- `DatWriter.cs` - Standard DAT format (caret ^ delimiters, ASCII 20/254)
- `OptWriter.cs` - Opticon format (tab delimiters)
- `CsvWriter.cs` - RFC 4180 CSV with proper escaping
- `XmlWriter.cs` - Structured XML markup
- `ConcordanceWriter.cs` - Concordance DB format (comma delimiters, CSV escaping)

**Performance & Memory:**
- `MemoryPoolManager.cs` - Pools memory for large file operations using ArrayPool<byte>
- `PerformanceMonitor.cs` - Real-time metrics (files/sec, memory usage, ETA)
- `ProgressTracker.cs` - Console progress display
- `BufferedStreamWriter.cs` - Buffered I/O for large writes

**Supporting Types:**
- `FileGenerationRequest.cs` - DTO for all generation parameters
- `LoadFileFormat.cs` - Enum for load file format types
- `DistributionType.cs` - Enum for distribution algorithms
- `BatesNumberConfig.cs` - Config for Bates numbering

## Key Data Flow Patterns

**Producer-Consumer Pattern (ParallelFileGenerator):**
1. Work channel creates FileWorkItem objects (index, folder, filename)
2. Multiple producer tasks generate FileData (content + metadata) from work items
3. Single consumer task writes files to ZIP archive via result channel
4. Load file written after all files processed

**Distribution Pattern:**
- All algorithms are O(1) - no iteration over previous files
- Proportional: Simple modulo arithmetic
- Gaussian: Uses pre-calculated parameters, then formula-based distribution
- Exponential: Uses pre-calculated decay rate, then formula-based distribution

**EML Special Handling:**
- EML files force sequential processing (concurrency=1) when attachments or text extraction enabled
- This avoids ZIP entry creation conflicts in concurrent scenarios

## File Type Support

- `pdf`, `jpg`, `tiff` - Use binary templates from `PlaceholderFiles.cs`
- `eml` - Dynamically generated via `EmlGenerationService.cs`
- `docx`, `xlsx` - Generated via `OfficeFileGenerator.cs`

## Load File Columns

**Always included:**
- Control Number (Document ID)
- File Path

**With --with-metadata (all types):**
- Custodian, Date Sent, Author, File Size

**EML type (always, regardless of --with-metadata):**
- To, From, Subject, Sent Date, Attachment

**With --bates-prefix:**
- Bates Number

**With --tiff-pages on TIFF:**
- Page Count

**With --with-text:**
- Extracted Text (path to .txt file)

## Encoding Handling

- **ANSI encoding** requires `System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` for cross-platform support (Windows-1252 not available by default on Linux)
- This is critical for any code that writes load files with ANSI encoding

## Memory Management Patterns

- Use `ArrayPool<byte>.Shared` for temporary large buffers
- Use `IMemoryOwner<byte>` for pooled memory that needs disposal
- Use `Span<T>` and `Memory<T>` for zero-allocation operations
- Dispose `IDisposable` and `IAsyncDisposable` resources properly
- Stream-based processing - avoid intermediate files where possible

## Performance Critical Paths

- File generation loop in `ParallelFileGenerator.ProcessFileWorkAsync`
- Distribution calculation in `FileDistributionHelper.GetFolderNumber`
- Load file writing in `ILoadFileWriter.WriteAsync` implementations
- ZIP entry creation in `ZipArchiveService.CreateArchiveAsync`

## Error Handling Patterns

- **CLI validation errors**: Exit code 1 with helpful usage text
- **Runtime errors**: Throw descriptive exceptions with clear messages
- **Progress indication**: Display console progress for operations > 5 seconds
