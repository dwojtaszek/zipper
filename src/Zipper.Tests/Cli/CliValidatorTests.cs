using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class CliValidatorTests
{
    private static (ParsedArguments? Parsed, CliModuleSet Modules) CreateValid()
    {
        return RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory() });
    }

    private static CliModuleSet CreateModules(Action<CliModuleSet>? configure = null)
    {
        var modules = CliModules.Create();
        configure?.Invoke(modules);
        return modules;
    }

    [Fact]
    public void Validate_ValidArgs_ReturnsTrue()
    {
        var (parsed, modules) = CreateValid();
        Assert.True(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_NullArg_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CliValidator.Validate(null!, CreateModules()));
    }

    [Fact]
    public void Validate_MissingType_ReturnsFalse()
    {
        var (parsed, modules) = RequestBuilderTestHelper.Parse(new[] { "--count", "10", "--output-path", Directory.GetCurrentDirectory() });
        Assert.False(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithoutType_ReturnsTrue()
    {
        var (parsed, modules) = RequestBuilderTestHelper.Parse(new[] { "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-only" });
        Assert.True(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_ProductionSet_WithoutType_ReturnsTrue()
    {
        var (parsed, modules) = RequestBuilderTestHelper.Parse(new[] { "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--production-set", "--bates-prefix", "PREFIX" });
        Assert.True(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_MissingCount_ReturnsFalse()
    {
        var (parsed, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--output-path", Directory.GetCurrentDirectory() });
        Assert.False(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_AttachmentRateOutOfRange_ReturnsFalse()
    {
        // Attachment-rate bounds moved to MetadataModule.TryBuild in Phase 2.
        var (_, modules) = CreateValid();
        modules.Metadata.TryApply("--attachment-rate", "-1");
        Assert.False(modules.Metadata.TryBuild(false, false, out _));

        modules.Metadata.TryApply("--attachment-rate", "101");
        Assert.False(modules.Metadata.TryBuild(false, false, out _));
    }

    [Fact]
    public void Validate_TargetZipSizeWithoutCount_ReturnsFalse()
    {
        var (parsed, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--target-zip-size", "10MB", "--output-path", Directory.GetCurrentDirectory() });
        Assert.False(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_InvalidLoadFileFormat_ReturnsFalse()
    {
        // Format validation moved to LoadFileModule.TryBuild in Phase 2.
        var (_, modules) = CreateValid();
        modules.LoadFile.TryApply("--load-file-format", "invalid");
        Assert.False(modules.LoadFile.TryBuild(0, null, false, null, null, false, out _));
    }

    [Fact]
    public void Validate_LoadfileOnlyWithTargetZipSize_ReturnsFalse()
    {
        // Loadfile-only conflicts moved to LoadFileModule.TryBuild in Phase 2.
        var (_, modules) = CreateValid();
        modules.LoadFile.TryApply("--loadfile-only", null);
        Assert.False(modules.LoadFile.TryBuild(0, null, false, null, "100MB", false, out _));
    }

    [Fact]
    public void Validate_LoadfileOnlyWithIncludeLoadFile_ReturnsFalse()
    {
        var (_, modules) = CreateValid();
        modules.LoadFile.TryApply("--loadfile-only", null);
        Assert.False(modules.LoadFile.TryBuild(0, null, false, null, null, true, out _));
    }

    [Fact]
    public void Validate_ProductionSet_ConflictsWithLoadfileOnly_ReturnsFalse()
    {
        var (parsed, modules) = CreateValid();
        modules.Production.TryApply("--production-set", null);
        modules.LoadFile.TryApply("--loadfile-only", null);
        Assert.False(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_RedactedProduction_ConflictsWithLoadfileOnly_ReturnsFalse()
    {
        var (parsed, modules) = CreateValid();
        modules.Production.TryApply("--production-set", null);
        modules.Production.TryApply("--redacted-production", null);
        modules.Bates.TryApply("--bates-prefix", "PREFIX");
        modules.LoadFile.TryApply("--loadfile-only", null);
        Assert.False(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_ProductionSet_WithoutBatesPrefix_ReturnsFalse()
    {
        var (parsed, modules) = CreateValid();
        modules.Production.TryApply("--production-set", null);
        Assert.False(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_BatesPrefix_WithPathSeparator_ReturnsFalse()
    {
        // Bates prefix char validation moved to BatesModule.TryBuild (dry-run) in Phase 2.
        var (_, modules) = CreateValid();
        modules.Bates.TryApply("--bates-prefix", "foo/bar");
        Assert.False(modules.Bates.TryBuild(false, 1, "continuous", null, out _));

        modules.Bates.TryApply("--bates-prefix", "foo\\bar");
        Assert.False(modules.Bates.TryBuild(false, 1, "continuous", null, out _));
    }

    [Fact]
    public void Validate_BatesPrefix_WithDotDot_ReturnsFalse()
    {
        var (_, modules) = CreateValid();
        modules.Bates.TryApply("--bates-prefix", "..");
        Assert.False(modules.Bates.TryBuild(false, 1, "continuous", null, out _));
    }

    [Fact]
    public void Validate_BatesPrefix_WithSpecialChars_ReturnsFalse()
    {
        var (_, modules) = CreateValid();
        modules.Bates.TryApply("--bates-prefix", "hello!@#");
        Assert.False(modules.Bates.TryBuild(false, 1, "continuous", null, out _));
    }

    [Fact]
    public void Validate_WithFamiliesWithoutEml_EmitsWarning()
    {
        // REQ-122: Warn when --with-families is specified without --type eml (moved to MetadataModule.TryBuild in Phase 2).
        var (_, modules) = CreateValid();
        modules.Metadata.TryApply("--with-families", null);
        modules.Metadata.TryApply("--attachment-rate", "50");

        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(modules.Metadata.TryBuild(false, false, out _));
                var output = errWriter.ToString();
                Assert.Contains("Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified.", output, StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public void Validate_WithFamiliesWithEmlAndAttachmentRateZero_EmitsWarning()
    {
        // REQ-122: Warn when --with-families is specified with --attachment-rate 0
        var (_, modules) = CreateValid();
        modules.Metadata.TryApply("--with-families", null);
        modules.Metadata.TryApply("--attachment-rate", "0");

        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(modules.Metadata.TryBuild(true, false, out _));
                var output = errWriter.ToString();
                Assert.Contains("Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified.", output, StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public void Validate_WithFamiliesWithEmlAndAttachmentRatePositive_DoesNotEmitWarning()
    {
        // REQ-122: Do not warn when --with-families is used correctly with --type eml and positive attachment rate
        var (_, modules) = CreateValid();
        modules.Metadata.TryApply("--with-families", null);
        modules.Metadata.TryApply("--attachment-rate", "50");

        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(modules.Metadata.TryBuild(true, false, out _));
                var output = errWriter.ToString();
                Assert.DoesNotContain("Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified.", output, StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    [Fact]
    public void Validate_WithFamiliesAndLoadfileOnly_EmitsWarning()
    {
        // Warn when --with-families is specified with --loadfile-only mode but not type eml/attachment rate > 0
        var (_, modules) = CreateValid();
        modules.Metadata.TryApply("--with-families", null);

        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(modules.Metadata.TryBuild(false, false, out _));
                var output = errWriter.ToString();
                Assert.Contains("Warning: --with-families is only meaningful when --type eml (or eml participates in --types) and --attachment-rate > 0 are specified.", output, StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    /// <summary>
    /// Validates that using a CSV format with the --loadfile-only flag returns false,
    /// satisfying the requirement to reject non-DAT/OPT formats (Covers issue #343).
    /// Moved to LoadFileModule.TryBuild in Phase 2.
    /// </summary>
    [Fact]
    public void Validate_LoadfileOnly_WithCsvFormat_ReturnsFalse()
    {
        var (_, modules) = CreateValid();
        modules.LoadFile.TryApply("--loadfile-only", null);
        modules.LoadFile.TryApply("--load-file-format", "csv");
        Assert.False(modules.LoadFile.TryBuild(0, null, false, null, null, false, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithEdrmXmlFormat_ReturnsFalse()
    {
        var (_, modules) = CreateValid();
        modules.LoadFile.TryApply("--loadfile-only", null);
        modules.LoadFile.TryApply("--load-file-format", "edrm-xml");
        Assert.False(modules.LoadFile.TryBuild(0, null, false, null, null, false, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithCsvFormatsPlural_ReturnsFalse()
    {
        var (_, modules) = CreateValid();
        modules.LoadFile.TryApply("--loadfile-only", null);
        modules.LoadFile.TryApply("--load-file-formats", "csv");
        Assert.False(modules.LoadFile.TryBuild(0, null, false, null, null, false, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithCsvAndXmlFormatsPlural_ReturnsFalse()
    {
        var (_, modules) = CreateValid();
        modules.LoadFile.TryApply("--loadfile-only", null);
        modules.LoadFile.TryApply("--load-file-formats", "csv,xml");
        Assert.False(modules.LoadFile.TryBuild(0, null, false, null, null, false, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithDatFormatsPlural_ReturnsTrue()
    {
        var (_, modules) = CreateValid();
        modules.LoadFile.TryApply("--loadfile-only", null);
        modules.LoadFile.TryApply("--load-file-formats", "dat,opt");
        Assert.True(modules.LoadFile.TryBuild(0, null, false, null, null, false, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithDatFormat_ReturnsTrue()
    {
        var (_, modules) = CreateValid();
        modules.LoadFile.TryApply("--loadfile-only", null);
        modules.LoadFile.TryApply("--load-file-format", "dat");
        Assert.True(modules.LoadFile.TryBuild(0, null, false, null, null, false, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithOptFormat_ReturnsTrue()
    {
        var (_, modules) = CreateValid();
        modules.LoadFile.TryApply("--loadfile-only", null);
        modules.LoadFile.TryApply("--load-file-format", "opt");
        Assert.True(modules.LoadFile.TryBuild(0, null, false, null, null, false, out _));
    }

    // --- Production Manifest Comparison CLI contracts (REQ-176, REQ-177, REQ-178) ---

    [Fact]
    public void Validate_ComparisonMode_WithoutCompareManifests_ReturnsFalse()
    {
        var args = new ParsedArguments { ComparisonMode = "replacement" };
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_ComparisonOutput_WithoutCompareManifests_ReturnsFalse()
    {
        var args = new ParsedArguments { ComparisonOutput = "/tmp/report.json" };
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_CompareManifests_WithoutComparisonMode_ReturnsFalse()
    {
        var args = new ParsedArguments
        {
            CompareProductionManifests = "/tmp/a.json,/tmp/b.json",
            ComparisonOutput = "/tmp/report.json",
        };
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_CompareManifests_WithoutComparisonOutput_ReturnsFalse()
    {
        var args = new ParsedArguments
        {
            CompareProductionManifests = "/tmp/a.json,/tmp/b.json",
            ComparisonMode = "replacement",
        };
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("supplemental")]
    [InlineData("reproduction")]
    [InlineData("REPRODUCTION")]
    public void Validate_CompareManifests_ValidMode_ReturnsTrue(string mode)
    {
        var args = new ParsedArguments
        {
            CompareProductionManifests = "/tmp/a.json,/tmp/b.json",
            ComparisonMode = mode,
            ComparisonOutput = "/tmp/report.json",
        };
        Assert.True(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_CompareManifests_InvalidMode_ReturnsFalse()
    {
        var args = new ParsedArguments
        {
            CompareProductionManifests = "/tmp/a.json,/tmp/b.json",
            ComparisonMode = "swap",
            ComparisonOutput = "/tmp/report.json",
        };
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_CompareManifests_BypassesTypeCountOutputPathValidation()
    {
        // REQ-179: comparison workflow short-circuits --type/--count/--output-path validation.
        var args = new ParsedArguments
        {
            CompareProductionManifests = "/tmp/a.json,/tmp/b.json",
            ComparisonMode = "replacement",
            ComparisonOutput = "/tmp/report.json",
        };
        Assert.True(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_Types_WithLoadfileOnly_ReturnsFalse()
    {
        var (parsed, modules) = CreateValid();
        modules.Output.TryApply("--types", "pdf:70,xls:30");
        modules.LoadFile.TryApply("--loadfile-only", null);
        Assert.False(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_Types_WithColumnProfile_ReturnsFalse()
    {
        var (parsed, modules) = CreateValid();
        modules.Output.TryApply("--types", "pdf:70,xls:30");
        modules.Metadata.TryApply("--column-profile", "standard");
        Assert.False(CliValidator.Validate(parsed!, modules));
    }

    [Fact]
    public void Validate_ColumnProfile_WithProductionSet_ReturnsFalse()
    {
        var (parsed, modules) = CreateValid();
        modules.Production.TryApply("--production-set", null);
        modules.Metadata.TryApply("--column-profile", "edrm-standard");
        Assert.False(CliValidator.Validate(parsed!, modules));
    }
}
