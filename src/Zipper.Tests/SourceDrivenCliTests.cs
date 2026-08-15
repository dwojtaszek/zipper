using Xunit;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class SourceDrivenCliTests : IDisposable
{
    private readonly string tempDir;

    public SourceDrivenCliTests()
    {
        this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"zipper_source_cli_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, true);
        }
    }

    private string WriteCsv(string content, string name = "input.csv")
    {
        var path = Path.Combine(this.tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string OutputDir(string name = "out")
    {
        var path = Path.Combine(this.tempDir, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CaptureError(Action action)
    {
        var original = Console.Error;
        using var writer = new StringWriter();
        Console.SetError(writer);
        try
        {
            action();
        }
        finally
        {
            Console.SetError(original);
        }

        return writer.ToString();
    }

    [Fact]
    public void Parse_InputCsvAndDirectoryTemplate_StoreRawValues()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--input-csv", "rows.csv", "--output-path", this.tempDir });

        Assert.True(ok);
        Assert.Equal("rows.csv", modules.SourceInput.InputCsv);

        var (okDir, modulesDir) = PipelineTestHelper.Parse(new[] { "--directory-template", "tpl", "--output-path", this.tempDir });

        Assert.True(okDir);
        Assert.Equal("tpl", modulesDir.SourceInput.DirectoryTemplate);
    }

    [Fact]
    public void Build_InputCsv_SetsCountTypeAndSourceFileTypesFromRows()
    {
        var csv = this.WriteCsv("FilePath,FileType\na/x.pdf,pdf\nb.eml,eml\nc.tiff,tiff\n");
        var args = new[] { "--input-csv", csv, "--output-path", this.OutputDir() };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.Equal(3, request!.Output.FileCount);
        Assert.Equal("pdf", request.Output.FileType);
        Assert.Equal(new[] { "eml", "pdf", "tiff" }, request.Output.SourceFileTypes!.ToArray());
        Assert.True(request.Output.IsMixedFileTypes);
        Assert.Equal(3, request.SourceRecords!.Count);
        Assert.Equal("a/x.pdf", request.SourceRecords[0].RelativePath);
    }

    [Fact]
    public void Build_InputCsvSingleType_IsNotMixed()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\nb.pdf,pdf\n");
        var args = new[] { "--input-csv", csv, "--output-path", this.OutputDir() };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.False(request!.Output.IsMixedFileTypes);
        Assert.Equal(new[] { "pdf" }, request.Output.SourceFileTypes!.ToArray());
    }

    [Fact]
    public void Build_InputCsvWithoutTypeAndCount_Succeeds()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\n");
        var args = new[] { "--input-csv", csv, "--output-path", this.OutputDir() };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.Equal(1, request!.Output.FileCount);
    }

    [Fact]
    public void Build_InputCsvWithMatchingCount_Succeeds()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\nb.pdf,pdf\n");
        var args = new[] { "--input-csv", csv, "--count", "2", "--output-path", this.OutputDir() };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.Equal(2, request!.Output.FileCount);
    }

    [Fact]
    public void Build_InputCsvWithMismatchedCount_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\nb.pdf,pdf\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--count", "5", "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("--count", error, StringComparison.Ordinal);
        Assert.Contains("5", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_InputCsvAndDirectoryTemplateTogether_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--directory-template", this.tempDir, "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("--input-csv", error, StringComparison.Ordinal);
        Assert.Contains("--directory-template", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_InputCsvWithType_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--type", "pdf", "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("--type", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_InputCsvWithTypes_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--types", "pdf:1", "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("--types", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_InputCsvWithProductionSet_Succeeds()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\nsub/b.docx,docx\n");

        var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--production-set", "--bates-prefix", "PROD", "--output-path", this.OutputDir() });

        Assert.NotNull(request);
        Assert.True(request!.Production.ProductionSet);
        Assert.Equal(2, request.SourceRecords!.Count);
        Assert.Equal(2, request.Output.FileCount);
        Assert.Equal(Zipper.Config.SourcePathMode.Bates, request.Production.SourcePathMode);
    }

    [Fact]
    public void Build_InputCsvBatesColumnWithProductionSet_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType,BatesNumber\na.pdf,pdf,ABC_00000001\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--production-set", "--bates-prefix", "ABC", "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("BatesNumber", error, StringComparison.Ordinal);
        Assert.Contains("--production-set", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SourcePathModeWithProductionSet_MapsToEnum()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\n");
        var args = new[] { "--input-csv", csv, "--production-set", "--source-path-mode", "originals", "--bates-prefix", "PROD", "--output-path", this.OutputDir() };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.Equal(Zipper.Config.SourcePathMode.Originals, request!.Production.SourcePathMode);
    }

    [Fact]
    public void Build_SourcePathModeWithoutProductionSet_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--source-path-mode", "preserve", "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("--source-path-mode", error, StringComparison.Ordinal);
        Assert.Contains("--production-set", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SourcePathModeWithoutSourceInput_ReturnsNull()
    {
        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--production-set", "--source-path-mode", "preserve", "--bates-prefix", "PROD", "--count", "1", "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("--source-path-mode", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_InvalidSourcePathMode_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--production-set", "--source-path-mode", "bogus", "--bates-prefix", "PROD", "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("--source-path-mode", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_InputCsvBatesColumnWithoutBatesPrefix_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType,BatesNumber\na.pdf,pdf,ABC_00000001\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("--bates-prefix", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_InputCsvBatesColumnWithBatesPrefix_Succeeds()
    {
        var csv = this.WriteCsv("FilePath,FileType,BatesNumber\na.pdf,pdf,ABC_00000001\n");
        var args = new[] { "--input-csv", csv, "--bates-prefix", "ABC", "--output-path", this.OutputDir() };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.Equal("ABC_00000001", request!.SourceRecords![0].BatesNumber);
    }

    [Fact]
    public void Build_InputCsvControlNumberCollidingWithGenerated_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType,ControlNumber\na.pdf,pdf,ABC-1\nb.eml,eml,DOC00000002\nc.tiff,tiff,ABC-3\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("collides with the generated Control Number", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_InputCsvControlNumberOutsideGeneratedRange_Succeeds()
    {
        var csv = this.WriteCsv("FilePath,FileType,ControlNumber\na.pdf,pdf,DOC00000005\nb.eml,eml,ABC-2\n");

        var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--output-path", this.OutputDir() });

        Assert.NotNull(request);
    }

    [Fact]
    public void Build_InputCsvBatesNumberCollidingWithGenerated_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType,BatesNumber\na.pdf,pdf,ABC00000002\nb.eml,eml,\nc.tiff,tiff,\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--bates-prefix", "ABC", "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("collides with the generated Bates sequence value", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ABC00000009")] // outside the 3-row generated range
    [InlineData("ABC2")] // numerically in range but different string (padding), no byte collision
    [InlineData("XYZ00000001")] // different prefix
    public void Build_InputCsvBatesNumberOutsideGeneratedSpace_Succeeds(string bates)
    {
        var csv = this.WriteCsv($"FilePath,FileType,BatesNumber\na.pdf,pdf,{bates}\nb.eml,eml,\nc.tiff,tiff,\n");

        var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--bates-prefix", "ABC", "--output-path", this.OutputDir() });

        Assert.NotNull(request);
    }

    [Fact]
    public void Build_InputCsvMissingFile_ReturnsNull()
    {
        var missing = Path.Combine(this.tempDir, "nope.csv");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", missing, "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("does not exist", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_InputCsvPathTraversal_ReturnsNull()
    {
        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", "../outside.csv", "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("traversal", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_InputCsvMalformedRow_ReturnsNull()
    {
        var csv = this.WriteCsv("FilePath,FileType\n../escape.pdf,pdf\n");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--input-csv", csv, "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("Row 2", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_DirectoryTemplate_SetsRecordsFromFiles()
    {
        var template = Path.Combine(this.tempDir, "tpl");
        Directory.CreateDirectory(Path.Combine(template, "sub"));
        File.WriteAllText(Path.Combine(template, "root.pdf"), "x");
        File.WriteAllText(Path.Combine(template, "sub", "inner.eml"), "x");

        var args = new[] { "--directory-template", template, "--output-path", this.OutputDir() };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.Equal(2, request!.Output.FileCount);
        Assert.Equal(new[] { "eml", "pdf" }, request.Output.SourceFileTypes!.ToArray());
        Assert.True(request.Output.IsMixedFileTypes);
        Assert.Equal("root.pdf", request.SourceRecords![0].RelativePath);
        Assert.Equal("sub/inner.eml", request.SourceRecords[1].RelativePath);
    }

    [Fact]
    public void Build_DirectoryTemplateMissing_ReturnsNull()
    {
        var missing = Path.Combine(this.tempDir, "no-template");

        var error = CaptureError(() =>
        {
            var request = Cli.Pipeline.Build(new[] { "--directory-template", missing, "--output-path", this.OutputDir() });
            Assert.Null(request);
        });

        Assert.Contains("does not exist", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_InputCsvWithJpgRow_DefaultsToDatAndOpt()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.jpg,jpg\nb.pdf,pdf\n");
        var args = new[] { "--input-csv", csv, "--output-path", this.OutputDir() };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.Equal(2, request!.LoadFile.Formats.Count);
        Assert.Contains(LoadFileFormat.Dat, request.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Opt, request.LoadFile.Formats);
    }
}
