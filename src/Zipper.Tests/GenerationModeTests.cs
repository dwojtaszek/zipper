using Xunit;

namespace Zipper.Tests;

/// <summary>
/// End-to-end tests for the generation mode orchestration branches
/// (StandardMode, ProductionSetMode, LoadFileOnlyMode) driven through Program.Main.
/// </summary>
public class GenerationModeTests
{
    [Fact]
    public async Task Main_StandardModeWithIncludeLoadFile_EmbedsLoadFileInArchive()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = new[] { "--type", "pdf", "--count", "5", "--output-path", tempDir, "--include-load-file", "--seed", "42" };
            var result = await Program.Main(args);

            Assert.Equal(0, result);

            var zipFile = Directory.GetFiles(tempDir, "*.zip").FirstOrDefault();
            Assert.NotNull(zipFile);

            using var archive = System.IO.Compression.ZipFile.OpenRead(zipFile!);
            Assert.Contains(archive.Entries, e => e.Name.EndsWith(".dat", StringComparison.OrdinalIgnoreCase));

            // The load file lives inside the archive, so no loose load file is emitted.
            Assert.Empty(Directory.GetFiles(tempDir, "*.dat"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Main_StandardModeWithTargetZipSizeFarFromActual_WarnsButSucceeds()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            // 10KB target with 10 placeholder PDFs lands well outside the +/-10% tolerance,
            // exercising the deviation warning branch while still completing successfully.
            var args = new[] { "--type", "pdf", "--count", "10", "--output-path", tempDir, "--target-zip-size", "10KB", "--seed", "42" };
            var result = await Program.Main(args);

            Assert.Equal(0, result);
            Assert.NotNull(Directory.GetFiles(tempDir, "*.zip").FirstOrDefault());
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Main_ProductionSetModeWithProductionZip_CreatesZipArchive()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = new[]
            {
                "--production-set", "--count", "3", "--output-path", tempDir,
                "--bates-prefix", "TST", "--type", "pdf", "--production-zip", "--seed", "42",
            };
            var result = await Program.Main(args);

            Assert.Equal(0, result);

            var zipFile = Directory.GetFiles(tempDir, "*.zip", SearchOption.AllDirectories).FirstOrDefault();
            Assert.NotNull(zipFile);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Main_LoadFileOnlyModeWithChaosTypes_GeneratesLoadFile()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        try
        {
            var args = new[]
            {
                "--loadfile-only", "--count", "5", "--output-path", tempDir,
                "--chaos-mode", "--chaos-types", "quotes", "--seed", "42",
            };
            var result = await Program.Main(args);

            Assert.Equal(0, result);
            Assert.NotEmpty(Directory.GetFiles(tempDir, "*.dat"));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task Main_LoadFileOnlyModeWithChaosScenario_PrintsResolvedChaosAmount()
    {
        var tempDir = Path.Combine(Directory.GetCurrentDirectory(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            var args = new[]
            {
                "--loadfile-only", "--count", "5", "--output-path", tempDir,
                "--chaos-mode", "--chaos-scenario", "structured-import-failures", "--seed", "42",
            };
            var result = await Program.Main(args);

            Assert.Equal(0, result);
            var output = sw.ToString();

            // structured-import-failures has a default amount of 3%
            Assert.Contains("Chaos Mode: Enabled (amount: 3%)", output, StringComparison.Ordinal);
            Assert.DoesNotContain("Chaos Mode: Enabled (amount: 1%)", output, StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(originalOut);
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
