# Zipper: A Complete Code Walkthrough

*2026-02-25T20:25:17Z by Showboat 0.6.1*
<!-- showboat-id: c8387891-b933-4b5c-a51b-e7a1bb27dd62 -->

## Overview

Zipper is a .NET 8 command-line tool for generating large ZIP archives containing placeholder documents and corresponding load files. It's designed for eDiscovery performance testing and can generate archives with up to 100 million files across six formats: PDF, JPG, TIFF, EML, DOCX, and XLSX.

This walkthrough traces the code linearly — from startup through argument parsing, file generation, and output — so you can see exactly how every piece fits together.

## Project Structure

Let's start with a bird's-eye view of the source tree. All application code lives under `src/`:

```bash
find src -maxdepth 1 -name "*.cs" -o -name "*.csproj" | sort && echo "---" && echo "src/LoadFiles/" && find src/LoadFiles -name "*.cs" | sort && echo "---" && echo "src/Profiles/" && find src/Profiles -name "*.cs" | sort
```

```output
src/BatesNumberGenerator.cs
src/ChaosAnomaly.cs
src/ChaosEngine.cs
src/CommandLineValidator.cs
src/ContentTypeHelper.cs
src/EmailBuilder.cs
src/EmailTemplateSystem.cs
src/EmlGenerationService.cs
src/EncodingHelper.cs
src/ExponentialDistribution.cs
src/FileDistributionHelper.cs
src/FileGenerationRequest.cs
src/GaussianDistribution.cs
src/LoadFileFormat.cs
src/LoadFileGenerator.cs
src/LoadfileAuditWriter.cs
src/LoadfileOnlyGenerator.cs
src/MemoryPoolManager.cs
src/OfficeFileGenerator.cs
src/ParallelFileGenerator.cs
src/PathValidator.cs
src/PerformanceBenchmarkRunner.cs
src/PerformanceConstants.cs
src/PerformanceMonitor.cs
src/PlaceholderFiles.cs
src/Program.cs
src/ProportionalDistribution.cs
src/TiffMultiPageGenerator.cs
src/ZipArchiveService.cs
src/Zipper.csproj
---
src/LoadFiles/
src/LoadFiles/ConcordanceWriter.cs
src/LoadFiles/CsvWriter.cs
src/LoadFiles/ILoadFileWriter.cs
src/LoadFiles/LoadFileWriterBase.cs
src/LoadFiles/LoadFileWriterFactory.cs
src/LoadFiles/OptWriter.cs
src/LoadFiles/XmlLoadFileWriter.cs
---
src/Profiles/
src/Profiles/BuiltInProfiles.cs
src/Profiles/ColumnProfile.cs
src/Profiles/ColumnProfileLoader.cs
src/Profiles/DataGenerator.cs
```

## 1. Entry Point — `Program.cs`

Everything starts in `Program.Main()`. It prints the version, checks for `--benchmark` mode, then hands off to `CommandLineValidator` to parse arguments. Based on the result, it branches into one of two paths: **loadfile-only** mode or the full **ZIP generation** pipeline.

```bash
sed -n '7,41p' src/Program.cs
```

```output
        public static async Task<int> Main(string[] args)
        {
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
            Console.WriteLine($"Zipper v{version} https://github.com/dwojtaszek/zipper/");
            Console.WriteLine();

            if (args.Contains("--benchmark"))
            {
                try
                {
                    await PerformanceBenchmarkRunner.RunBenchmarks();
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"\nBenchmark error: {ex.Message}");
                    return 1;
                }
            }

            // Validate and parse command line arguments
            var request = CommandLineValidator.ValidateAndParseArguments(args);
            if (request == null)
            {
                return 1; // Error already displayed by CommandLineValidator
            }

            if (request.LoadfileOnly)
            {
                return await RunLoadfileOnly(request);
            }

            bool success = await GenerateFiles(request);
            return success ? 0 : 1;
        }
```

The three branches are clear:
- **`--benchmark`** → runs `PerformanceBenchmarkRunner` and exits
- **`request.LoadfileOnly`** → calls `RunLoadfileOnly()` which streams a DAT/OPT directly to disk
- **Otherwise** → calls `GenerateFiles()` which creates the full ZIP archive with `ParallelFileGenerator`

Both paths return an exit code: `0` for success, `1` for failure.

## 2. Argument Parsing — `CommandLineValidator.cs`

This is the largest file in the project (~1,160 lines). It handles three stages:

