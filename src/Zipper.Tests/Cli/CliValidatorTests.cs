using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class CliValidatorTests
{
    private static ParsedArguments CreateValidArgs()
    {
        return new ParsedArguments
        {
            FileType = "pdf",
            Count = 10,
            OutputPathStr = Directory.GetCurrentDirectory(),
        };
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
        Assert.True(CliValidator.Validate(CreateValidArgs(), CreateModules()));
    }

    [Fact]
    public void Validate_NullArg_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CliValidator.Validate(null!, CreateModules()));
    }

    [Fact]
    public void Validate_MissingType_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.FileType = null;
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithoutType_ReturnsTrue()
    {
        var args = CreateValidArgs();
        args.FileType = null;
        var modules = CreateModules(m => m.LoadFile.TryApply("--loadfile-only", null));
        Assert.True(CliValidator.Validate(args, modules));
    }

    [Fact]
    public void Validate_ProductionSet_WithoutType_ReturnsTrue()
    {
        var args = CreateValidArgs();
        args.FileType = null;
        args.ProductionSet = true;
        var modules = CreateModules(m => m.Bates.TryApply("--bates-prefix", "PREFIX"));
        Assert.True(CliValidator.Validate(args, modules));
    }

    [Fact]
    public void Validate_MissingCount_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.Count = null;
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_CountZero_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.Count = 0;
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_CountNegative_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.Count = -1;
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_CountExceedsMax_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.Count = int.MaxValue;
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_NullOutputPathStr_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.OutputPathStr = null;
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_FoldersOutOfRange_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.Folders = 0;
        Assert.False(CliValidator.Validate(args, CreateModules()));

        args.Folders = 101;
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_AttachmentRateOutOfRange_ReturnsFalse()
    {
        // Attachment-rate bounds moved to MetadataModule.TryBuild in Phase 2.
        var args = CreateValidArgs();
        var modules = CreateModules(m => m.Metadata.TryApply("--attachment-rate", "-1"));
        Assert.False(modules.Metadata.TryBuild(args, out _));

        modules = CreateModules(m => m.Metadata.TryApply("--attachment-rate", "101"));
        Assert.False(modules.Metadata.TryBuild(args, out _));
    }

    [Fact]
    public void Validate_InvalidEncoding_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.Encoding = "INVALID_ENCODING";
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_InvalidDistribution_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.Distribution = "invalid_dist";
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_InvalidTargetZipSize_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.TargetZipSize = "invalid";
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_TargetZipSizeWithoutCount_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.Count = null;
        args.TargetZipSize = "10MB";
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_InvalidLoadFileFormat_ReturnsFalse()
    {
        // Format validation moved to LoadFileModule.TryBuild in Phase 2.
        var args = CreateValidArgs();
        var modules = CreateModules(m => m.LoadFile.TryApply("--load-file-format", "invalid"));
        Assert.False(modules.LoadFile.TryBuild(args, 0, out _));
    }

    [Fact]
    public void Validate_LoadfileOnlyWithTargetZipSize_ReturnsFalse()
    {
        // Loadfile-only conflicts moved to LoadFileModule.TryBuild in Phase 2.
        var args = CreateValidArgs();
        args.TargetZipSize = "100MB";
        var modules = CreateModules(m => m.LoadFile.TryApply("--loadfile-only", null));
        Assert.False(modules.LoadFile.TryBuild(args, 0, out _));
    }

    [Fact]
    public void Validate_LoadfileOnlyWithIncludeLoadFile_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.IncludeLoadFile = true;
        var modules = CreateModules(m => m.LoadFile.TryApply("--loadfile-only", null));
        Assert.False(modules.LoadFile.TryBuild(args, 0, out _));
    }

    [Fact]
    public void Validate_ProductionSet_ConflictsWithLoadfileOnly_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.ProductionSet = true;
        var modules = CreateModules(m => m.LoadFile.TryApply("--loadfile-only", null));
        Assert.False(CliValidator.Validate(args, modules));
    }

    [Fact]
    public void Validate_ProductionSet_WithoutBatesPrefix_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.ProductionSet = true;
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_ProductionZip_WithoutProductionSet_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.ProductionZip = true;
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_VolumeSize_WithoutProductionSet_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.VolumeSize = 100;
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_BatesPrefix_WithPathSeparator_ReturnsFalse()
    {
        // Bates prefix char validation moved to BatesModule.TryBuild (dry-run) in Phase 2.
        var args = CreateValidArgs();
        var modules = CreateModules(m => m.Bates.TryApply("--bates-prefix", "foo/bar"));
        Assert.False(modules.Bates.TryBuild(args, out _));

        modules = CreateModules(m => m.Bates.TryApply("--bates-prefix", "foo\\bar"));
        Assert.False(modules.Bates.TryBuild(args, out _));
    }

    [Fact]
    public void Validate_BatesPrefix_WithDotDot_ReturnsFalse()
    {
        var args = CreateValidArgs();
        var modules = CreateModules(m => m.Bates.TryApply("--bates-prefix", ".."));
        Assert.False(modules.Bates.TryBuild(args, out _));
    }

    [Fact]
    public void Validate_BatesPrefix_WithSpecialChars_ReturnsFalse()
    {
        var args = CreateValidArgs();
        var modules = CreateModules(m => m.Bates.TryApply("--bates-prefix", "hello!@#"));
        Assert.False(modules.Bates.TryBuild(args, out _));
    }

    [Fact]
    public void Validate_WithFamiliesWithoutEml_EmitsWarning()
    {
        // REQ-122: Warn when --with-families is specified without --type eml (moved to MetadataModule.TryBuild in Phase 2).
        var args = CreateValidArgs();
        args.FileType = "pdf";
        var modules = CreateModules(m =>
        {
            m.Metadata.TryApply("--with-families", null);
            m.Metadata.TryApply("--attachment-rate", "50");
        });

        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(modules.Metadata.TryBuild(args, out _));
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
        var args = CreateValidArgs();
        args.FileType = "eml";
        var modules = CreateModules(m =>
        {
            m.Metadata.TryApply("--with-families", null);
            m.Metadata.TryApply("--attachment-rate", "0");
        });

        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(modules.Metadata.TryBuild(args, out _));
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
        var args = CreateValidArgs();
        args.FileType = "eml";
        var modules = CreateModules(m =>
        {
            m.Metadata.TryApply("--with-families", null);
            m.Metadata.TryApply("--attachment-rate", "50");
        });

        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(modules.Metadata.TryBuild(args, out _));
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
        var args = CreateValidArgs();
        var modules = CreateModules(m => m.Metadata.TryApply("--with-families", null));

        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                Assert.True(modules.Metadata.TryBuild(args, out _));
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
        var args = CreateValidArgs();
        var modules = CreateModules(m =>
        {
            m.LoadFile.TryApply("--loadfile-only", null);
            m.LoadFile.TryApply("--load-file-format", "csv");
        });
        Assert.False(modules.LoadFile.TryBuild(args, 0, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithEdrmXmlFormat_ReturnsFalse()
    {
        var args = CreateValidArgs();
        var modules = CreateModules(m =>
        {
            m.LoadFile.TryApply("--loadfile-only", null);
            m.LoadFile.TryApply("--load-file-format", "edrm-xml");
        });
        Assert.False(modules.LoadFile.TryBuild(args, 0, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithCsvFormatsPlural_ReturnsFalse()
    {
        var args = CreateValidArgs();
        var modules = CreateModules(m =>
        {
            m.LoadFile.TryApply("--loadfile-only", null);
            m.LoadFile.TryApply("--load-file-formats", "csv");
        });
        Assert.False(modules.LoadFile.TryBuild(args, 0, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithCsvAndXmlFormatsPlural_ReturnsFalse()
    {
        var args = CreateValidArgs();
        var modules = CreateModules(m =>
        {
            m.LoadFile.TryApply("--loadfile-only", null);
            m.LoadFile.TryApply("--load-file-formats", "csv,xml");
        });
        Assert.False(modules.LoadFile.TryBuild(args, 0, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithDatFormatsPlural_ReturnsTrue()
    {
        var args = CreateValidArgs();
        var modules = CreateModules(m =>
        {
            m.LoadFile.TryApply("--loadfile-only", null);
            m.LoadFile.TryApply("--load-file-formats", "dat,opt");
        });
        Assert.True(modules.LoadFile.TryBuild(args, 0, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithDatFormat_ReturnsTrue()
    {
        var args = CreateValidArgs();
        var modules = CreateModules(m =>
        {
            m.LoadFile.TryApply("--loadfile-only", null);
            m.LoadFile.TryApply("--load-file-format", "dat");
        });
        Assert.True(modules.LoadFile.TryBuild(args, 0, out _));
    }

    [Fact]
    public void Validate_LoadfileOnly_WithOptFormat_ReturnsTrue()
    {
        var args = CreateValidArgs();
        var modules = CreateModules(m =>
        {
            m.LoadFile.TryApply("--loadfile-only", null);
            m.LoadFile.TryApply("--load-file-format", "opt");
        });
        Assert.True(modules.LoadFile.TryBuild(args, 0, out _));
    }

    // --- Supplemental CLI contracts (REQ-173, REQ-174, REQ-175) ---

    [Fact]
    public void Validate_SupplementalProduction_WithoutProductionSet_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.SupplementalProduction = true;
        args.PriorManifests = "/tmp/prior_manifest.json";
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_SupplementalProduction_WithoutPriorManifest_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.ProductionSet = true;
        args.SupplementalProduction = true;
        var modules = CreateModules(m => m.Bates.TryApply("--bates-prefix", "SUPP"));
        Assert.False(CliValidator.Validate(args, modules));
    }

    [Fact]
    public void Validate_PriorManifest_WithoutSupplementalProduction_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.ProductionSet = true;
        args.PriorManifests = "/tmp/prior_manifest.json";
        var modules = CreateModules(m => m.Bates.TryApply("--bates-prefix", "SUPP"));
        Assert.False(CliValidator.Validate(args, modules));
    }

    [Fact]
    public void Validate_SupplementalGapPolicy_WithoutSupplementalProduction_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.ProductionSet = true;
        args.SupplementalGapPolicy = "reject";
        var modules = CreateModules(m => m.Bates.TryApply("--bates-prefix", "SUPP"));
        Assert.False(CliValidator.Validate(args, modules));
    }

    [Theory]
    [InlineData("allow")]
    [InlineData("reject")]
    [InlineData("ALLOW")]
    public void Validate_SupplementalGapPolicy_ValidValue_ReturnsTrue(string policy)
    {
        var args = CreateValidArgs();
        args.ProductionSet = true;
        args.SupplementalProduction = true;
        args.PriorManifests = "/tmp/prior_manifest.json";
        args.SupplementalGapPolicy = policy;
        var modules = CreateModules(m => m.Bates.TryApply("--bates-prefix", "SUPP"));
        Assert.True(CliValidator.Validate(args, modules));
    }

    [Fact]
    public void Validate_SupplementalGapPolicy_InvalidValue_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.ProductionSet = true;
        args.SupplementalProduction = true;
        args.PriorManifests = "/tmp/prior_manifest.json";
        args.SupplementalGapPolicy = "skip";
        var modules = CreateModules(m => m.Bates.TryApply("--bates-prefix", "SUPP"));
        Assert.False(CliValidator.Validate(args, modules));
    }

    // --- Production Manifest Comparison CLI contracts (REQ-176, REQ-177, REQ-178) ---

    [Fact]
    public void Validate_ComparisonMode_WithoutCompareManifests_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.ComparisonMode = "replacement";
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_ComparisonOutput_WithoutCompareManifests_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.ComparisonOutput = "/tmp/report.json";
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_CompareManifests_WithoutComparisonMode_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.CompareProductionManifests = "/tmp/a.json,/tmp/b.json";
        args.ComparisonOutput = "/tmp/report.json";
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_CompareManifests_WithoutComparisonOutput_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.CompareProductionManifests = "/tmp/a.json,/tmp/b.json";
        args.ComparisonMode = "replacement";
        Assert.False(CliValidator.Validate(args, CreateModules()));
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("supplemental")]
    [InlineData("reproduction")]
    [InlineData("REPRODUCTION")]
    public void Validate_CompareManifests_ValidMode_ReturnsTrue(string mode)
    {
        var args = CreateValidArgs();
        args.CompareProductionManifests = "/tmp/a.json,/tmp/b.json";
        args.ComparisonMode = mode;
        args.ComparisonOutput = "/tmp/report.json";
        Assert.True(CliValidator.Validate(args, CreateModules()));
    }

    [Fact]
    public void Validate_CompareManifests_InvalidMode_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.CompareProductionManifests = "/tmp/a.json,/tmp/b.json";
        args.ComparisonMode = "swap";
        args.ComparisonOutput = "/tmp/report.json";
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
    public void Validate_ColumnProfile_WithProductionSet_ReturnsFalse()
    {
        var args = CreateValidArgs();
        args.ProductionSet = true;
        var modules = CreateModules(m => m.Metadata.TryApply("--column-profile", "edrm-standard"));
        Assert.False(CliValidator.Validate(args, modules));
    }
}
