using Xunit;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class MixedFileTypeCliTests : IDisposable
{
    private readonly string tempDir;

    public MixedFileTypeCliTests()
    {
        this.tempDir = Path.Combine(Directory.GetCurrentDirectory(), $"zipper_mixed_cli_{Guid.NewGuid():N}");
        Directory.CreateDirectory(this.tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(this.tempDir))
        {
            Directory.Delete(this.tempDir, true);
        }
    }

    [Fact]
    public void Parse_TypesArgument_StoresRawValue()
    {
        var (ok, modules) = PipelineTestHelper.Parse(new[] { "--types", "pdf:50,eml:50", "--count", "10", "--output-path", this.tempDir });

        Assert.True(ok);
        Assert.Equal("pdf:50,eml:50", modules.Output.FileTypes);
    }

    [Fact]
    public void Build_ValidMix_CreatesFileTypePlan()
    {
        var args = new[] { "--types", "pdf:50,eml:30,tiff:20", "--count", "10", "--output-path", this.tempDir };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.True(request!.Output.IsMixedFileTypes);
        Assert.NotNull(request.Output.FileTypePlan);
        Assert.Equal("pdf", request.Output.FileType);
        Assert.Equal(5, request.Output.FileTypePlan!.GetTypeCount("pdf"));
        Assert.Equal(3, request.Output.FileTypePlan.GetTypeCount("eml"));
        Assert.Equal(2, request.Output.FileTypePlan.GetTypeCount("tiff"));
    }

    [Fact]
    public void Build_SingleEntryMix_BehavesAsSingleType()
    {
        var args = new[] { "--types", "eml:1", "--count", "10", "--output-path", this.tempDir };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.Equal("eml", request!.Output.FileType);
        Assert.False(request.Output.IsMixedFileTypes);
        Assert.Null(request.Output.FileTypePlan);
    }

    [Fact]
    public void Build_TypesAndTypeTogether_ReturnsNull()
    {
        var error = CaptureError(() =>
        {
            var args = new[] { "--type", "pdf", "--types", "eml:1", "--count", "10", "--output-path", this.tempDir };
            var request = Cli.Pipeline.Build(args);
            Assert.Null(request);
        });

        Assert.Contains("--type", error, StringComparison.Ordinal);
        Assert.Contains("--types", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TypesWithLoadfileOnly_ReturnsNull()
    {
        var error = CaptureError(() =>
        {
            var args = new[] { "--types", "pdf:1,eml:1", "--count", "10", "--output-path", this.tempDir, "--loadfile-only" };
            var request = Cli.Pipeline.Build(args);
            Assert.Null(request);
        });

        Assert.Contains("--types", error, StringComparison.Ordinal);
        Assert.Contains("--loadfile-only", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TypesWithoutType_SatisfiesTypeRequirement()
    {
        var args = new[] { "--types", "pdf:1,eml:1", "--count", "10", "--output-path", this.tempDir };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
    }

    [Fact]
    public void Build_TypesWithUnknownType_ReturnsNull()
    {
        var error = CaptureError(() =>
        {
            var args = new[] { "--types", "pdf:1,bogus:1", "--count", "10", "--output-path", this.tempDir };
            var request = Cli.Pipeline.Build(args);
            Assert.Null(request);
        });

        Assert.Contains("bogus", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_MixWithTiffOrJpg_DefaultsToDatAndOpt()
    {
        var args = new[] { "--types", "tiff:1,pdf:1", "--count", "10", "--output-path", this.tempDir };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.Equal(new[] { LoadFileFormat.Dat, LoadFileFormat.Opt }, request!.LoadFile.Formats);
    }

    [Fact]
    public void Build_MixWithoutTiffOrJpg_DefaultsToDatOnly()
    {
        var args = new[] { "--types", "pdf:1,eml:1", "--count", "10", "--output-path", this.tempDir };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.Equal(new[] { LoadFileFormat.Dat }, request!.LoadFile.Formats);
    }

    [Fact]
    public void Build_ProductionSetWithMix_ParsesCorrectly()
    {
        var args = new[]
        {
            "--production-set", "--types", "pdf:1,eml:1", "--count", "10", "--output-path", this.tempDir,
            "--bates-prefix", "TEST",
        };

        var request = Cli.Pipeline.Build(args);

        Assert.NotNull(request);
        Assert.True(request!.Output.IsMixedFileTypes);
    }

    [Fact]
    public void Build_WithFamiliesAndEmlInMix_NoWarning()
    {
        var error = CaptureError(() =>
        {
            var args = new[] { "--types", "pdf:1,eml:1", "--count", "10", "--output-path", this.tempDir, "--with-families", "--attachment-rate", "50" };
            var request = Cli.Pipeline.Build(args);
            Assert.NotNull(request);
        });

        Assert.DoesNotContain("--with-families", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithFamiliesAndNoEmlInMix_Warns()
    {
        var error = CaptureError(() =>
        {
            var args = new[] { "--types", "pdf:1,tiff:1", "--count", "10", "--output-path", this.tempDir, "--with-families", "--attachment-rate", "50" };
            var request = Cli.Pipeline.Build(args);
            Assert.NotNull(request);
        });

        Assert.Contains("Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TypesWithColumnProfile_ReturnsNull()
    {
        var error = CaptureError(() =>
        {
            var args = new[] { "--types", "pdf:1,eml:1", "--count", "10", "--output-path", this.tempDir, "--column-profile", "standard" };
            var request = Cli.Pipeline.Build(args);
            Assert.Null(request);
        });

        Assert.Contains("--column-profile", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_TypesWithEmptyValue_ReturnsNull()
    {
        var error = CaptureError(() =>
        {
            var args = new[] { "--types", string.Empty, "--count", "10", "--output-path", this.tempDir };
            var request = Cli.Pipeline.Build(args);
            Assert.Null(request);
        });

        Assert.Contains("--types requires a value", error, StringComparison.Ordinal);
    }

    private static string CaptureError(Action action)
    {
        var originalError = Console.Error;
        var errorOutput = new StringWriter();
        Console.SetError(errorOutput);
        try
        {
            action();
            return errorOutput.ToString();
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}