1. **`ParseArguments()`** — tokenizes the raw `string[] args` into a `ParsedArguments` dictionary, handling flags, key-value pairs, and validation of unknown arguments
2. **`ValidateRequiredArguments()`** — ensures `--type`, `--count`, and `--output-path` are present (with `--type` optional in `--loadfile-only` mode)
3. **`ValidateOptionalArguments()`** — checks ranges, mutual exclusions, and format constraints
4. **`CreateFileGenerationRequest()`** — transforms validated args into a `FileGenerationRequest` DTO

The key entry point for all of this is `ValidateAndParseArguments()`:

```bash
sed -n '19,54p' src/CommandLineValidator.cs
```

```output
        /// <summary>
        /// Validates and parses command line arguments into a FileGenerationRequest.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>Validated FileGenerationRequest, or null if validation fails.</returns>
        public static FileGenerationRequest? ValidateAndParseArguments(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                ShowUsage();
                return null;
            }

            // Parse arguments
            var parsedArgs = ParseArguments(args);
            if (parsedArgs == null)
            {
                return null;
            }

            // Validate required arguments
            if (!ValidateRequiredArguments(parsedArgs))
            {
                ShowUsage();
                return null;
            }

            // Validate optional arguments
            if (!ValidateOptionalArguments(parsedArgs))
            {
                return null;
            }

            // Convert to FileGenerationRequest
            return CreateFileGenerationRequest(parsedArgs);
        }
```

The pipeline is: parse → validate required → validate optional → build request. If any step fails, it returns `null` and `Program.Main()` exits with code 1.

Notable validation constraints include:
- `--folders` must be 1–100
- `--attachment-rate` only valid for `--type eml`
- `--loadfile-only` conflicts with `--target-zip-size` and `--include-load-file`
- `--chaos-mode` requires `--loadfile-only`
- Custom delimiters (`--col-delim`, `--quote-delim`, etc.) use strict `ascii:N` or `char:C` prefix format

## 3. The Config DTO — `FileGenerationRequest.cs`

`FileGenerationRequest` is a plain data class that carries every setting from the CLI into the generation pipeline. It is **immutable after construction** — shared across concurrent tasks without synchronization. Here are the key properties:

```bash
grep -n 'public.*{ get; set; }' src/FileGenerationRequest.cs | head -20
```

```output
14:        public string OutputPath { get; set; } = string.Empty;
19:        public long FileCount { get; set; }
24:        public string FileType { get; set; } = string.Empty;
29:        public int Folders { get; set; } = 1;
34:        public int Concurrency { get; set; } = PerformanceConstants.DefaultConcurrency;
39:        public bool WithMetadata { get; set; }
44:        public bool WithText { get; set; }
49:        public long? TargetZipSize { get; set; }
54:        public bool IncludeLoadFile { get; set; }
59:        public DistributionType Distribution { get; set; } = DistributionType.Proportional;
64:        public string Encoding { get; set; } = "UTF-8";
69:        public int AttachmentRate { get; set; } = 0;
74:        public LoadFileFormat LoadFileFormat { get; set; } = LoadFileFormat.Dat;
79:        public List<LoadFileFormat>? LoadFileFormats { get; set; }
84:        public string ColumnDelimiter { get; set; } = "\u0014"; // ASCII 20
89:        public string QuoteDelimiter { get; set; } = "\u00fe"; // ASCII 254
94:        public string NewlineDelimiter { get; set; } = "\u00ae"; // ASCII 174
99:        public BatesNumberConfig? BatesConfig { get; set; }
104:        public (int Min, int Max)? TiffPageRange { get; set; }
109:        public ColumnProfile? ColumnProfile { get; set; }
```

The defaults tell the story: standard Concordance delimiters (ASCII 20/254/174), proportional distribution, UTF-8 encoding, DAT format. The `ColumnProfile?`, `BatesConfig?`, and `TiffPageRange?` properties are nullable — only set when the user opts into those features.

`FileGenerationResult` is the companion return type — it carries the paths of generated files, elapsed time, and throughput metrics back to `Program.Main()` for the final summary.

## 4. Placeholder Files — The O(1) Content Strategy

Before diving into the generation pipeline, we need to understand zipper's core optimization: **pre-computed, minimal placeholder files**. Instead of generating unique content for each document, zipper creates one tiny valid file per format at startup and reuses it for every entry in the archive.

