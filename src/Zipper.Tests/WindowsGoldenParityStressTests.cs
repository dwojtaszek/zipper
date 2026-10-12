using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

// TEMPORARY diagnostic stress test for #1180 / #1181. Deleted after verification.
// Amplifies the rare Windows nondeterminism by running the exact seeded golden-parity
// scenario in a loop, so one CI run yields many samples instead of one.
public sealed class WindowsGoldenParityStressTests
{
    [Fact]
    public async Task SeededGoldenParity_HoldsAcrossManyIterations()
    {
        var iterations = 40;
        for (var i = 0; i < iterations; i++)
        {
            var tempDir1 = Path.Combine(Directory.GetCurrentDirectory(), $"Stress1_{i}_" + Guid.NewGuid().ToString("N"));
            var tempDir2 = Path.Combine(Directory.GetCurrentDirectory(), $"Stress2_{i}_" + Guid.NewGuid().ToString("N"));
            try
            {
                var result1 = await Generate(tempDir1);
                var result2 = await Generate(tempDir2);

                var manifest1 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(result1.ProductionPath);
                var manifest2 = GoldenBaselineHarness.GenerateTimestampNormalizedSha256Manifest(result2.ProductionPath);

                var lines1 = manifest1.Split('\n').Select(static l => l.TrimEnd('\r')).ToArray();
                var lines2 = manifest2.Split('\n').Select(static l => l.TrimEnd('\r')).ToArray();

                Assert.True(lines1.Length > 0, $"iteration {i}: manifest1 empty");
                Assert.Equal(lines1, lines2);
            }
            finally
            {
                foreach (var d in new[] { tempDir1, tempDir2 })
                {
                    if (Directory.Exists(d))
                    {
                        try { Directory.Delete(d, true); } catch { /* stress best-effort */ }
                    }
                }
            }
        }
    }

    private static async Task<Zipper.ProductionSetResult> Generate(string outputPath)
    {
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = outputPath,
                FileCount = 3,
                FileType = "pdf",
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 5000,
                RedactedProduction = false,
                WithheldNativePolicy = "keep-native",
                SourcePathMode = SourcePathMode.Bates,
            },
            Metadata = new MetadataConfig { Seed = null },
            Bates = new BatesNumberConfig
            {
                Prefix = "DOC",
                Start = 1,
                Digits = 8,
                Increment = 1,
            },
            Hash = new HashConfig
            {
                Mode = HashMode.Actual,
                Algorithms = new HashSet<HashAlgorithm>(),
            },
            LoadFile = new LoadFileConfig
            {
                Encoding = "UTF-8",
            },
        };
        request.Metadata = new MetadataConfig { Seed = 42 };
        request.Bates = new BatesNumberConfig { Prefix = "GOLD", Start = 1, Digits = 6, Increment = 1 };
        request.Production = request.Production with { ProductionId = "GOLDPROD", VolumeSize = 2 };

        return await ProductionSetOrchestrator.GenerateAsync(
            request,
            new ProductionFileMaterializer(),
            new HashComputer());
    }
}
