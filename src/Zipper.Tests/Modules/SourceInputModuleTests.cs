using Xunit;
using Zipper.Cli.Modules;
using Zipper.Config;
using Zipper.SourceInput;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class SourceInputModuleTests : IDisposable
{
    private readonly string tempDir;

    public SourceInputModuleTests()
    {
        this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"zipper_source_input_{Guid.NewGuid():N}");
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

    private static bool TryBuild(
        string?[] apply,
        out IReadOnlyList<SourceRecord>? records,
        long? count = null,
        bool productionSet = false,
        BatesNumberConfig? bates = null)
    {
        var module = new SourceInputModule();
        for (int i = 0; i < apply.Length; i += 2)
        {
            Assert.True(module.TryApply(apply[i]!, apply[i + 1]));
        }
        return module.TryBuild(count, productionSet, bates, out records);
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

    // --- TryApply raw storage ---

    [Fact]
    public void TryApply_InputCsv_StoresValue()
    {
        var module = new SourceInputModule();
        Assert.True(module.TryApply("--input-csv", "rows.csv"));
        Assert.Equal("rows.csv", module.InputCsv);
        Assert.True(module.HasSourceInput);
    }

    [Fact]
    public void TryApply_DirectoryTemplate_StoresValue()
    {
        var module = new SourceInputModule();
        Assert.True(module.TryApply("--directory-template", "tpl"));
        Assert.Equal("tpl", module.DirectoryTemplate);
        Assert.True(module.HasSourceInput);
    }

    [Fact]
    public void TryApply_MissingValue_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(new SourceInputModule().TryApply("--input-csv", null));
        });
        Assert.Contains("Error: --input-csv requires a value.", error, StringComparison.Ordinal);

        error = CaptureError(() =>
        {
            Assert.False(new SourceInputModule().TryApply("--directory-template", null));
        });
        Assert.Contains("Error: --directory-template requires a value.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApply_UnknownFlag_ReturnsFalse()
    {
        Assert.False(new SourceInputModule().TryApply("--unknown-flag", "x"));
    }

    [Fact]
    public void TakesValue_OwnedFlags_ReturnsTrue()
    {
        var module = new SourceInputModule();
        Assert.True(module.TakesValue("--input-csv"));
        Assert.True(module.TakesValue("--directory-template"));
    }

    // --- TryBuild source-reading gate ---

    [Fact]
    public void TryBuild_NoSourceInput_ReturnsTrueWithNullRecords()
    {
        var module = new SourceInputModule();
        Assert.True(module.TryBuild(null, false, null, out var records));
        Assert.Null(records);
    }

    [Fact]
    public void TryBuild_CsvAndDirectoryTogether_ReturnsFalse()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\n");

        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--input-csv", csv, "--directory-template", this.tempDir }, out _));
        });
        Assert.Contains("Error: --input-csv and --directory-template cannot be used together.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_PathTraversal_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--input-csv", "../outside.csv" }, out _));
        });
        Assert.Contains("Error: Path traversal detected in source input path '../outside.csv'. Source input must reside within working directory.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_MissingCsv_ReturnsFalse()
    {
        var missing = Path.Combine(this.tempDir, "nope.csv");

        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--input-csv", missing }, out _));
        });
        Assert.Contains($"Error: Source CSV '{missing}' does not exist.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_MissingDirectory_ReturnsFalse()
    {
        var missing = Path.Combine(this.tempDir, "no-template");

        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--directory-template", missing }, out _));
        });
        Assert.Contains($"Error: Directory template '{missing}' does not exist.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_MalformedRow_ReturnsFalse()
    {
        var csv = this.WriteCsv("FilePath,FileType\n../escape.pdf,pdf\n");

        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--input-csv", csv }, out _));
        });
        Assert.Contains("Error: Row 2: invalid FilePath:", error, StringComparison.Ordinal);
    }

    // --- TryBuild row-count and Bates-column checks ---

    [Fact]
    public void TryBuild_CountMismatch_ReturnsFalse()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\nb.pdf,pdf\n");

        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--input-csv", csv }, out _, count: 5));
        });
        Assert.Contains("Error: --count (5) does not match the Source Record count (2). Align --count with the source input or omit it.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_CountMatches_ReturnsRecords()
    {
        var csv = this.WriteCsv("FilePath,FileType\na.pdf,pdf\nb.pdf,pdf\n");

        Assert.True(TryBuild(new[] { "--input-csv", csv }, out var records, count: 2));
        Assert.Equal(2, records!.Count);
    }

    [Fact]
    public void TryBuild_BatesColumnWithoutPrefix_ReturnsFalse()
    {
        var csv = this.WriteCsv("FilePath,FileType,BatesNumber\na.pdf,pdf,ABC_00000001\n");

        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--input-csv", csv }, out _));
        });
        Assert.Contains("Error: the source 'BatesNumber' column requires --bates-prefix so the Bates column is emitted.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_BatesColumnWithPrefix_ReturnsRecords()
    {
        var csv = this.WriteCsv("FilePath,FileType,BatesNumber\na.pdf,pdf,ABC_00000001\n");

        Assert.True(TryBuild(new[] { "--input-csv", csv }, out var records, bates: new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 8 }));
        Assert.Single(records!);
        Assert.Equal("ABC_00000001", records![0].BatesNumber);
    }

    [Fact]
    public void TryBuild_BatesColumnWithProductionSet_ReturnsFalse()
    {
        var csv = this.WriteCsv("FilePath,FileType,BatesNumber\na.pdf,pdf,ABC_00000001\n");

        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--input-csv", csv }, out _, productionSet: true, bates: new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 8 }));
        });
        Assert.Contains("Error: the source 'BatesNumber' column cannot be used with --production-set. Production Set Bates Numbers come from the configured Bates sequence so Volume ranges in the Production Manifest stay exact.", error, StringComparison.Ordinal);
    }

    // --- TryBuild identity collision ---

    [Fact]
    public void TryBuild_IdentityCollisionControlNumber_ReturnsFalse()
    {
        var csv = this.WriteCsv("FilePath,FileType,ControlNumber\na.pdf,pdf,DOC00000001\nb.eml,eml,ABC-2\n");

        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--input-csv", csv }, out _));
        });
        Assert.Contains("Error: source ControlNumber 'DOC00000001' collides with the generated Control Number for row 1. Choose an override outside the generated identity space.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_IdentityCollisionControlNumberOutsideGeneratedSpace_ReturnsRecords()
    {
        var csv = this.WriteCsv("FilePath,FileType,ControlNumber\na.pdf,pdf,DOC99999999\nb.eml,eml,ABC-2\n");

        Assert.True(TryBuild(new[] { "--input-csv", csv }, out var records));
        Assert.Equal(2, records!.Count);
    }

    [Fact]
    public void TryBuild_IdentityCollisionBates_ReturnsFalse()
    {
        var csv = this.WriteCsv("FilePath,FileType,BatesNumber\na.pdf,pdf,ABC00000001\nb.eml,eml,\nc.tiff,tiff,\n");
        var bates = new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 8 };

        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--input-csv", csv }, out _, bates: bates));
        });
        Assert.Contains("Error: source BatesNumber 'ABC00000001' collides with the generated Bates sequence value for row 1. Choose an override outside the generated identity space.", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ABC00000009")] // outside the 3-row generated range
    [InlineData("ABC2")] // numerically in range but different string (padding), no byte collision
    [InlineData("XYZ00000001")] // different prefix
    public void TryBuild_IdentityCollisionBatesOutsideGeneratedSpace_ReturnsRecords(string batesValue)
    {
        var csv = this.WriteCsv($"FilePath,FileType,BatesNumber\na.pdf,pdf,{batesValue}\nb.eml,eml,\nc.tiff,tiff,\n");
        var bates = new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 8 };

        Assert.True(TryBuild(new[] { "--input-csv", csv }, out var records, bates: bates));
        Assert.Equal(3, records!.Count);
    }

    // --- TryBuild record construction ---

    [Fact]
    public void TryBuild_ValidCsv_ReturnsRecords()
    {
        var csv = this.WriteCsv("FilePath,FileType\na/x.pdf,pdf\nb.eml,eml\nc.tiff,tiff\n");

        Assert.True(TryBuild(new[] { "--input-csv", csv }, out var records));
        Assert.Equal(3, records!.Count);
        Assert.Equal("a/x.pdf", records[0].RelativePath);
        Assert.Equal("pdf", records[0].FileType);
        Assert.Equal("b.eml", records[1].RelativePath);
        Assert.Equal("tiff", records[2].FileType);
    }

    [Fact]
    public void TryBuild_ValidDirectoryTemplate_ReturnsRecords()
    {
        var template = Path.Combine(this.tempDir, "tpl");
        Directory.CreateDirectory(Path.Combine(template, "sub"));
        File.WriteAllText(Path.Combine(template, "root.pdf"), "x");
        File.WriteAllText(Path.Combine(template, "sub", "inner.eml"), "x");

        Assert.True(TryBuild(new[] { "--directory-template", template }, out var records));
        Assert.Equal(2, records!.Count);
        Assert.Equal("root.pdf", records[0].RelativePath);
        Assert.Equal("sub/inner.eml", records[1].RelativePath);
    }
}
