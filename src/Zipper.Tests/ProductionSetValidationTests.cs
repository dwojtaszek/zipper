using System.IO;
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
}