`PlaceholderFiles.cs` stores hardcoded byte arrays for PDF, JPG, and TIFF. Each is a minimal, valid file — a 1×1 black pixel image or a bare-bones PDF document:

This "content map" pattern means `GetContent("pdf")` always returns the same ~320-byte PDF. The trade-off is obvious: files are not unique, but they are **valid** (they open in real viewers) and generation is O(1) per file. This is what enables generating 100M files in minutes rather than hours.

DOCX and XLSX files follow the same pattern but are built via `OfficeFileGenerator`, which pre-computes them at static initialization:

```bash
sed -n '9,39p' src/OfficeFileGenerator.cs
```

```output
internal static class OfficeFileGenerator
{
    /// <summary>
    /// Pre-computed minimal valid DOCX document.
    /// </summary>
    private static readonly byte[] PrecomputedDocx = CreateMinimalDocx();

    /// <summary>
    /// Pre-computed minimal valid XLSX spreadsheet.
    /// </summary>
    private static readonly byte[] PrecomputedXlsx = CreateMinimalXlsx();

    /// <summary>
    /// Returns a pre-computed minimal DOCX document.
    /// </summary>
    /// <param name="workItem">File work item containing index and metadata.</param>
    /// <returns>Byte array containing a valid DOCX document.</returns>
    public static byte[] GenerateDocx(FileWorkItem workItem)
    {
        return PrecomputedDocx;
    }

    /// <summary>
    /// Returns a pre-computed minimal XLSX spreadsheet.
    /// </summary>
    /// <param name="workItem">File work item containing index and metadata.</param>
    /// <returns>Byte array containing a valid XLSX spreadsheet.</returns>
    public static byte[] GenerateXlsx(FileWorkItem workItem)
    {
        return PrecomputedXlsx;
    }
```

`CreateMinimalDocx()` builds a valid OOXML ZIP structure (content types, relationships, and a document.xml) in memory. `CreateMinimalXlsx()` uses the ClosedXML library to create a workbook with a single sheet. Both are called once and cached as `static readonly byte[]`.

## 5. Email Generation — The EML Pipeline

Email files are the most complex content type. Three classes collaborate:

1. **`EmailTemplateSystem`** — generates realistic email metadata (To/From/Subject/Body) from template categories (business, personal, notifications, etc.)
2. **`EmailBuilder`** — constructs RFC 2822 compliant EML content with proper MIME formatting, including multipart boundaries for attachments
3. **`EmlGenerationService`** — orchestrates the above two, deciding whether to include attachments based on the configured rate

```bash
sed -n '45,79p' src/EmlGenerationService.cs
```

```output
        /// <summary>
        /// Generates EML content based on configuration.
        /// </summary>
        /// <param name="config">Configuration for EML generation.</param>
        /// <returns>EML generation result with content and optional attachment.</returns>
        public static EmlGenerationResult GenerateEmlContent(EmlGenerationConfig config)
        {
            // Get a realistic email template
            var emailTemplate = EmailTemplateSystem.GetRandomTemplate(
                config.FileIndex,
                config.FileIndex,
                config.Category);

            // Determine if we should include an attachment
            (string filename, byte[] content)? attachment = null;
            if (ShouldIncludeAttachment(config.AttachmentRate))
            {
                attachment = PlaceholderFiles.GetRandomAttachment();
            }

            // Build the EML content
            var emlContent = EmailBuilder.BuildEmail(
                emailTemplate.To,
                emailTemplate.From,
                emailTemplate.Subject,
                emailTemplate.SentDate,
                emailTemplate.Body,
                attachment);

            return new EmlGenerationResult
            {
                Content = emlContent,
                Attachment = attachment,
            };
        }
```

The flow is clear: get a template → maybe pick an attachment → build the EML bytes. Attachments are randomly selected from the same `PlaceholderFiles` pool (PDF, JPG, or TIFF), base64-encoded and wrapped in MIME boundaries by `EmailBuilder`.

## 6. The Parallel Generation Pipeline — `ParallelFileGenerator.cs`

This is the heart of zipper. It uses a **producer-consumer pattern** with `System.Threading.Channels`:

```bash
sed -n '20,46p' src/ParallelFileGenerator.cs
```

