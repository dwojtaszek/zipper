using Xunit;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class CliModuleSetTests
{
    [Fact]
    public void Parse_RequiredArgs_ParsesCorrectly()
    {
        var modules = CliModules.Create();
        var ok = modules.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Path.Combine(Directory.GetCurrentDirectory(), "test") });
        Assert.True(ok);
        Assert.Equal("pdf", modules.Output.FileType);
        Assert.Equal(100, modules.Output.Count);
    }

    [Fact]
    public void Parse_MissingTypeValue_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type" }));
    }

    [Fact]
    public void Parse_MissingValueForValueTakingFlags_ReturnsFalse()
    {
        var flags = new[]
        {
            "--type", "--count", "--output-path", "--delimiter-column", "--delimiter-quote", "--delimiter-newline",
            "--folders", "--encoding", "--distribution", "--attachment-rate", "--target-zip-size",
            "--load-file-format", "--bates-prefix", "--bates-start", "--bates-digits", "--tiff-pages",
            "--column-profile", "--seed", "--date-format", "--empty-percentage", "--custodian-count",
                "--load-file-formats", "--dat-delimiters", "--loadfile-format", "--eol", "--col-delim",
            "--quote-delim", "--newline-delim", "--multi-delim", "--nested-delim", "--chaos-amount",
            "--chaos-types", "--chaos-scenario", "--volume-size", "--hash-mode", "--hash-algorithms",
            "--compare-production-manifests", "--comparison-mode", "--comparison-output",
            "--types", "--input-csv", "--directory-template", "--source-path-mode"
        };

        foreach (var flag in flags)
        {
            Assert.False(CliModules.Create().Parse(new[] { flag }), $"Expected false when {flag} is missing a value");
        }
    }

    [Fact]
    public void Parse_UnknownFlag_ReturnsFalse()
    {
        var ok = CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--unknown-flag" });
        Assert.False(ok);
    }

    [Fact]
    public void Parse_UnknownPositionalValue_ReturnsFalse()
    {
        var ok = CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "extra_value" });
        Assert.False(ok);
    }

    [Fact]
    public void Parse_UnknownFlagInValuePosition_ReturnsFalse()
    {
        var ok = CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", "--unknown-flag" });
        Assert.False(ok);
    }

    [Fact]
    public void Parse_WithNullArgs_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CliModules.Create().Parse(null!));
    }

    [Fact]
    public void Parse_ChaosListNotConsumedAsValueForPrecedingArg_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "--chaos-list" }));
    }

    [Fact]
    public void Parse_BenchmarkNotConsumedAsValueForPrecedingArg_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "--benchmark" }));
    }

    [Fact]
    public void Parse_InvalidCount_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "not-a-number", "--output-path", Directory.GetCurrentDirectory() }));
    }

    [Fact]
    public void Parse_InvalidFolders_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--folders", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidAttachmentRate_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--attachment-rate", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidBatesStart_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--bates-start", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidBatesDigits_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--bates-digits", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidSeed_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--seed", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidEmptyPercentage_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--empty-percentage", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidCustodianCount_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--custodian-count", "notanumber" }));
    }

    [Fact]
    public void Parse_InvalidVolumeSize_ReturnsFalse()
    {
        Assert.False(CliModules.Create().Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--volume-size", "notanumber" }));
    }

    [Fact]
    public void Parse_AllBooleanFlags_SetCorrectly()
    {
        var modules = CliModules.Create();
        var ok = modules.Parse(new[] { "--type", "pdf", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--with-metadata", "--with-text", "--include-load-file", "--with-families", "--loadfile-only", "--production-set", "--production-zip" });
        Assert.True(ok);
        Assert.True(modules.Metadata.WithMetadata);
        Assert.True(modules.Output.WithText);
        Assert.True(modules.Output.IncludeLoadFile);
        Assert.True(modules.Metadata.WithFamilies);
        Assert.True(modules.LoadFile.LoadfileOnly);
        Assert.True(modules.Production.ProductionSet);
        Assert.True(modules.Production.ProductionZip);
    }

    [Fact]
    public void Parse_LoadfileOnlyArgs_ParsesCorrectly()
    {
        var modules = CliModules.Create();
        var ok = modules.Parse(new[] { "--loadfile-only", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-format", "opt" });
        Assert.True(ok);
        Assert.True(modules.LoadFile.LoadfileOnly);
        Assert.True(modules.LoadFile.IsLoadFileFormatExplicit);
        Assert.Equal(LoadFileFormat.Opt, modules.LoadFile.CurrentFormat);
    }

    [Fact]
    public void Parse_ProductionSetArgs_ParsesCorrectly()
    {
        var modules = CliModules.Create();
        var ok = modules.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--bates-start", "100", "--bates-digits", "6", "--volume-size", "1000", "--count", "20", "--output-path", Directory.GetCurrentDirectory() });
        Assert.True(ok);
        Assert.True(modules.Production.ProductionSet);
        Assert.Equal("CL001", modules.Bates.BatesPrefix);
        Assert.Equal(100, modules.Bates.BatesStart);
        Assert.Equal(6, modules.Bates.BatesDigits);
        Assert.Equal(1000, modules.Production.VolumeSize);
    }

    [Fact]
    public void Parse_ColumnProfileArgs_ParsesCorrectly()
    {
        var modules = CliModules.Create();
        var ok = modules.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", "standard", "--seed", "42", "--date-format", "yyyy-MM-dd", "--empty-percentage", "15", "--custodian-count", "50" });
        Assert.True(ok);
        Assert.Equal("standard", modules.Metadata.ColumnProfile);
        Assert.Equal(42, modules.Metadata.Seed);
        Assert.Equal("yyyy-MM-dd", modules.Metadata.DateFormat);
        Assert.Equal(15, modules.Metadata.EmptyPercentage);
        Assert.Equal(50, modules.Metadata.CustodianCount);
    }
}
