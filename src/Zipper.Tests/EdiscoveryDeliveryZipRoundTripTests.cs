using Xunit;
using Xunit.Abstractions;
using Zipper.Tests.TestHelpers;

namespace Zipper.Tests;

/// <summary>
/// End-to-end round-trip ZIP generation verification for e-discovery deliveries (#1464).
/// Tests Standard mode with --include-load-file and Production Set mode with --production-zip,
/// multi-volume splitting, mixed file types (PDF, Office, Email with Attachments, TIFF, Extracted Text),
/// independent reader streaming content hash matching, and opt-in 300- and 5,000-document scale recipes.
/// </summary>
[Collection("ConsoleTests")]
public class EdiscoveryDeliveryZipRoundTripTests : WorkingDirectoryTestBase
{
    private readonly ITestOutputHelper output;

    public EdiscoveryDeliveryZipRoundTripTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    private static bool ScaleRecipesEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("ZIPPER_SCALE_RECIPES"), "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("ZIPPER_SCALE_RECIPES"), "true", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public async Task StandardMode_WithIncludeLoadFile_RoundTripZipVerification()
    {
        var testDir = Path.Combine(this.TempDir, $"std_rt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var args = new[]
            {
                "--types", "pdf:1,docx:1,eml:1,tiff:1",
                "--count", "4",
                "--output-path", testDir,
                "--include-load-file",
                "--with-families",
                "--attachment-rate", "100",
                "--with-text",
                "--seed", "42",
            };

            var request = Cli.Pipeline.Build(args);
            Assert.NotNull(request);

            var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() => Program.Main(args));
            Assert.Equal(0, exitCode);

            var zipFiles = Directory.GetFiles(testDir, "*.zip");
            var zipFile = Assert.Single(zipFiles);

            var report = await EdiscoveryDeliveryZipVerifier.VerifyStandardModeZipDeliveryAsync(zipFile, request!, this.output);

            Assert.True(report.DocumentCount > 0, "Document count must be greater than zero.");
            Assert.True(report.NativeFileCount > 0, "Native file count must be greater than zero.");
            Assert.True(report.ZipMemberCount > 0, "ZIP member count must be greater than zero.");
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [Fact]
    public async Task ProductionSetMode_WithProductionZip_MultiVolume_RoundTripZipVerification()
    {
        var testDir = Path.Combine(this.TempDir, $"prod_rt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var args = new[]
            {
                "--production-set",
                "--production-zip",
                "--types", "pdf:1,docx:1,eml:1,tiff:1",
                "--count", "4",
                "--output-path", testDir,
                "--volume-size", "2",
                "--bates-prefix", "PROD",
                "--with-families",
                "--attachment-rate", "100",
                "--with-text",
                "--seed", "42",
            };

            var request = Cli.Pipeline.Build(args);
            Assert.NotNull(request);

            var (exitCode, stdout, stderr) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() => Program.Main(args));
            Assert.Equal(0, exitCode);

            var prodDirs = Directory.GetDirectories(testDir, "PRODUCTION_*");
            var prodDir = Assert.Single(prodDirs);

            var zipFiles = Directory.GetFiles(testDir, "PRODUCTION_*.zip");
            var zipFile = Assert.Single(zipFiles);

            var report = await EdiscoveryDeliveryZipVerifier.VerifyProductionSetZipDeliveryAsync(zipFile, prodDir, request!, this.output);

            Assert.True(report.VolumeCount >= 2, $"Expected at least 2 volumes, got {report.VolumeCount}.");
            Assert.True(report.DocumentCount > 0, "Document count must be greater than zero.");
            Assert.True(report.NativeFileCount > 0, "Native file count must be greater than zero.");
            Assert.True(report.ZipMemberCount > 0, "ZIP member count must be greater than zero.");
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [SkippableFact]
    [Trait("Category", "Scale")]
    public async Task ProductionSetMode_ScaleRecipe_300Documents()
    {
        Skip.If(!ScaleRecipesEnabled, "Scale recipes are opt-in. Set ZIPPER_SCALE_RECIPES=1 to run.");

        var testDir = Path.Combine(this.TempDir, $"scale300_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var report = await RunProductionSetScaleRecipeAsync(300, 100, testDir, this.output);

            Assert.True(report.VolumeCount >= 3, $"Expected at least 3 volumes for 300 docs with volume size 100, got {report.VolumeCount}.");
            Assert.True(report.DocumentCount >= 300, "Document count must be >= 300.");
            Assert.True(report.NativeFileCount >= 300, "Native file count must be >= 300.");
            Assert.True(report.ZipMemberCount > 300, "ZIP member count must exceed native file count.");
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    [SkippableFact]
    [Trait("Category", "Scale")]
    public async Task ProductionSetMode_ScaleRecipe_5000Documents()
    {
        Skip.If(!ScaleRecipesEnabled, "Scale recipes are opt-in. Set ZIPPER_SCALE_RECIPES=1 to run.");

        var testDir = Path.Combine(this.TempDir, $"scale5000_{Guid.NewGuid():N}");
        Directory.CreateDirectory(testDir);

        try
        {
            var report = await RunProductionSetScaleRecipeAsync(5000, 1000, testDir, this.output);

            Assert.True(report.VolumeCount >= 5, $"Expected at least 5 volumes for 5000 docs with volume size 1000, got {report.VolumeCount}.");
            Assert.True(report.DocumentCount >= 5000, "Document count must be >= 5000.");
            Assert.True(report.NativeFileCount >= 5000, "Native file count must be >= 5000.");
            Assert.True(report.ZipMemberCount > 5000, "ZIP member count must exceed native file count.");
        }
        finally
        {
            if (Directory.Exists(testDir))
            {
                Directory.Delete(testDir, true);
            }
        }
    }

    public static async Task<EdiscoveryDeliveryReport> RunProductionSetScaleRecipeAsync(
        int documentCount,
        int volumeSize,
        string outputDirectory,
        ITestOutputHelper? output = null)
    {
        var args = new[]
        {
            "--production-set",
            "--production-zip",
            "--types", "pdf:1,docx:1,eml:1,tiff:1",
            "--count", documentCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--output-path", outputDirectory,
            "--volume-size", volumeSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--bates-prefix", "PROD",
            "--with-families",
            "--attachment-rate", "100",
            "--with-text",
            "--seed", "42",
        };

        var request = Cli.Pipeline.Build(args);
        Assert.NotNull(request);

        var (exitCode, _, _) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(() => Program.Main(args));
        Assert.Equal(0, exitCode);

        var prodDirs = Directory.GetDirectories(outputDirectory, "PRODUCTION_*");
        var prodDir = Assert.Single(prodDirs);

        var zipFiles = Directory.GetFiles(outputDirectory, "PRODUCTION_*.zip");
        var zipFile = Assert.Single(zipFiles);

        return await EdiscoveryDeliveryZipVerifier.VerifyProductionSetZipDeliveryAsync(zipFile, prodDir, request!, output);
    }
}