```output
        public async Task<FileGenerationResult> GenerateFilesAsync(FileGenerationRequest request)
        {
            this.performanceMonitor.Start(request.FileCount);

            try
            {
                // Validate inputs
                if (request.FileCount <= 0)
                {
                    throw new ArgumentException("File count must be positive", nameof(request.FileCount));
                }

                if (request.Concurrency <= 0)
                {
                    request.Concurrency = PerformanceConstants.DefaultConcurrency;
                }

                // For EML files, use sequential processing to avoid ZIP entry creation conflicts
                // when attachments and text extraction are enabled
                if (request.FileType.ToLowerInvariant() == "eml" && (request.WithText || request.AttachmentRate > 0))
                {
                    request.Concurrency = 1; // Force sequential processing
                }

                Directory.CreateDirectory(request.OutputPath);

                var baseFileName = $"archive_{DateTime.Now:yyyyMMdd_HHmmss}";
```

```bash
sed -n '46,116p' src/ParallelFileGenerator.cs
```

```output
                var baseFileName = $"archive_{DateTime.Now:yyyyMMdd_HHmmss}";
                var zipFilePath = Path.Combine(request.OutputPath, $"{baseFileName}.zip");
                var loadFileName = $"{baseFileName}.dat";
                var loadFilePath = Path.Combine(request.OutputPath, loadFileName);

                var placeholderContent = PlaceholderFiles.GetContent(request.FileType.ToLowerInvariant());

                // Allow Office formats and EML to have empty placeholder content (generated dynamically)
                if (placeholderContent.Length == 0 &&
                    request.FileType.ToLowerInvariant() != "eml" &&
                    !OfficeFileGenerator.IsOfficeFormat(request.FileType))
                {
                    throw new InvalidOperationException($"Unknown file type: {request.FileType}");
                }

                long paddingPerFile = 0;
                if (request.TargetZipSize.HasValue)
                {
                    paddingPerFile = this.CalculatePaddingPerFile(request.TargetZipSize.Value, placeholderContent.Length, request.FileCount, request.WithText);
                }

                // Create channels for work distribution
                var workChannelReader = CreateWorkChannel(request.FileCount, request.Folders, request.Distribution, request.FileType);
                var resultChannel = Channel.CreateBounded<FileData>(new BoundedChannelOptions(request.Concurrency * 2)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                });

                // Start the consumer task to write the archive concurrently
                var consumerTask = this.WriteArchiveAsync(zipFilePath, loadFileName, loadFilePath, request, resultChannel.Reader);

                // Generate files in parallel (producers)
                using var semaphore = new SemaphoreSlim(request.Concurrency);
                var producerTasks = Enumerable.Range(0, request.Concurrency)
                    .Select(i => this.ProcessFileWorkAsync(semaphore, workChannelReader, placeholderContent, paddingPerFile, resultChannel.Writer, request))
                    .ToList();

                // Wait for all producers to complete, wrapped in a task that ensures completion
                var allProducersTask = Task.WhenAll(producerTasks);

                try
                {
                    await allProducersTask;
                }
                finally
                {
                    // Signal that production is done, even if producers fail
                    resultChannel.Writer.Complete(allProducersTask.Exception);
                }

                // Wait for the consumer to finish writing the archive
                var actualLoadFilePath = await consumerTask;

                this.performanceMonitor.FinalizeProgress();
                var performanceMetrics = this.performanceMonitor.Stop();

                return new FileGenerationResult
                {
                    ZipFilePath = zipFilePath,
                    LoadFilePath = actualLoadFilePath,
                    FilesGenerated = request.FileCount,
                    GenerationTime = TimeSpan.FromMilliseconds(performanceMetrics.ElapsedMilliseconds),
                    FilesPerSecond = performanceMetrics.FilesPerSecond,
                };
            }
            catch
            {
                this.performanceMonitor.Stop();
                throw;
            }
        }
```

Here is the architecture in action:

1. **`CreateWorkChannel()`** creates a bounded channel and feeds `FileWorkItem` objects into it — each one carries an index, folder assignment, and file path
2. **N producer tasks** (`ProcessFileWorkAsync`) pull work items from the channel, generate file content (via `GenerateFileData`), and push `FileData` into a result channel
3. **One consumer task** (`WriteArchiveAsync` → `ZipArchiveService.CreateArchiveAsync`) reads from the result channel and writes each file into the ZIP archive sequentially (ZIP format requires sequential writes)
4. A `SemaphoreSlim` limits concurrency to the configured worker count

The bounded result channel (`Concurrency * 2` capacity) naturally applies backpressure — if the ZIP writer falls behind, producers block rather than accumulating unbounded memory.

Notice the special case: **EML files with attachments or text extraction are forced to sequential** (`Concurrency = 1`) because attachments create multiple ZIP entries per logical file, which would conflict in concurrent writing.

