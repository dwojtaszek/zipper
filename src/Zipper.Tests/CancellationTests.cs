using Xunit;

using Zipper.Config;

namespace Zipper;

/// <summary>
/// Tests covering cooperative cancellation (Ctrl-C / CancellationToken) across all three
/// generation modes. Verifies that:
///   1. An already-cancelled token throws <see cref="OperationCanceledException"/> (not a hang).
///   2. Partial output files are deleted on cancellation (best-effort cleanup).
///   3. <see cref="GenerationRunner.RunAsync"/> returns exit code 130 on cancellation.
/// These tests do NOT require real Ctrl-C: they pass a pre-cancelled token.
/// </summary>
[Collection("ConsoleTests")]
public class CancellationTests
{
    // -----------------------------------------------------------------------
    // ParallelFileGenerator (Standard mode)
    // -----------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task GenerateFilesAsync_PreCancelledToken_ThrowsOperationCanceledException()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var generator = new ParallelFileGenerator();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                generator.GenerateFilesAsync(BuildStandardRequest(outputPath), cts.Token));
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task GenerateFilesAsync_PreCancelledToken_LeavesNoPartialZip()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var generator = new ParallelFileGenerator();
            try
            {
                await generator.GenerateFilesAsync(BuildStandardRequest(outputPath), cts.Token);
            }
            catch (OperationCanceledException) { /* expected */ }

            // No partial zip should remain after cancellation cleanup
            var zips = Directory.GetFiles(outputPath, "*.zip");
            Assert.Empty(zips);
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task GenerateFilesAsync_PreCancelledToken_LeavesNoPartialDat()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var generator = new ParallelFileGenerator();
            try
            {
                await generator.GenerateFilesAsync(BuildStandardRequest(outputPath), cts.Token);
            }
            catch (OperationCanceledException) { /* expected */ }

            // No partial dat should remain after cancellation cleanup
            var dats = Directory.GetFiles(outputPath, "*.dat");
            Assert.Empty(dats);
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    // -----------------------------------------------------------------------
    // LoadfileOnlyGenerator
    // -----------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task LoadfileOnlyGenerator_PreCancelledToken_ThrowsOperationCanceledException()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                LoadfileOnlyGenerator.GenerateAsync(BuildLoadfileOnlyRequest(outputPath), cts.Token));
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task LoadfileOnlyGenerator_PreCancelledToken_LeavesNoPartialFiles()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                await LoadfileOnlyGenerator.GenerateAsync(BuildLoadfileOnlyRequest(outputPath), cts.Token);
            }
            catch (OperationCanceledException) { /* expected */ }

            // No partial dat/opt/json should remain after cancellation cleanup
            var files = Directory.GetFiles(outputPath);
            Assert.Empty(files);
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    // -----------------------------------------------------------------------
    // ProductionSetGenerator
    // -----------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task ProductionSetGenerator_PreCancelledToken_ThrowsOperationCanceledException()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ProductionSetGenerator.GenerateAsync(BuildProductionSetRequest(outputPath), cts.Token));
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task ProductionSetGenerator_PreCancelledToken_LeavesNoPartialProductionDirectory()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                await ProductionSetGenerator.GenerateAsync(BuildProductionSetRequest(outputPath), cts.Token);
            }
            catch (OperationCanceledException) { /* expected */ }

            // No partial PRODUCTION_* directory should remain after cleanup
            var productionDirs = Directory.GetDirectories(outputPath, "PRODUCTION_*");
            Assert.Empty(productionDirs);
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    // -----------------------------------------------------------------------
    // GenerationRunner exit-code contract
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GenerationRunner_PreCancelledToken_Returns130()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // A mode that immediately honours the token
        var mode = new TokenCheckingMode();
        int exitCode = await GenerationRunner.RunAsync(mode, BuildStandardRequest(Path.GetTempPath()), cts.Token);

        Assert.Equal(130, exitCode);
    }

    [Fact]
    public async Task GenerationRunner_PreCancelledToken_WritesOperationCancelledToStderr()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var mode = new TokenCheckingMode();
        var originalError = Console.Error;
        using var errWriter = new StringWriter();
        Console.SetError(errWriter);
        try
        {
            await GenerationRunner.RunAsync(mode, BuildStandardRequest(Path.GetTempPath()), cts.Token);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Contains("Operation cancelled.", errWriter.ToString(), StringComparison.Ordinal);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static FileGenerationRequest BuildStandardRequest(string outputPath) =>
        new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = outputPath,
                FileCount = 50,
                FileType = "pdf",
                Folders = 1,
                Concurrency = 2,
            },
        };

    private static FileGenerationRequest BuildLoadfileOnlyRequest(string outputPath) =>
        new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = outputPath,
                FileCount = 1000,
                FileType = "pdf",
            },
            LoadFile = new LoadFileConfig
            {
                Formats = new List<LoadFileFormat> { LoadFileFormat.Dat },
                Encoding = "UTF-8",
            },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            LoadfileOnly = true,
        };

    private static FileGenerationRequest BuildProductionSetRequest(string outputPath) =>
        new FileGenerationRequest
        {
            Output = new OutputConfig
            {
                OutputPath = outputPath,
                FileCount = 10,
                FileType = "pdf",
            },
            LoadFile = new LoadFileConfig
            {
                Formats = new List<LoadFileFormat> { LoadFileFormat.Dat },
                Encoding = "UTF-8",
            },
            Delimiters = new DelimiterConfig { EndOfLine = "CRLF" },
            Bates = new BatesNumberConfig
            {
                Prefix = "TST",
                Start = 1,
                Digits = 7,
            },
            Production = new ProductionConfig
            {
                ProductionSet = true,
                VolumeSize = 100,
            },
        };

    private sealed class TokenCheckingMode : IGenerationMode
    {
        public Task RunAsync(FileGenerationRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
