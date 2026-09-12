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