## 7. File Distribution — Spreading Files Across Folders

When `--folders` is greater than 1, zipper decides which folder each file goes into using `FileDistributionHelper`. It delegates to three strategy classes:

- **`ProportionalDistribution`** — round-robin: `folder = (fileIndex - 1) % totalFolders + 1`
- **`GaussianDistribution`** — bell curve centered on the middle folder
- **`ExponentialDistribution`** — exponential decay, concentrating files in early folders

Each strategy maps a file index to a folder number (1-based):

```bash
sed -n '14,52p' src/FileDistributionHelper.cs
```

```output
    public static class FileDistributionHelper
    {
        /// <summary>
        /// Gets the folder number for a file based on the specified distribution type.
        /// </summary>
        /// <param name="fileIndex">Current file index (1-based).</param>
        /// <param name="totalFiles">Total number of files.</param>
        /// <param name="totalFolders">Total number of folders (1-100).</param>
        /// <param name="distributionType">Type of distribution to use.</param>
        /// <returns>Folder number (1 to totalFolders).</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when input parameters are out of valid ranges.</exception>
        /// <exception cref="ArgumentException">Thrown when distribution type is unknown.</exception>
        public static int GetFolderNumber(long fileIndex, long totalFiles, int totalFolders, DistributionType distributionType)
        {
            // Input validation
            if (fileIndex < 1 || fileIndex > totalFiles)
            {
                throw new ArgumentOutOfRangeException(nameof(fileIndex), "File index must be between 1 and total files");
            }

            if (totalFolders < 1 || totalFolders > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(totalFolders), "Total folders must be between 1 and 100");
            }

            if (totalFiles < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(totalFiles), "Total files must be at least 1");
            }

            return distributionType switch
            {
                DistributionType.Proportional => ProportionalDistribution.CalculateFolder(fileIndex, totalFolders),
                DistributionType.Gaussian => GaussianDistribution.CalculateFolder(fileIndex, totalFiles, totalFolders),
                DistributionType.Exponential => ExponentialDistribution.CalculateFolder(fileIndex, totalFiles, totalFolders),
                _ => throw new ArgumentException($"Unknown distribution type: {distributionType}", nameof(distributionType)),
            };
        }
    }
```

## 8. ZIP Writing — `ZipArchiveService.cs`

The consumer side of the pipeline. `CreateArchiveAsync()` reads `FileData` from the channel and writes each file into a `System.IO.Compression.ZipArchive`. For each file, it may also write extracted text, attachments, and attachment text. After all files are written, it generates the load file(s) using the factory pattern:

```bash
sed -n '73,107p' src/ZipArchiveService.cs
```

```output
                ? request.LoadFileFormats
                : new List<LoadFileFormat> { request.LoadFileFormat };

            string actualLoadFilePath = loadFilePath;
            var baseFileName = Path.GetFileNameWithoutExtension(loadFileName);
            var baseFilePath = Path.GetDirectoryName(loadFilePath) ?? string.Empty;

            foreach (var format in formatsToGenerate)
            {
                var loadFileWriter = LoadFileWriterFactory.CreateWriter(format);
                var actualLoadFileName = baseFileName + loadFileWriter.FileExtension;

                if (request.IncludeLoadFile)
                {
                    var loadFileEntry = archive.CreateEntry(actualLoadFileName, CompressionLevel.Optimal);
                    using var loadFileStream = loadFileEntry.Open();
                    await loadFileWriter.WriteAsync(loadFileStream, request, processedFiles);

                    // Return path within the ZIP archive when load file is included
                    actualLoadFilePath = actualLoadFileName;
                }
                else
                {
                    var currentFilePath = Path.Combine(baseFilePath, actualLoadFileName);
                    await using var fileStream = new FileStream(currentFilePath, FileMode.Create);
                    await loadFileWriter.WriteAsync(fileStream, request, processedFiles);
                    await fileStream.FlushAsync();

                    actualLoadFilePath = currentFilePath;
                }
            }

            // Dispose all memory owners after processing is complete
            foreach (var fileData in processedFiles)
            {
```

The key design choice here: load files are written **after** all document files — which means the entire `processedFiles` list is held in memory. This is necessary because load files reference all documents, and the factory pattern (`LoadFileWriterFactory.CreateWriter()`) allows generating multiple formats (DAT, OPT, CSV, XML) in one pass.

## 9. Load File Writers — The Factory Pattern

