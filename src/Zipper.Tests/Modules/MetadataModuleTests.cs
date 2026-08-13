using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class MetadataModuleTests
{
    private static bool TryBuild(ParsedArguments parsed, string[] apply, out MetadataConfig config)
    {
        var module = new MetadataModule();
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
        return module.TryBuild(parsed, out config);
    }

    private static ParsedArguments CreateParsedArgs()
    {
        return new ParsedArguments
        {
            FileType = "pdf",
            Count = 10,
            OutputPathStr = Directory.GetCurrentDirectory(),
        };
    }

    [Fact]
    public void TryBuild_NoMetadataArgs_SetsDefaults()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, Array.Empty<string>(), out var config));

        Assert.False(config.WithMetadata);
        Assert.Null(config.ColumnProfile);
        Assert.False(config.WithFamilies);
        Assert.False(config.WithCollectionMetadata);
        Assert.Null(config.Seed);
    }

    [Fact]
    public void TryBuild_WithMetadataFlag_SetsConfig()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, new[] { "--with-metadata" }, out var config));
        Assert.True(config.WithMetadata);
    }

    [Fact]
    public void TryBuild_ProfileSeedDateFormat_SetsConfig()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, new[] { "--column-profile", "standard", "--seed", "42", "--date-format", "yyyy-MM-dd", "--empty-percentage", "15", "--custodian-count", "50" }, out var config));

        Assert.NotNull(config.ColumnProfile);
        Assert.Equal(42, config.Seed);
        Assert.Equal("yyyy-MM-dd", config.DateFormatOverride);
        Assert.Equal(15, config.EmptyPercentageOverride);
        Assert.Equal(50, config.CustodianCountOverride);
    }

    [Fact]
    public void TryApply_ProfileArgs_ParseCorrectly()
    {
        var module = new MetadataModule();
        Assert.True(module.TryApply("--column-profile", "standard"));
        Assert.True(module.TryApply("--seed", "42"));
        Assert.True(module.TryApply("--date-format", "yyyy-MM-dd"));
        Assert.True(module.TryApply("--empty-percentage", "15"));
        Assert.True(module.TryApply("--custodian-count", "50"));
        Assert.True(module.TryApply("--attachment-rate", "30"));

        Assert.True(module.HasColumnProfile);
        Assert.Equal("standard", module.ColumnProfile);
        Assert.Equal(42, module.Seed);
        Assert.Equal("yyyy-MM-dd", module.DateFormat);
        Assert.Equal(15, module.EmptyPercentage);
        Assert.Equal(50, module.CustodianCount);
        Assert.Equal(30, module.AttachmentRate);
    }

    [Fact]
    public void TryBuild_AttachmentRateOutOfRange_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, new[] { "--attachment-rate", "-1" }, out _));
        Assert.False(TryBuild(parsed, new[] { "--attachment-rate", "101" }, out _));
    }

    [Fact]
    public void TryBuild_EmptyPercentageOutOfRange_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, new[] { "--empty-percentage", "-1" }, out _));
        Assert.False(TryBuild(parsed, new[] { "--empty-percentage", "101" }, out _));
    }

    [Fact]
    public void TryBuild_CustodianCountOutOfRange_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, new[] { "--custodian-count", "0" }, out _));
        Assert.False(TryBuild(parsed, new[] { "--custodian-count", "1001" }, out _));
    }

    [Fact]
    public void TryBuild_WithFamiliesWithoutEml_EmitsWarning()
    {
        var parsed = CreateParsedArgs();
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(TryBuild(parsed, new[] { "--with-families", "--attachment-rate", "50" }, out _));
                Assert.Contains("Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified.", errWriter.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public void TryBuild_WithFamiliesWithEmlAndAttachmentRateZero_EmitsWarning()
    {
        var parsed = CreateParsedArgs();
        parsed.FileType = "eml";
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(TryBuild(parsed, new[] { "--with-families", "--attachment-rate", "0" }, out _));
                Assert.Contains("Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified.", errWriter.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public void TryBuild_WithFamiliesWithEmlAndAttachmentRatePositive_DoesNotEmitWarning()
    {
        var parsed = CreateParsedArgs();
        parsed.FileType = "eml";
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(TryBuild(parsed, new[] { "--with-families", "--attachment-rate", "50" }, out _));
                Assert.DoesNotContain("Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified.", errWriter.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public void TryBuild_ProfileWithParentTraversal_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, new[] { "--column-profile", "../outside-profile.json" }, out _));
    }

    [Fact]
    public void TryBuild_ProfileNonExistentFile_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        var missingPath = Path.Combine(Directory.GetCurrentDirectory(), "missing_profile_" + Guid.NewGuid().ToString("N") + ".json");
        Assert.False(TryBuild(parsed, new[] { "--column-profile", missingPath }, out _));
    }

    [Fact]
    public void TryBuild_ProfileInvalidJson_ReturnsFalse()
    {
        var tempProfilePath = Path.Combine(Directory.GetCurrentDirectory(), "bad_profile_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(tempProfilePath, "{ not valid json");
            var parsed = new ParsedArguments();
            var originalError = Console.Error;
            using (var errWriter = new StringWriter())
            {
                Console.SetError(errWriter);
                try
                {
                    Assert.False(TryBuild(parsed, new[] { "--column-profile", tempProfilePath }, out _));
                    Assert.Contains("Error: Invalid JSON in column profile", errWriter.ToString(), StringComparison.Ordinal);
                }
                finally
                {
                    Console.SetError(originalError);
                }
            }
        }
        finally
        {
            if (File.Exists(tempProfilePath))
            {
                File.Delete(tempProfilePath);
            }
        }
    }

    [Fact]
    public void TryBuild_ProfileValidFile_LoadsProfile()
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

            var parsed = new ParsedArguments();
            Assert.True(TryBuild(parsed, new[] { "--column-profile", tempProfilePath }, out var config));
            Assert.NotNull(config.ColumnProfile);
        }
        finally
        {
            if (File.Exists(tempProfilePath))
            {
                File.Delete(tempProfilePath);
            }
        }
    }

    [Fact]
    public void TryBuild_ProfileBuiltIn_LoadsProfile()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, new[] { "--column-profile", "standard" }, out var config));
        Assert.NotNull(config.ColumnProfile);
    }

    [Fact]
    public void TryBuild_ProfileWithMetadata_PrecedenceWarning()
    {
        var parsed = new ParsedArguments();
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(TryBuild(parsed, new[] { "--with-metadata", "--column-profile", "standard" }, out var config));
                Assert.Contains("Warning: --column-profile takes precedence over --with-metadata. --with-metadata will be ignored.", errWriter.ToString(), StringComparison.Ordinal);
                Assert.False(config.WithMetadata);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public void TryApply_InvalidSeed_ReturnsFalse()
    {
        Assert.False(new MetadataModule().TryApply("--seed", "notanumber"));
    }

    [Fact]
    public void TryApply_InvalidEmptyPercentage_ReturnsFalse()
    {
        Assert.False(new MetadataModule().TryApply("--empty-percentage", "notanumber"));
    }

    [Fact]
    public void TryApply_InvalidCustodianCount_ReturnsFalse()
    {
        Assert.False(new MetadataModule().TryApply("--custodian-count", "notanumber"));
    }

    [Fact]
    public void TryApply_InvalidAttachmentRate_ReturnsFalse()
    {
        Assert.False(new MetadataModule().TryApply("--attachment-rate", "notanumber"));
    }

    [Fact]
    public void TryApply_MissingValue_ReturnsFalse()
    {
        Assert.False(new MetadataModule().TryApply("--seed", null));
    }

    [Fact]
    public void TryApply_UnknownFlag_ReturnsFalse()
    {
        Assert.False(new MetadataModule().TryApply("--unknown-flag", "x"));
    }

    [Fact]
    public void TryBuild_NullArg_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new MetadataModule().TryBuild(null!, out _));
    }
}
