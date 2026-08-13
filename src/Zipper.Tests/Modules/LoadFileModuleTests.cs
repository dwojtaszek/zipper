using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class LoadFileModuleTests
{
    private static bool TryBuild(ParsedArguments parsed, int attachmentRate, string[] apply, out LoadFileConfig config)
    {
        var module = new LoadFileModule();
        for (int i = 0; i < apply.Length;)
        {
            if (module.TakesValue(apply[i]))
            {
                Assert.True(module.TryApply(apply[i], apply[i + 1]));
                i += 2;
            }
            else
            {
                Assert.True(module.TryApply(apply[i], null));
                i += 1;
            }
        }
        return module.TryBuild(parsed, attachmentRate, out config);
    }

    [Fact]
    public void TryBuild_NoFormatArgs_SingleDatFormat()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, 0, Array.Empty<string>(), out var config));

        Assert.Single(config.Formats);
        Assert.Equal(LoadFileFormat.Dat, config.Formats[0]);
        Assert.Equal(0, config.AttachmentRate);
        Assert.Equal("UTF-8", config.Encoding);
    }

    [Fact]
    public void TryBuild_DefaultDistribution_Proportional()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, 0, Array.Empty<string>(), out var config));
        Assert.Equal(DistributionType.Proportional, config.Distribution);
    }

    [Fact]
    public void TryBuild_AttachmentRate_SetsConfig()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, 42, Array.Empty<string>(), out var config));
        Assert.Equal(42, config.AttachmentRate);
    }

    [Fact]
    public void TryBuild_LoadfileOnly_WithTargetZipSize_ReturnsFalse()
    {
        var parsed = new ParsedArguments { TargetZipSize = "100MB" };
        Assert.False(TryBuild(parsed, 0, new[] { "--loadfile-only" }, out _));
    }

    [Fact]
    public void TryBuild_LoadfileOnly_WithIncludeLoadFile_ReturnsFalse()
    {
        var parsed = new ParsedArguments { IncludeLoadFile = true };
        Assert.False(TryBuild(parsed, 0, new[] { "--loadfile-only" }, out _));
    }

    [Fact]
    public void TryBuild_LoadfileOnly_WithCsvFormat_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, 0, new[] { "--loadfile-only", "--load-file-format", "csv" }, out _));
    }

    [Fact]
    public void TryBuild_LoadfileOnly_WithEdrmXmlFormat_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, 0, new[] { "--loadfile-only", "--load-file-format", "edrm-xml" }, out _));
    }

    [Fact]
    public void TryBuild_LoadfileOnly_WithCsvFormatsPlural_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, 0, new[] { "--loadfile-only", "--load-file-formats", "csv" }, out _));
    }

    [Fact]
    public void TryBuild_LoadfileOnly_WithCsvAndXmlFormatsPlural_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, 0, new[] { "--loadfile-only", "--load-file-formats", "csv,xml" }, out _));
    }

    [Fact]
    public void TryBuild_LoadfileOnly_WithDatFormatsPlural_ReturnsTrue()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, 0, new[] { "--loadfile-only", "--load-file-formats", "dat,opt" }, out var config));
        Assert.Equal(2, config.Formats.Count);
    }

    [Fact]
    public void TryBuild_LoadfileOnly_WithDatFormat_ReturnsTrue()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, 0, new[] { "--loadfile-only", "--load-file-format", "dat" }, out _));
    }

    [Fact]
    public void TryBuild_LoadfileOnly_WithOptFormat_ReturnsTrue()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, 0, new[] { "--loadfile-only", "--load-file-format", "opt" }, out _));
    }

    [Fact]
    public void TryBuild_LoadfileOnly_WithLegacyCsvFormat_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, 0, new[] { "--loadfile-only", "--loadfile-format", "csv" }, out _));
    }

    [Fact]
    public void TryBuild_UnknownFormat_ReturnsInvalidFormatNotDatOptRestriction()
    {
        var parsed = new ParsedArguments();
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.False(TryBuild(parsed, 0, new[] { "--loadfile-only", "--load-file-format", "invalid" }, out _));
                var output = errWriter.ToString();
                Assert.Contains("Error: Invalid load file format. Supported values are dat, opt, csv, edrm-xml, xml, concordance.", output, StringComparison.Ordinal);
                Assert.DoesNotContain("Error: --loadfile-only mode is only supported for 'dat' and 'opt' load file formats.", output, StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public void TryBuild_InvalidLoadFileFormatsPlural_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.False(TryBuild(parsed, 0, new[] { "--load-file-formats", "dat,bogus" }, out _));
                Assert.Contains("Error: Invalid load file format 'bogus'. Supported: dat, opt, csv, edrm-xml, xml, concordance.", errWriter.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public void TryBuild_MultiFormat_CreatesFormatList()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, 0, new[] { "--load-file-formats", "dat,opt,csv" }, out var config));

        Assert.Equal(3, config.Formats.Count);
        Assert.Contains(LoadFileFormat.Dat, config.Formats);
        Assert.Contains(LoadFileFormat.Opt, config.Formats);
        Assert.Contains(LoadFileFormat.Csv, config.Formats);
    }

    [Fact]
    public void TryBuild_LoadfileOnlyEncoding_UsesExtendedSet()
    {
        var parsed = new ParsedArguments { Encoding = "WINDOWS-1252" };
        Assert.True(TryBuild(parsed, 0, new[] { "--loadfile-only" }, out var config));
        Assert.Equal("WINDOWS-1252", config.Encoding);
    }

    [Fact]
    public void TryBuild_Encoding_PreservesNormalizedInputName()
    {
        var parsed = new ParsedArguments { Encoding = "UTF-16" };
        Assert.True(TryBuild(parsed, 0, Array.Empty<string>(), out var config));
        Assert.Equal("UTF-16", config.Encoding);
    }

    [Fact]
    public void CurrentFormat_Default_Dat()
    {
        var module = new LoadFileModule();
        Assert.Equal(LoadFileFormat.Dat, module.CurrentFormat);
        Assert.False(module.LoadfileOnly);
        Assert.False(module.IsLoadFileFormatExplicit);
    }

    [Fact]
    public void CurrentFormat_AfterApply_ReflectsFormat()
    {
        var module = new LoadFileModule();
        Assert.True(module.TryApply("--load-file-format", "opt"));
        Assert.Equal(LoadFileFormat.Opt, module.CurrentFormat);
        Assert.True(module.IsLoadFileFormatExplicit);
    }

    [Fact]
    public void TryApply_LastFlagWins_ForSingleFormatFlag()
    {
        var module = new LoadFileModule();
        Assert.True(module.TryApply("--load-file-format", "csv"));
        Assert.True(module.TryApply("--loadfile-format", "opt"));
        Assert.Equal(LoadFileFormat.Opt, module.CurrentFormat);
    }

    [Fact]
    public void TryApply_LoadfileOnly_ParsesCorrectly()
    {
        var module = new LoadFileModule();
        Assert.True(module.TryApply("--loadfile-only", null));
        Assert.True(module.TryApply("--load-file-format", "opt"));
        Assert.True(module.LoadfileOnly);
        Assert.Equal(LoadFileFormat.Opt, module.CurrentFormat);
    }

    [Fact]
    public void TryApply_MissingValue_ReturnsFalse()
    {
        Assert.False(new LoadFileModule().TryApply("--load-file-format", null));
        Assert.False(new LoadFileModule().TryApply("--load-file-formats", null));
        Assert.False(new LoadFileModule().TryApply("--loadfile-format", null));
    }

    [Fact]
    public void TryApply_UnknownFlag_ReturnsFalse()
    {
        Assert.False(new LoadFileModule().TryApply("--unknown-flag", "x"));
    }

    [Fact]
    public void TryBuild_NullArg_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LoadFileModule().TryBuild(null!, 0, out _));
    }
}