The `LoadFiles/` directory contains a clean abstraction hierarchy:

- **`ILoadFileWriter`** — interface with `FormatName`, `FileExtension`, and `WriteAsync()`
- **`LoadFileWriterBase`** — base class with shared utilities (metadata generation, CSV escaping, Bates numbers)
- **`LoadFileWriterFactory`** — creates the right writer based on `LoadFileFormat` enum

Concrete writers:
- **`DatWriter`** (via `LoadFileGenerator`) — Concordance DAT with configurable ASCII delimiters
- **`OptWriter`** — Opticon format, 7-column comma-separated page references
- **`CsvWriter`** — RFC 4180 CSV with quoted fields
- **`ConcordanceWriter`** — Concordance format
- **`XmlLoadFileWriter`** — EDRM XML v1.2 schema

```bash
cat src/LoadFiles/LoadFileWriterFactory.cs | sed -n '8,26p'
```

```output
    /// <summary>
    /// Creates a load file writer for the specified format.
    /// </summary>
    /// <param name="format">The desired load file format.</param>
    /// <returns>An instance of ILoadFileWriter for the specified format.</returns>
    internal static ILoadFileWriter CreateWriter(LoadFileFormat format)
    {
        return format switch
        {
            LoadFileFormat.Dat => new DatWriter(),
            LoadFileFormat.Opt => new OptWriter(),
            LoadFileFormat.Csv => new CsvWriter(),
            LoadFileFormat.Xml => new XmlLoadFileWriter(),
            LoadFileFormat.EdrmXml => new XmlLoadFileWriter(), // EDRM XML uses same writer
            LoadFileFormat.Concordance => new ConcordanceWriter(),
            _ => new DatWriter(),
        };
    }
}
```

## 10. Bates Numbering — `BatesNumberGenerator.cs`

Bates numbers are sequential identifiers used in legal document management. The generator is simple but elegant — a `BatesNumberConfig` record holds the prefix, start number, digit count, and increment:

```bash
sed -n '6,70p' src/BatesNumberGenerator.cs
```

```output
public record BatesNumberConfig
{
    /// <summary>
    /// Gets prefix for Bates numbers (e.g., "CLIENT001").
    /// </summary>
    public string Prefix { get; init; } = "DOC";

    /// <summary>
    /// Gets starting number for the sequence.
    /// </summary>
    public long Start { get; init; } = 1;

    /// <summary>
    /// Gets number of digits for zero-padding.
    /// </summary>
    public int Digits { get; init; } = 8;

    /// <summary>
    /// Gets increment between consecutive numbers.
    /// </summary>
    public long Increment { get; init; } = 1;
}

/// <summary>
/// Generates Bates numbers for legal document identification.
/// </summary>
public static class BatesNumberGenerator
{
    /// <summary>
    /// Calculates the numeric value for a Bates number at the given index.
    /// </summary>
    /// <param name="config">Bates number configuration.</param>
    /// <param name="currentIndex">Zero-based index of the current document.</param>
    /// <returns>The numeric Bates value.</returns>
    private static long CalculateValue(BatesNumberConfig config, long currentIndex)
    {
        checked
        {
            return config.Start + (currentIndex * config.Increment);
        }
    }

    /// <summary>
    /// Generates a Bates number with prefix for the given index.
    /// </summary>
    /// <param name="config">Bates number configuration.</param>
    /// <param name="currentIndex">Zero-based index of the current document.</param>
    /// <returns>Formatted Bates number (e.g., "CLIENT00100000001").</returns>
    public static string Generate(BatesNumberConfig config, long currentIndex)
    {
        var number = CalculateValue(config, currentIndex);
        var formattedNumber = number.ToString($"D{config.Digits}");
        return $"{config.Prefix}{formattedNumber}";
    }

    /// <summary>
    /// Generates a Bates number without prefix for the given index.
    /// </summary>
    /// <param name="config">Bates number configuration.</param>
    /// <param name="currentIndex">Zero-based index of the current document.</param>
    /// <returns>Formatted number only (e.g., "00000001").</returns>
    public static string GenerateWithoutPrefix(BatesNumberConfig config, long currentIndex)
    {
        return CalculateValue(config, currentIndex).ToString($"D{config.Digits}");
    }
```

The `checked` block in `CalculateValue` ensures that overflow is detected at runtime — important when generating millions of sequentially-numbered documents. The `D{config.Digits}` format string produces zero-padded numbers like `00000001`.

