using Xunit;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ProductionModuleTests
{
    private static bool TryBuild(string?[] apply, out ProductionConfig config)
    {
        var module = new ProductionModule();
        for (int i = 0; i < apply.Length; i += 2)
        {
            Assert.True(module.TryApply(apply[i]!, apply[i + 1]));
        }
        return module.TryBuild(out config);
    }

    private static string CaptureError(Action action)
    {
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                action();
                return errWriter.ToString();
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
    }

    // --- TryApply raw storage ---

    [Fact]
    public void TryApply_VolumeSize_StoresValue()
    {
        var module = new ProductionModule();
        Assert.True(module.TryApply("--volume-size", "1000"));
        Assert.Equal(1000, module.VolumeSize);
    }

    [Fact]
    public void TryApply_RollingCount_StoresValue()
    {
        var module = new ProductionModule();
        Assert.True(module.TryApply("--rolling-count", "3"));
        Assert.Equal(3, module.RollingCount);
    }

    [Fact]
    public void TryApply_InvalidVolumeSize_ReturnsFalse()
    {
        var error = CaptureError(() => Assert.False(new ProductionModule().TryApply("--volume-size", "notanumber")));
        Assert.Contains("Error: Invalid value for --volume-size: 'notanumber'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApply_InvalidRollingCount_ReturnsFalse()
    {
        var error = CaptureError(() => Assert.False(new ProductionModule().TryApply("--rolling-count", "notanumber")));
        Assert.Contains("Error: Invalid value for --rolling-count: 'notanumber'", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApply_MissingVolumeSizeValue_ReturnsFalse()
    {
        var error = CaptureError(() => Assert.False(new ProductionModule().TryApply("--volume-size", null)));
        Assert.Contains("Error: --volume-size requires a value.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApply_MissingRollingCountValue_ReturnsFalse()
    {
        var error = CaptureError(() => Assert.False(new ProductionModule().TryApply("--rolling-count", null)));
        Assert.Contains("Error: --rolling-count requires a value.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApply_ProductionSetFlag_StoresTrue()
    {
        var module = new ProductionModule();
        Assert.True(module.TryApply("--production-set", null));
        Assert.True(module.ProductionSet);
    }

    [Fact]
    public void TryApply_RedactedProductionFlag_StoresTrue()
    {
        var module = new ProductionModule();
        Assert.True(module.TryApply("--redacted-production", null));
        Assert.True(module.RedactedProduction);
    }

    [Fact]
    public void TryApply_UnknownFlag_ReturnsFalse()
    {
        Assert.False(new ProductionModule().TryApply("--unknown-flag", "x"));
    }

    // --- Pure-domain dependency checks ---

    [Fact]
    public void TryBuild_ProductionZipWithoutSet_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-zip", null }, out _));
        });
        Assert.Contains("Error: --production-zip requires --production-set.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_VolumeSizeWithoutSet_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--volume-size", "100" }, out _));
        });
        Assert.Contains("Error: --volume-size requires --production-set.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_VolumeSizeZeroWithSet_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--volume-size", "0" }, out _));
        });
        Assert.Contains("Error: --volume-size must be at least 1.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_SupplementalWithoutSet_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--supplemental-production", null }, out _));
        });
        Assert.Contains("Error: --supplemental-production requires --production-set.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_SupplementalWithoutManifest_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--supplemental-production", null }, out _));
        });
        Assert.Contains("Error: --supplemental-production requires --prior-manifest.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_PriorManifestWithoutSupplemental_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--prior-manifest", "/tmp/prior_manifest.json" }, out _));
        });
        Assert.Contains("Error: --prior-manifest requires --supplemental-production.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_GapPolicyWithoutSupplemental_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--supplemental-gap-policy", "reject" }, out _));
        });
        Assert.Contains("Error: --supplemental-gap-policy requires --supplemental-production.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_GapPolicyInvalid_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--supplemental-production", null, "--prior-manifest", "/tmp/prior_manifest.json", "--supplemental-gap-policy", "skip" }, out _));
        });
        Assert.Contains("Error: --supplemental-gap-policy must be 'reject' or 'allow'.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_RedactedWithoutSet_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--redacted-production", null }, out _));
        });
        Assert.Contains("Error: --redacted-production requires --production-set.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_WithheldWithoutRedacted_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--withheld-native-policy", "keep-native" }, out _));
        });
        Assert.Contains("Error: --withheld-native-policy requires --redacted-production.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_WithheldInvalid_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--redacted-production", null, "--withheld-native-policy", "invalid" }, out _));
        });
        Assert.Contains("Error: --withheld-native-policy must be 'keep-native', 'omit-native-path', or 'replace-with-placeholder'.", error, StringComparison.Ordinal);
    }

    // --- Rolling config checks ---

    [Fact]
    public void TryBuild_RollingCountZero_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--rolling-count", "0" }, out _));
        });
        Assert.Contains("Error: --rolling-count must be a positive number.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_RollingBatesModeInvalid_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--rolling-bates-mode", "invalid" }, out _));
        });
        Assert.Contains("Error: --rolling-bates-mode must be 'continuous' or 'restart'.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_SourcePathModeInvalid_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--source-path-mode", "invalid" }, out _));
        });
        Assert.Contains("Error: --source-path-mode must be 'bates', 'preserve', or 'originals'.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_ProductionIdCountMismatch_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--rolling-count", "2", "--production-id", "A,B,C" }, out _));
        });
        Assert.Contains("Error: Number of production IDs must match rolling count.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_ProductionIdDuplicate_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--rolling-count", "2", "--production-id", "PROD001,PROD001" }, out _));
        });
        Assert.Contains("Error: Duplicate production IDs are not allowed.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_ProductionIdEmpty_ReturnsFalse()
    {
        var error = CaptureError(() =>
        {
            Assert.False(TryBuild(new[] { "--production-set", null, "--rolling-count", "2", "--production-id", "PROD001," }, out _));
        });
        Assert.Contains("Error: Production ID cannot be empty.", error, StringComparison.Ordinal);
    }

    // --- Config assembly ---

    [Fact]
    public void TryBuild_NoFlags_BuildsDefaults()
    {
        Assert.True(TryBuild(Array.Empty<string>(), out var config));

        Assert.False(config.ProductionSet);
        Assert.False(config.ProductionZip);
        Assert.Equal(5000, config.VolumeSize);
        Assert.False(config.SupplementalProduction);
        Assert.Empty(config.PriorManifests);
        Assert.Equal("reject", config.SupplementalGapPolicy);
        Assert.Null(config.ProductionId);
        Assert.Equal(1, config.RollingCount);
        Assert.Equal(RollingBatesMode.Continuous, config.RollingBatesMode);
        Assert.False(config.RedactedProduction);
        Assert.Equal("keep-native", config.WithheldNativePolicy);
        Assert.Equal(SourcePathMode.Bates, config.SourcePathMode);
    }

    [Fact]
    public void TryBuild_ValidFlags_MatchesRequestBuilderAssembly()
    {
        var apply = new[]
        {
            "--production-set", null,
            "--production-zip", null,
            "--volume-size", "1000",
            "--supplemental-production", null,
            "--prior-manifest", "/tmp/a.json, /tmp/b.json",
            "--supplemental-gap-policy", "allow",
            "--production-id", "PROD001",
            "--rolling-count", "2",
            "--rolling-bates-mode", "restart",
            "--redacted-production", null,
            "--withheld-native-policy", "keep-native",
            "--source-path-mode", "preserve",
        };
        Assert.True(TryBuild(apply, out var config));

        Assert.True(config.ProductionSet);
        Assert.True(config.ProductionZip);
        Assert.Equal(1000, config.VolumeSize);
        Assert.True(config.SupplementalProduction);
        Assert.Equal(new[] { "/tmp/a.json", "/tmp/b.json" }, config.PriorManifests);
        Assert.Equal("allow", config.SupplementalGapPolicy);
        Assert.Equal("PROD001", config.ProductionId);
        Assert.Equal(2, config.RollingCount);
        Assert.Equal(RollingBatesMode.Restart, config.RollingBatesMode);
        Assert.True(config.RedactedProduction);
        Assert.Equal("keep-native", config.WithheldNativePolicy);
        Assert.Equal(SourcePathMode.PreserveSubdirs, config.SourcePathMode);
    }

    [Fact]
    public void TryBuild_SourcePathModeOriginals_MapsToEnum()
    {
        Assert.True(TryBuild(new[] { "--production-set", null, "--source-path-mode", "originals" }, out var config));
        Assert.Equal(SourcePathMode.Originals, config.SourcePathMode);
    }

    [Fact]
    public void TryBuild_WithheldPolicyCase_MapsToLowerInvariant()
    {
        Assert.True(TryBuild(new[] { "--production-set", null, "--redacted-production", null, "--withheld-native-policy", "OMIT-NATIVE-PATH" }, out var config));
        Assert.Equal("omit-native-path", config.WithheldNativePolicy);
    }

    // --- GenerateProductionIds ---

    [Fact]
    public void GenerateProductionIds_CommaList_SplitsTrimmed()
    {
        Assert.Equal(new[] { "A", "B", "C" }, ProductionModule.GenerateProductionIds("A, B, C", 3));
    }

    [Fact]
    public void GenerateProductionIds_TrailingDigit_IncrementsWithWidth()
    {
        Assert.Equal(new[] { "PROD001", "PROD002", "PROD003" }, ProductionModule.GenerateProductionIds("PROD001", 3));
    }

    [Fact]
    public void GenerateProductionIds_UnderscoreSuffix_AppendsNumber()
    {
        Assert.Equal(new[] { "PROD", "PROD_2", "PROD_3" }, ProductionModule.GenerateProductionIds("PROD", 3));
    }

    [Fact]
    public void GenerateProductionIds_DefaultTimestamp_MatchesShape()
    {
        var single = ProductionModule.GenerateProductionIds(null, 1);
        Assert.Single(single);
        Assert.Matches("^PRODUCTION_\\d{8}_\\d{6}$", single[0]);

        var multi = ProductionModule.GenerateProductionIds(null, 2);
        Assert.Equal(2, multi.Count);
        Assert.Matches("^PRODUCTION_\\d{8}_\\d{6}_001$", multi[0]);
        Assert.Matches("^PRODUCTION_\\d{8}_\\d{6}_002$", multi[1]);
    }

    [Fact]
    public void GenerateProductionIds_CommaList_ReturnsAllElementsTrimmed()
    {
        var result = ProductionModule.GenerateProductionIds("A,B,C", 2);

        Assert.Equal(new[] { "A", "B", "C" }, result);
    }
}
