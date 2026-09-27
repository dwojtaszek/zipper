using System.Text.Json;
using Xunit;
using Zipper.Config;
using Zipper.Validation;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ProductionSetValidationTests : IDisposable
{
    private readonly string testOutputPath;

    public ProductionSetValidationTests()
    {
        this.testOutputPath = Path.Combine(Directory.GetCurrentDirectory(), $"zipper_validation_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.testOutputPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.testOutputPath))
        {
            Directory.Delete(this.testOutputPath, true);
        }
    }

    private FileGenerationRequest CreateTestRequest(int count = 5)
    {
        return new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.testOutputPath,
                FileCount = count,
                FileType = "pdf",
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 10,
            },
            Metadata = new MetadataConfig { Seed = 42 },
            Bates = new BatesNumberConfig
            {
                Prefix = "TEST",
                Start = 1,
                Digits = 8,
            },
        };
    }

    [Fact]
    public async Task Validate_ValidProductionSet_ShouldWritePassingReport()
    {
        var request = this.CreateTestRequest();
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var reportPath = Path.Combine(result.ProductionPath, "_validation_report.json");
        Assert.True(File.Exists(reportPath));

        var json = await File.ReadAllTextAsync(reportPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("passed", root.GetProperty("status").GetString());
        Assert.Equal(0, root.GetProperty("errorCount").GetInt32());
        Assert.Equal(0, root.GetProperty("warningCount").GetInt32());

        var fileCounts = root.GetProperty("checkedFileCounts");
        Assert.Equal(1, fileCounts.GetProperty("dat").GetInt32());
        Assert.Equal(1, fileCounts.GetProperty("opt").GetInt32());
        Assert.Equal(5, fileCounts.GetProperty("native").GetInt32());

        // Verify manifest references report
        var manifestJson = await File.ReadAllTextAsync(result.ManifestPath);
        using var manifestDoc = JsonDocument.Parse(manifestJson);
        Assert.Equal("_validation_report.json", manifestDoc.RootElement.GetProperty("validationReport").GetString());
    }

    [Fact]
    public async Task Validate_ImageReferencesInBothLoadFiles_ShouldCountBothInReport()
    {
        var request = this.CreateTestRequest(count: 3);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var datLines = await File.ReadAllLinesAsync(result.DatFilePath);
        var optLines = await File.ReadAllLinesAsync(result.OptFilePath);
        Assert.Equal(3, datLines.Length - 1);
        Assert.Equal(3, optLines.Length);
        var columnDelimiter = request.Delimiters.GetColumnChar();
        var quoteDelimiter = request.Delimiters.GetQuoteChar();
        var headers = ProductionSetPostValidator.ParseDatLine(datLines[0], columnDelimiter, quoteDelimiter);
        var imageIndex = headers.IndexOf("IMAGE_PATH");
        Assert.True(imageIndex >= 0);
        Assert.All(datLines.Skip(1), line =>
            Assert.False(string.IsNullOrEmpty(ProductionSetPostValidator.ParseDatLine(line, columnDelimiter, quoteDelimiter)[imageIndex])));
        Assert.All(optLines, line => Assert.False(string.IsNullOrEmpty(line.Split(',')[2])));

        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);
        Assert.Equal("passed", report.Status);
        Assert.Equal(6, report.CheckedFileCounts["image"]);

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(result.ProductionPath, "_validation_report.json")));
        Assert.Equal(6, document.RootElement.GetProperty("checkedFileCounts").GetProperty("image").GetInt32());

        var columns = optLines[0].Split(',');
        columns[2] = string.Empty;
        optLines[0] = string.Join(',', columns);
        await File.WriteAllLinesAsync(result.OptFilePath, optLines);

        var reportWithEmptyOptPath = ProductionSetPostValidator.Validate(result.ProductionPath, request);
        Assert.Equal("passed", reportWithEmptyOptPath.Status);
        Assert.Equal(5, reportWithEmptyOptPath.CheckedFileCounts["image"]);
    }

    [Fact]
    public async Task Validate_MissingNativeFile_ShouldCreateFailedReport()
    {
        var request = this.CreateTestRequest(count: 3);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        // Delete a native file to simulate a missing file
        var nativeFiles = Directory.GetFiles(Path.Combine(result.ProductionPath, "NATIVES"), "*.pdf", SearchOption.AllDirectories);
        Assert.NotEmpty(nativeFiles);
        File.Delete(nativeFiles[0]);

        // Run validator directly
        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);

        Assert.Equal("failed", report.Status);
        Assert.True(report.ErrorCount > 0);
        Assert.Contains(report.Findings, f => f.Code == "PathExistence" && f.Path == "DATA/loadfile.dat" && f.Message.Contains("does not exist", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_DuplicateDocId_ShouldCreateFailedReport()
    {
        var request = this.CreateTestRequest(count: 3);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        // Modify DAT file to introduce a duplicate DOCID on line 3
        var datLines = await File.ReadAllLinesAsync(result.DatFilePath);
        Assert.True(datLines.Length >= 4);

        // Duplicate the DOCID of first data row into the second data row
        var colDelim = request.Delimiters.ColumnDelimiter[0];
        var quoteDelim = request.Delimiters.QuoteDelimiter[0];

        var firstRowFields = ProductionSetPostValidator.ParseDatLine(datLines[1], colDelim, quoteDelim);
        var secondRowFields = ProductionSetPostValidator.ParseDatLine(datLines[2], colDelim, quoteDelim);

        // replace second row DOCID with first row DOCID
        secondRowFields[0] = firstRowFields[0];

        // Re-construct the line
        var quoteStr = quoteDelim.ToString();
        var colStr = colDelim.ToString();
        var reconstructedLine = string.Join(colStr, secondRowFields.Select(f => $"{quoteStr}{f}{quoteStr}"));
        datLines[2] = reconstructedLine;

        await File.WriteAllLinesAsync(result.DatFilePath, datLines);

        // Run validator directly
        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);

        Assert.Equal("failed", report.Status);
        Assert.True(report.ErrorCount > 0);
        Assert.Contains(report.Findings, f => f.Code == "UniqueId" && f.Path == "DATA/loadfile.dat" && f.Message.Contains("Duplicate DOCID", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_OptLineWithInvalidColumnCount_ShouldCreateFailedReport()
    {
        var request = this.CreateTestRequest(count: 3);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        // Modify OPT file to have a line with 8 columns
        var optLines = await File.ReadAllLinesAsync(result.OptFilePath);
        Assert.NotEmpty(optLines);
        optLines[0] = optLines[0] + ",EXTRA_COL";

        await File.WriteAllLinesAsync(result.OptFilePath, optLines);

        // Run validator directly
        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);

        Assert.Equal("failed", report.Status);
        Assert.True(report.ErrorCount > 0);
        Assert.Contains(report.Findings, f => f.Code == "OptBoundary" && f.Path == "DATA/loadfile.opt" && f.Message.Contains("columns, expected 7", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_BrokenBatesSequence_ShouldCreateFailedReport()
    {
        var request = this.CreateTestRequest(count: 3);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        // Modify DAT file to have a broken Bates number
        var datLines = await File.ReadAllLinesAsync(result.DatFilePath);
        Assert.True(datLines.Length >= 3);

        var colDelim = request.Delimiters.ColumnDelimiter[0];
        var quoteDelim = request.Delimiters.QuoteDelimiter[0];

        var fields = ProductionSetPostValidator.ParseDatLine(datLines[2], colDelim, quoteDelim);
        fields[1] = "TEST99999999"; // Bates range is at column index 1

        var quoteStr = quoteDelim.ToString();
        var colStr = colDelim.ToString();
        var reconstructedLine = string.Join(colStr, fields.Select(f => $"{quoteStr}{f}{quoteStr}"));
        datLines[2] = reconstructedLine;

        await File.WriteAllLinesAsync(result.DatFilePath, datLines);

        // Run validator directly
        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);

        Assert.Equal("failed", report.Status);
        Assert.True(report.ErrorCount > 0);
        Assert.Contains(report.Findings, f => f.Code == "BatesConsistency" && f.Path == "DATA/loadfile.dat" && f.Message.Contains("Bates range inconsistency", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_OneMissingFilePerKind_ShouldAttributeEachToItsOwnLoadFileAndLine()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.testOutputPath,
                FileCount = 8,
                FileTypeRatios = new List<FileTypeRatio>
                {
                    new() { Type = "pdf", Weight = 4 },
                    new() { Type = "tiff", Weight = 4 },
                },
                WithText = true,
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 10,
                RedactedProduction = true,
            },
            Metadata = new MetadataConfig { Seed = 42 },
            Bates = new BatesNumberConfig
            {
                Prefix = "TEST",
                Start = 1,
                Digits = 8,
            },
        };

        var result = await ProductionSetGenerator.GenerateAsync(request);

        // Delete exactly one file of each referenced kind, so every reference check has work
        // to do and all four call sites of the referenced-file check produce a finding.
        //
        // The directory AND the extension are both pinned. REDACTED/ holds both an IMAGES and
        // a TEXT subtree, so taking whichever file enumerated first would make the expected
        // kind depend on filesystem order: Directory.GetFiles guarantees no ordering, and NTFS
        // returns sorted entries, so Windows would hand back a redacted image where Linux
        // hands back redacted text. Pinning both makes the kind deterministic everywhere.
        var deleted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["native"] = DeleteFirstFile(result.ProductionPath, "*.pdf", "NATIVES"),
            ["text"] = DeleteFirstFile(result.ProductionPath, "*.txt", "TEXT"),
            ["redacted text"] = DeleteFirstFile(result.ProductionPath, "*.txt", "REDACTED", "TEXT"),
            ["redacted image"] = DeleteFirstFile(result.ProductionPath, "*.tif", "REDACTED", "IMAGES"),
            ["image"] = DeleteFirstFile(result.ProductionPath, "*.tif", "IMAGES"),
        };

        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);

        Assert.Equal("failed", report.Status);

        // The image reference appears in both Load Files, so it is expected twice: once
        // attributed to the DAT Load File and once to the OPT Load File.
        var expectations = new (string Kind, string LoadFile)[]
        {
            ("native", "DATA/loadfile.dat"),
            ("text", "DATA/loadfile.dat"),
            ("redacted text", "DATA/loadfile.dat"),
            ("redacted image", "DATA/loadfile.dat"),
            ("image", "DATA/loadfile.dat"),
            ("image", "DATA/loadfile.opt"),
        };

        foreach (var (kind, loadFile) in expectations)
        {
            var referencedPath = deleted[kind];
            var line = await FindReferenceLineAsync(
                Path.Combine(result.ProductionPath, loadFile.Replace('/', Path.DirectorySeparatorChar)),
                referencedPath);

            Assert.Single(report.Findings, f =>
                f.Code == "PathExistence" &&
                f.Severity == "error" &&
                f.Path == loadFile &&
                f.Line == line &&
                f.Message == $"Referenced {kind} file '{referencedPath}' does not exist.");
        }

        // Exactly one finding per expectation, and no others: a count catches a spurious or
        // duplicated finding that the per-expectation Assert.Single would not, because that
        // only requires at least one match somewhere in the list.
        Assert.Equal(expectations.Length, report.Findings.Count(f => f.Code == "PathExistence"));
    }

    private static string DeleteFirstFile(string productionPath, string searchPattern, params string[] relativeDirectory)
    {
        var files = Directory.GetFiles(
            Path.Combine([productionPath, .. relativeDirectory]),
            searchPattern,
            SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        // Keep the production-relative path in the separator and case the Load File uses,
        // because that exact string is echoed back in the finding message. Load Files store
        // Windows-style separators on every platform, so normalize with '/' rather than
        // Path.DirectorySeparatorChar (a no-op on Windows, per the repo's Code Style rule).
        var referencedPath = Path.GetRelativePath(productionPath, files[0]).Replace('/', '\\');
        File.Delete(files[0]);
        return referencedPath;
    }

    [Fact]
    public async Task Validate_MultipageTiffProductionSet_ShouldCountImageReferencesFromBothLoadFiles()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.testOutputPath,
                FileCount = 3,
                FileType = "tiff",
            },
            Tiff = new TiffConfig { PageRange = (3, 3) },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 10,
            },
            Metadata = new MetadataConfig { Seed = 42 },
            Bates = new BatesNumberConfig
            {
                Prefix = "TEST",
                Start = 1,
                Digits = 8,
            },
        };

        var result = await ProductionSetGenerator.GenerateAsync(request);
        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);

        // Read the image references independently out of each Load File rather than trusting the
        // counter, then assert the sum.
        //
        // The page range matters: with single-page records the DAT and the OPT name the SAME
        // file, one reference each, so the two halves of this assertion are indistinguishable
        // and any per-page counting bug goes unnoticed. Forcing 3 pages makes the OPT emit one
        // record per page (3 per Native File) while the DAT still emits one IMAGE_PATH per Native
        // File, so the two counts must differ. Without that, a counter that only fired on the
        // first page of each Native File would still pass.
        var datImagePaths = await ReadDatImagePathsAsync(result.DatFilePath, request);
        var optImagePaths = await ReadOptImagePathsAsync(result.OptFilePath);

        // The counts do not move when a page image goes missing, so a broken Production Set would
        // otherwise satisfy every count assertion below on a report that failed validation.
        Assert.Equal("passed", report.Status);

        // The DAT half is pinned to an exact value, which is what makes it an independent check
        // of the validator rather than a restatement of it. PageRange (3, 3) is deterministic --
        // min == max short-circuits the page count -- so the OPT half is pinned the same way; a
        // producer that emitted the wrong number of pages per Native File must not pass.
        Assert.Equal(request.Output.FileCount, datImagePaths.Count);
        Assert.Equal(request.Output.FileCount * 3, optImagePaths.Count);

        // Kept alongside the exact anchor because it catches a different defect: an OPT column
        // reorder would leave this helper reading DocBreak instead of ImagePath, returning one
        // reference per Native File and tripping the comparison even though the totals line up.
        Assert.True(
            optImagePaths.Count > datImagePaths.Count,
            $"multipage TIFFs must make the OPT emit more image references than the DAT; got DAT={datImagePaths.Count}, OPT={optImagePaths.Count}");

        Assert.Equal(datImagePaths.Count + optImagePaths.Count, report.CheckedFileCounts["image"]);

        // The counts alone do not pin the page-one fallback the report documents, so assert the
        // structure directly: a multipage Native File's DAT row names the BASE path, which is
        // never written to disk, while the OPT names each _NNN page, which is. Deleting the
        // fallback branch that resolves the missing base path would leave every count above green.
        //
        // Both Load Files carry backslash-separated paths, so normalize exactly as the validator
        // does before touching the filesystem. Skipping that step makes every existence check
        // below vacuously true on a non-Windows host, where a backslash is an ordinary character.
        static string OnDisk(string productionPath, string loadFilePath) =>
            Path.Combine(productionPath, loadFilePath.Replace('\\', '/'));

        foreach (var basePath in datImagePaths)
        {
            Assert.False(
                File.Exists(OnDisk(result.ProductionPath, basePath)),
                $"the DAT base path must not exist literally for a multipage Native File: {basePath}");
        }

        foreach (var pagePath in optImagePaths)
        {
            Assert.True(
                File.Exists(OnDisk(result.ProductionPath, pagePath)),
                $"the OPT must name a page image that exists on disk: {pagePath}");
            // Strip the _NNN page suffix and restore the extension: what is left must be a DAT
            // base path. This is the relationship that makes the two counts describe different
            // files for the same Native File, and it is why a resolver must apply the same
            // page-one fallback to a DAT row.
            var suffixIndex = pagePath.LastIndexOf('_');
            Assert.True(suffixIndex > 0, $"the OPT page path must carry a _NNN page suffix: {pagePath}");
            Assert.Contains(pagePath[..suffixIndex] + Path.GetExtension(pagePath), datImagePaths);
        }

        // Each Native File contributes exactly one image per page, so the page suffixes are
        // 001..003 repeated once per Native File. The suffix sits before the extension, so
        // compare against the file name without it.
        foreach (var pageSuffix in new[] { "_001", "_002", "_003" })
        {
            Assert.Equal(
                request.Output.FileCount,
                optImagePaths.Count(path => Path.GetFileNameWithoutExtension(path)
                    .EndsWith(pageSuffix, StringComparison.Ordinal)));
        }
    }

    private static async Task<List<string>> ReadDatImagePathsAsync(string datPath, FileGenerationRequest request)
    {
        var lines = await File.ReadAllLinesAsync(datPath);
        var colDelim = request.Delimiters.GetColumnChar();
        var quoteDelim = request.Delimiters.GetQuoteChar();

        var headers = ProductionSetPostValidator.ParseDatLine(lines[0], colDelim, quoteDelim);
        var imageIdx = headers.FindIndex(h => string.Equals(h, "IMAGE_PATH", StringComparison.OrdinalIgnoreCase));
        Assert.True(imageIdx >= 0, "the DAT Load File should have an IMAGE_PATH column");

        var paths = new List<string>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = ProductionSetPostValidator.ParseDatLine(line, colDelim, quoteDelim);
            if (imageIdx < fields.Count && !string.IsNullOrEmpty(fields[imageIdx]))
            {
                paths.Add(fields[imageIdx]);
            }
        }

        return paths;
    }

    private static async Task<List<string>> ReadOptImagePathsAsync(string optPath)
    {
        var lines = await File.ReadAllLinesAsync(optPath);

        // The OPT is a fixed comma-delimited legacy format and does NOT use the configurable
        // DelimiterConfig.ColumnDelimiter that the DAT uses, so it is split on a literal comma.
        // It has no header: fixed positional fields, with ImagePath third. The OPT applies no
        // quoting or escaping at all, which is a limitation of the format rather than a courtesy
        // -- a comma inside any value would corrupt the file, and this helper and ValidateOptRow
        // would then mis-parse it identically, so the test would agree with the bug. Image paths
        // are planner-derived (volume name plus Bates number) and cannot contain a comma today.
        var paths = new List<string>();
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var fields = line.Split(',');
            if (fields.Length > 2 && !string.IsNullOrEmpty(fields[2]))
            {
                paths.Add(fields[2]);
            }
        }

        return paths;
    }

    /// <summary>
    /// The physical 1-based line in a Load File that names <paramref name="referencedPath"/>,
    /// counted by scanning raw newlines rather than by enumerating parsed lines. Deriving it
    /// independently of the validator's own line counting is the point: if the validator
    /// propagated a line number off by one, a test that reused its counting would agree with
    /// the bug instead of catching it.
    /// </summary>
    private static async Task<long> FindReferenceLineAsync(string loadFilePath, string referencedPath)
    {
        var content = await File.ReadAllTextAsync(loadFilePath);
        var offset = content.IndexOf(referencedPath, StringComparison.OrdinalIgnoreCase);
        if (offset < 0)
        {
            Assert.Fail($"'{loadFilePath}' does not reference '{referencedPath}'.");
        }

        var newlinesBefore = 0;
        for (var i = 0; i < offset; i++)
        {
            if (content[i] == '\n')
            {
                newlinesBefore++;
            }
        }

        return newlinesBefore + 1;
    }

    [Fact]
    public void ParseDatLine_WithDoubledQuotes_ShouldUnescapeLiteralQuotes()
    {
        var lineInput = "\"value1\",\"value \"\"2\"\" hello\",\"value3\"";
        var fields = ProductionSetPostValidator.ParseDatLine(lineInput, ',', '"');

        Assert.Equal(3, fields.Count);
        Assert.Equal("value1", fields[0]);
        Assert.Equal("value \"2\" hello", fields[1]);
        Assert.Equal("value3", fields[2]);
    }

    [Fact]
    public async Task Validate_QuoteDelimNone_WithThorn_ShouldNotFailColumnCount()
    {
        var request = this.CreateTestRequest(count: 1);
        request.Delimiters = new DelimiterConfig
        {
            QuoteDelimiter = string.Empty,
        };

        var result = await ProductionSetGenerator.GenerateAsync(request);

        // Modify DAT file to contain a thorn in the middle of a field
        var datLines = await File.ReadAllLinesAsync(result.DatFilePath);
        var colDelim = request.Delimiters.ColumnDelimiter[0];

        // The generator won't quote anything because QuoteDelimiter is empty.
        // We inject a thorn inside a value to simulate it
        var fields = datLines[1].Split(colDelim).ToList();
        fields[0] = fields[0] + "\xfe"; // append thorn to the DOCID field
        datLines[1] = string.Join(colDelim.ToString(), fields);

        await File.WriteAllLinesAsync(result.DatFilePath, datLines);

        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);
        Assert.DoesNotContain(report.Findings, f => f.Code == "ColumnCount");
    }

    [Fact]
    public async Task Validate_ManifestBatesMismatch_ShouldCreateFailedReport()
    {
        // Issue #822 regression: Manifest start does not match DAT first Bates
        var request = this.CreateTestRequest(count: 1);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        // Modify DAT file: replace BATES_NUMBER with a mismatched Bates number
        var datLines = await File.ReadAllLinesAsync(result.DatFilePath);
        Assert.True(datLines.Length >= 2);

        var colDelim = request.Delimiters.ColumnDelimiter[0];
        var quoteDelim = request.Delimiters.QuoteDelimiter[0];
        var fields = ProductionSetPostValidator.ParseDatLine(datLines[1], colDelim, quoteDelim);

        var headers = ProductionSetPostValidator.ParseDatLine(datLines[0], colDelim, quoteDelim);
        int batesIdx = headers.FindIndex(h => string.Equals(h, "BATES_NUMBER", StringComparison.OrdinalIgnoreCase));
        Assert.True(batesIdx >= 0);

        fields[batesIdx] = "TEST00000099";

        var quoteStr = quoteDelim.ToString();
        var colStr = colDelim.ToString();
        datLines[1] = string.Join(colStr, fields.Select(f => $"{quoteStr}{f}{quoteStr}"));
        await File.WriteAllLinesAsync(result.DatFilePath, datLines);

        // Validator should detect that DAT first Bates number does not match manifest start
        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);

        Assert.Equal("failed", report.Status);
        Assert.True(report.ErrorCount > 0);
        Assert.Contains(report.Findings, f => f.Code == "BatesConsistency" && f.Message.Contains("does not match manifest start", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_ManifestEndBatesMismatch_ShouldCreateFailedReport()
    {
        var request = this.CreateTestRequest(count: 3);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var datLines = await File.ReadAllLinesAsync(result.DatFilePath);
        Assert.True(datLines.Length >= 4);

        var colDelim = request.Delimiters.ColumnDelimiter[0];
        var quoteDelim = request.Delimiters.QuoteDelimiter[0];
        var headers = ProductionSetPostValidator.ParseDatLine(datLines[0], colDelim, quoteDelim);
        int batesIdx = headers.FindIndex(h => string.Equals(h, "BATES_NUMBER", StringComparison.OrdinalIgnoreCase));
        Assert.True(batesIdx >= 0);

        // Mutate last row only
        var fields = ProductionSetPostValidator.ParseDatLine(datLines[3], colDelim, quoteDelim);
        fields[batesIdx] = "TEST00000099";

        var quoteStr = quoteDelim.ToString();
        var colStr = colDelim.ToString();
        datLines[3] = string.Join(colStr, fields.Select(f => $"{quoteStr}{f}{quoteStr}"));
        await File.WriteAllLinesAsync(result.DatFilePath, datLines);

        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);

        Assert.Equal("failed", report.Status);
        Assert.True(report.ErrorCount > 0);
        Assert.Contains(report.Findings, f => f.Code == "BatesConsistency" && f.Message.Contains("does not match manifest end", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Validate_CorruptManifest_ShouldCreateFailedReport()
    {
        var request = this.CreateTestRequest(count: 2);
        var result = await ProductionSetGenerator.GenerateAsync(request);

        var manifestPath = Path.Combine(result.ProductionPath, "_manifest.json");
        await File.WriteAllTextAsync(manifestPath, "{ invalid json content");

        var report = ProductionSetPostValidator.Validate(result.ProductionPath, request);

        Assert.Equal("failed", report.Status);
        Assert.True(report.ErrorCount > 0);
        Assert.Contains(report.Findings, f => f.Code == "ManifestSyntax");
    }

    [Fact]
    public async Task Validate_RollingSet2_ResolvesEffectiveBatesDirectly()
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = this.testOutputPath,
                FileCount = 3,
                FileType = "pdf",
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 10,
                ProductionId = "DIRVAL",
                RollingCount = 2,
                RollingBatesMode = RollingBatesMode.Continuous,
            },
            Bates = new BatesNumberConfig { Prefix = "VAL", Start = 100, Digits = 8 },
        };

        var result = await ProductionSetGenerator.GenerateAsync(request);
        Assert.NotNull(result);

        var dir2 = Path.Combine(this.testOutputPath, "DIRVAL_2");
        Assert.True(Directory.Exists(dir2));

        // Validate Set 2 directly using the root un-sliced request
        var report = ProductionSetPostValidator.Validate(dir2, request);

        Assert.Equal("passed", report.Status);
        Assert.Equal(0, report.ErrorCount);
    }
}