## 11. Column Profiles — Rich Metadata Generation

The `Profiles/` directory provides a system for generating realistic metadata with configurable columns. The `ColumnProfile` class defines a JSON-serializable schema with data sources, column definitions, and settings. Four built-in profiles are included:

| Profile | Columns | Use Case |
|---------|---------|----------|
| `minimal` | 5 | Basic: DOCID, FILEPATH, CUSTODIAN, DATECREATED, FILESIZE |
| `standard` | 25 | Common eDiscovery fields |
| `litigation` | 50 | Full privilege/responsiveness support |
| `full` | 127 | Maximum coverage |

`DataGenerator` handles value generation for each column type (identifier, text, date, number, boolean, email, coded, longtext). It uses the profile settings and optional seed for reproducible output.

## 12. Loadfile-Only Mode — `LoadfileOnlyGenerator.cs`

When `--loadfile-only` is specified, zipper skips ZIP creation entirely and writes a load file directly to disk. This is the fast path for testing data ingestion tools. The `GenerateAsync()` method streams rows to a file writer, handling both DAT and OPT formats:

```bash
sed -n '12,92p' src/LoadfileOnlyGenerator.cs
```

```output
    /// <summary>
    /// Generates a standalone load file and its companion properties JSON.
    /// </summary>
    /// <param name="request">File generation request with loadfile-only settings.</param>
    /// <returns>Result containing generated file paths and performance metrics.</returns>
    public static async Task<LoadfileOnlyResult> GenerateAsync(FileGenerationRequest request)
    {
        var stopwatch = Stopwatch.StartNew();

        Directory.CreateDirectory(request.OutputPath);

        var baseFileName = $"loadfile_{DateTime.Now:yyyyMMdd_HHmmss}";
        var extension = request.LoadFileFormat == LoadFileFormat.Opt ? ".opt" : ".dat";
        var loadFilePath = Path.Combine(request.OutputPath, $"{baseFileName}{extension}");

        var encoding = EncodingHelper.GetEncodingOrDefault(request.Encoding);
        var eolString = GetEolString(request.EndOfLine);
        int totalLines = request.LoadFileFormat == LoadFileFormat.Opt
            ? (int)request.FileCount // OPT has no header
            : (int)request.FileCount + 1; // DAT: +1 for header

        // Initialize chaos engine if enabled
        ChaosEngine? chaosEngine = null;
        if (request.ChaosMode)
        {
            chaosEngine = new ChaosEngine(
                totalLines,
                request.ChaosAmount,
                request.ChaosTypes,
                request.LoadFileFormat,
                request.ColumnDelimiter,
                request.QuoteDelimiter,
                request.Seed);
        }

        var random = request.Seed.HasValue ? new Random(request.Seed.Value + 1) : new Random();

        // Write the load file
        await using (var fileStream = new FileStream(loadFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            if (request.LoadFileFormat == LoadFileFormat.Opt)
            {
                await WriteOptFile(fileStream, request, encoding, eolString, chaosEngine, random);
            }
            else
            {
                await WriteDatFile(fileStream, request, encoding, eolString, chaosEngine, random);
            }
        }

        // Write the companion properties JSON
        string propertiesPath;
        try
        {
            propertiesPath = await LoadfileAuditWriter.WriteAsync(
                loadFilePath,
                request,
                request.FileCount,
                chaosEngine?.Anomalies);
        }
        catch
        {
            // Clean up partial output on failure
            if (File.Exists(loadFilePath))
            {
                File.Delete(loadFilePath);
            }

            throw;
        }

        stopwatch.Stop();

        return new LoadfileOnlyResult
        {
            LoadFilePath = loadFilePath,
            PropertiesFilePath = propertiesPath,
            TotalRecords = request.FileCount,
            GenerationTime = stopwatch.Elapsed,
        };
    }
```

Key design details in loadfile-only mode:
- Uses a 64KB buffer (`FileStream` with `65536` buffer) for efficient I/O
- Creates a companion `_properties.json` audit file via `LoadfileAuditWriter`
- Initializes the `ChaosEngine` if `--chaos-mode` is enabled (see next section)
- Uses a seeded `Random` instance when `--seed` is provided for reproducibility
- Cleans up partial output on failure

## 13. The Chaos Engine — `ChaosEngine.cs`

The Chaos Engine is a testing tool that deliberately **injects structural anomalies** into load files. This is used to validate that ingestion tools handle corrupted data gracefully.

