using Xunit;
using Zipper.Cli;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class CliParserTests
{
    [Fact]
    public void Parse_RequiredArgs_ParsesCorrectly()
    {
        var args = new[] { "--type", "pdf", "--count", "100", "--output-path", Path.Combine(Directory.GetCurrentDirectory(), "test") };
        var result = CliParser.Parse(args);
        Assert.NotNull(result);
        Assert.Equal("pdf", result!.FileType);
        Assert.Equal(100, result.Count);
    }

    [Fact]
    public void Parse_MissingTypeValue_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--type" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_MissingValueForValueTakingFlags_ReturnsNull()
    {
        var flags = new[]
        {
            "--type", "--count", "--output-path", "--delimiter-column", "--delimiter-quote", "--delimiter-newline",
            "--folders", "--encoding", "--distribution", "--attachment-rate", "--target-zip-size",
            "--load-file-format", "--bates-prefix", "--bates-start", "--bates-digits", "--tiff-pages",
            "--column-profile", "--seed", "--date-format", "--empty-percentage", "--custodian-count",
                "--load-file-formats", "--dat-delimiters", "--loadfile-format", "--eol", "--col-delim",
            "--quote-delim", "--newline-delim", "--multi-delim", "--nested-delim", "--chaos-amount",
            "--chaos-types", "--chaos-scenario", "--volume-size", "--hash-mode", "--hash-algorithms"
        };

        foreach (var flag in flags)
        {
            var result = CliParser.Parse(new[] { flag });
            Assert.True(result is null, $"Expected null when {flag} is missing a value");
        }
    }

    [Fact]
    public void Parse_UnknownFlag_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--unknown-flag" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_UnknownPositionalValue_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "extra_value" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_UnknownFlagInValuePosition_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", "--unknown-flag" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_AllBooleanFlags_SetCorrectly()
    {
        var (result, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--with-metadata", "--with-text", "--include-load-file", "--with-families", "--loadfile-only", "--production-set", "--production-zip" });
        Assert.NotNull(result);
        Assert.True(modules.Metadata.WithMetadata);
        Assert.True(result!.WithText);
        Assert.True(result.IncludeLoadFile);
        Assert.True(modules.Metadata.WithFamilies);
        Assert.True(modules.LoadFile.LoadfileOnly);
        Assert.True(result.ProductionSet);
        Assert.True(result.ProductionZip);
    }

    [Fact]
    public void Parse_LoadfileOnlyArgs_ParsesCorrectly()
    {
        var (result, modules) = RequestBuilderTestHelper.Parse(new[] { "--loadfile-only", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-format", "opt" });
        Assert.NotNull(result);
        Assert.True(modules.LoadFile.LoadfileOnly);
        Assert.True(modules.LoadFile.IsLoadFileFormatExplicit);
        Assert.Equal(LoadFileFormat.Opt, modules.LoadFile.CurrentFormat);
    }

    [Fact]
    public void Parse_ProductionSetArgs_ParsesCorrectly()
    {
        var (result, modules) = RequestBuilderTestHelper.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--bates-start", "100", "--bates-digits", "6", "--volume-size", "1000", "--count", "20", "--output-path", Directory.GetCurrentDirectory() });
        Assert.NotNull(result);
        Assert.True(result!.ProductionSet);
        Assert.Equal("CL001", modules.Bates.BatesPrefix);
        Assert.Equal(100, modules.Bates.BatesStart);
        Assert.Equal(6, modules.Bates.BatesDigits);
        Assert.Equal(1000, result.VolumeSize);
    }

    [Fact]
    public void Parse_ColumnProfileArgs_ParsesCorrectly()
    {
        var (result, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", "standard", "--seed", "42", "--date-format", "yyyy-MM-dd", "--empty-percentage", "15", "--custodian-count", "50" });
        Assert.NotNull(result);
        Assert.Equal("standard", modules.Metadata.ColumnProfile);
        Assert.Equal(42, modules.Metadata.Seed);
        Assert.Equal("yyyy-MM-dd", modules.Metadata.DateFormat);
        Assert.Equal(15, modules.Metadata.EmptyPercentage);
        Assert.Equal(50, modules.Metadata.CustodianCount);
    }

    [Fact]
    public void Parse_WithNullArgs_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CliParser.Parse(null!));
    }

    [Fact]
    public void Parse_InvalidCount_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "not-a-number", "--output-path", Directory.GetCurrentDirectory() });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidFolders_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--folders", "notanumber" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidAttachmentRate_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--attachment-rate", "notanumber" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidBatesStart_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--bates-start", "notanumber" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidBatesDigits_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--bates-digits", "notanumber" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidSeed_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--seed", "notanumber" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidEmptyPercentage_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--empty-percentage", "notanumber" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidCustodianCount_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--custodian-count", "notanumber" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_InvalidVolumeSize_ReturnsNull()
    {
        var result = CliParser.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--volume-size", "notanumber" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_ChaosListNotConsumedAsValueForPrecedingArg()
    {
        // Before fix: --chaos-list would be consumed as the value for --type.
        // After fix: TryGetValue returns false (--chaos-list is parameterless),
        // so --type fails with a missing-value error and Parse returns null.
        var result = CliParser.Parse(new[] { "--type", "--chaos-list" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_BenchmarkNotConsumedAsValueForPrecedingArg()
    {
        // --benchmark is a parameterless flag; it must not be consumed as a value
        // for a preceding option such as --type.
        var result = CliParser.Parse(new[] { "--type", "--benchmark" });
        Assert.Null(result);
    }

    /// <summary>
    /// REQ-106: A relative path containing ".." that resolves outside CWD must be rejected by
    /// the CLI layer. Before the fix, CliParser called ValidateAndCreateDirectory with no
    /// baseDirectory, bypassing the traversal check entirely.
    /// </summary>
    [Fact]
    public void Parse_OutputPathWithParentTraversal_RejectsPathOutsideCwd()
    {
        // "../escape" resolves to the parent of CWD — outside the allowed base directory.
        var (result, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", "../escape" });
        Assert.NotNull(result);

        // Validation will pass because it's a structural check, but Build will reject it
        var isValid = CliValidator.Validate(result!, modules);
        Assert.True(isValid);
        var buildResult = RequestBuilderTestHelper.Build(result!, modules: modules);
        Assert.Null(buildResult);
    }

    /// <summary>
    /// REQ-106: A path that stays inside CWD must still be accepted after the fix.
    /// Regression guard: adding the base-directory check must not block safe relative paths.
    /// </summary>
    [Fact]
    public void Parse_OutputPathWithinCwd_IsAccepted()
    {
        var uniqueDirName = "output_" + Guid.NewGuid().ToString("N");
        try
        {
            // uniqueDirName is a safe relative subdirectory of CWD and must be accepted.
            var (result, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", uniqueDirName });

            Assert.NotNull(result);

            var isValid = CliValidator.Validate(result!, modules);
            Assert.True(isValid);

            var buildResult = RequestBuilderTestHelper.Build(result!, modules: modules);
            Assert.NotNull(buildResult);
        }
        finally
        {
            if (Directory.Exists(uniqueDirName))
            {
                Directory.Delete(uniqueDirName, recursive: true);
            }
        }
    }

    /// <summary>
    /// REQ-164: A custom profile path containing ".." that resolves outside CWD must be rejected by the CLI layer.
    /// Since Phase 2, CliValidator is a structural check (conflicts only); the path-safety rejection
    /// moved to MetadataModule.TryBuild, so Build must return null.
    /// </summary>
    [Fact]
    public void Parse_ColumnProfileWithParentTraversal_RejectsPathOutsideCwd()
    {
        var (result, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", "../outside-profile.json" });
        Assert.NotNull(result);

        var isValid = CliValidator.Validate(result!, modules);
        Assert.True(isValid);

        var buildResult = RequestBuilderTestHelper.Build(result!, modules: modules);
        Assert.Null(buildResult);
    }

    /// <summary>
    /// REQ-164: A built-in profile name or safe profile path within CWD must be accepted by the CLI layer.
    /// </summary>
    [Fact]
    public void Parse_ColumnProfileWithinCwd_IsAccepted()
    {
        var tempProfilePath = Path.Combine(Directory.GetCurrentDirectory(), "temp_profile_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var validProfileJson = @"{
                ""name"": ""TempProfile"",
                ""columns"": [{ ""name"": ""DocID"", ""type"": ""identifier"" }],
                ""dataSources"": {}
            }";
            File.WriteAllText(tempProfilePath, validProfileJson);

            var (result, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", tempProfilePath });
            Assert.NotNull(result);

            var isValid = CliValidator.Validate(result!, modules);
            Assert.True(isValid);

            var buildResult = RequestBuilderTestHelper.Build(result!, modules: modules);
            Assert.NotNull(buildResult);
        }
        finally
        {
            if (File.Exists(tempProfilePath))
            {
                File.Delete(tempProfilePath);
            }
        }
    }
}