It works as a **line-level interceptor**: before each line is written, `ShouldIntercept()` checks if it is targeted for corruption. If so, `Intercept()` applies a random anomaly type.

For **DAT** files, anomaly types include:
- `mixed-delimiters` — swaps ASCII delimiters for CSV-style ones
- `quotes` — drops an opening or closing quote mark
- `columns` — adds or removes a column from the row
- `eol` — injects a raw newline into a field value
- `encoding` — inserts invalid bytes between lines

For **OPT** files:
- `opt-boundary` — flips Y/N document boundary markers
- `opt-columns` — removes a comma to shift columns
- `opt-pagecount` — corrupts page count values

```bash
sed -n '80,108p' src/ChaosEngine.cs
```

```output
    /// <summary>
    /// Determines if a line should be intercepted by the chaos engine.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <returns>True if the line should be corrupted.</returns>
    public bool ShouldIntercept(int lineNumber) => this.targetLines.Contains(lineNumber);

    /// <summary>
    /// Intercepts and corrupts a line. Returns the modified line.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <param name="line">Original line content.</param>
    /// <param name="recordId">Record ID (e.g., "DOC00001054" or "HEADER").</param>
    /// <returns>Modified line with injected anomaly.</returns>
    public string Intercept(int lineNumber, string line, string recordId)
    {
        if (this.enabledTypes.Count == 0)
        {
            return line;
        }

        var typeList = this.enabledTypes.ToArray();
        var chosenType = typeList[this.anomalyTypeIndex % typeList.Length];
        this.anomalyTypeIndex++;

        return this.format == LoadFileFormat.Opt
            ? this.ApplyOptChaos(lineNumber, line, recordId, chosenType)
            : this.ApplyDatChaos(lineNumber, line, recordId, chosenType);
    }
```

The chaos engine pre-selects target line numbers at construction time (`SelectTargetLines`), then cycles through enabled anomaly types round-robin. Each anomaly is recorded as a `ChaosAnomaly` object so it can be written to the audit JSON file.

## 14. Performance Monitoring — `PerformanceMonitor.cs`

Throughout generation, `PerformanceMonitor` tracks throughput metrics using `Stopwatch` and `Interlocked` counters for thread-safe updates. It throttles progress output to every 100ms and only when percentage changes by at least 1%%, keeping the console readable even at high throughput. It is disabled in CI environments (when `CI=true`) to prevent output buffering issues.

## 15. Architecture Summary

Here is the complete data flow through zipper:

```
CLI args
  │
  ▼
CommandLineValidator.ValidateAndParseArguments()
  │
  ▼
FileGenerationRequest (immutable DTO)
  │
  ├──[--loadfile-only]──▶ LoadfileOnlyGenerator.GenerateAsync()
  │                           │
  │                           ├──▶ WriteDatFile() / WriteOptFile()
  │                           │      └──▶ ChaosEngine (if --chaos-mode)
  │                           └──▶ LoadfileAuditWriter.WriteAsync()
  │
  └──[default]──▶ ParallelFileGenerator.GenerateFilesAsync()
                       │
                       ├──▶ CreateWorkChannel()     ─── FileWorkItem ──▶
                       │     (assigns folders via FileDistributionHelper)
                       │
                       ├──▶ ProcessFileWorkAsync() × N workers
                       │     └──▶ GenerateFileData()
                       │           ├── PlaceholderFiles.GetContent()
                       │           ├── OfficeFileGenerator.GenerateContent()
                       │           ├── EmlGenerationService.GenerateEmlContent()
                       │           └── TiffMultiPageGenerator.Generate()
                       │
                       └──▶ ZipArchiveService.CreateArchiveAsync()  [consumer]
                             ├── WriteFileToArchive()
                             ├── WriteExtractedTextToArchive()
                             ├── WriteAttachmentToArchive()
                             └── LoadFileWriterFactory.CreateWriter()
                                   ├── DatWriter → LoadFileGenerator
                                   ├── OptWriter
                                   ├── CsvWriter
                                   ├── ConcordanceWriter
                                   └── XmlLoadFileWriter
```

The design prioritizes:
1. **Throughput** — pre-computed content, parallel generation, bounded channels for backpressure
2. **Correctness** — valid file formats, proper MIME encoding, deterministic seeds
3. **Flexibility** — factory patterns for load file formats, pluggable column profiles, chaos injection
4. **Testability** — immutable DTOs, seeded randomness, static methods with explicit dependencies
