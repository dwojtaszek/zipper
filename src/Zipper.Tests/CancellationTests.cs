using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

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
    // LoadFileOnlyGenerator
    // -----------------------------------------------------------------------

    [Fact(Timeout = 10000)]
    public async Task LoadFileOnlyGenerator_PreCancelledToken_ThrowsOperationCanceledException()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                LoadFileOnlyGenerator.GenerateAsync(BuildLoadfileOnlyRequest(outputPath), cts.Token));
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact(Timeout = 10000)]
    public async Task LoadFileOnlyGenerator_PreCancelledToken_LeavesNoPartialFiles()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);
        try
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                await LoadFileOnlyGenerator.GenerateAsync(BuildLoadfileOnlyRequest(outputPath), cts.Token);
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

    [Fact(Timeout = 15000)]
    public async Task LoadFileOnlyGenerator_ActiveCancellationAfterFirstFormatWritten_ThrowsAndCleansUpAllOwnedArtifactsAndPreservesSentinels()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "LFOCancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);
        var sentinelPath = Path.Combine(outputPath, "sentinel.txt");
        var sentinelBytes = new byte[] { 0x11, 0x22, 0x33, 0x44 };
        await File.WriteAllBytesAsync(sentinelPath, sentinelBytes);

        try
        {
            using var cts = new CancellationTokenSource();
            var request = BuildLoadfileOnlyRequest(outputPath);
            request.Output = request.Output with { FileCount = 5000 };
            request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt } };

            bool generationStarted = false;
            using var monitorCts = new CancellationTokenSource();
            var monitorTask = Task.Run(async () =>
            {
                while (!monitorCts.Token.IsCancellationRequested && !cts.IsCancellationRequested)
                {
                    var dats = Directory.GetFiles(outputPath, "*.dat");
                    var jsons = Directory.GetFiles(outputPath, "*_properties.json");
                    if (dats.Length > 0 && jsons.Length > 0 && new FileInfo(dats[0]).Length > 0 && new FileInfo(jsons[0]).Length > 0)
                    {
                        generationStarted = true;
                        cts.Cancel();
                        break;
                    }
                    await Task.Delay(5);
                }
            });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                LoadFileOnlyGenerator.GenerateAsync(request, cts.Token));

            monitorCts.Cancel();
            await monitorTask;

            Assert.True(generationStarted, "Active cancellation requires proof that generation started before cancellation.");
            Assert.Empty(Directory.GetFiles(outputPath, "*.dat"));
            Assert.Empty(Directory.GetFiles(outputPath, "*.opt"));
            Assert.Empty(Directory.GetFiles(outputPath, "*.json"));
            Assert.True(File.Exists(sentinelPath), "Pre-existing sentinel file must be preserved.");
            Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinelPath));
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

    [Fact(Timeout = 15000)]
    public async Task ProductionSetGenerator_ActiveCancellationAfterNativeFileWrite_ThrowsAndCleansUpAllOwnedArtifactsAndPreservesSentinels()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "ProdNativeCancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);
        var sentinelPath = Path.Combine(outputPath, "sentinel.txt");
        var sentinelBytes = new byte[] { 0x55, 0x66, 0x77, 0x88 };
        await File.WriteAllBytesAsync(sentinelPath, sentinelBytes);

        try
        {
            using var cts = new CancellationTokenSource();
            var request = BuildProductionSetRequest(outputPath);
            request.Output = request.Output with { FileCount = 10 };

            bool generationStarted = false;
            var innerMaterializer = new ProductionFileMaterializer();
            var decorator = new FaultingMaterializerDecorator(innerMaterializer)
            {
                OnWriteBytesAsync = async (path, content, ct) =>
                {
                    await innerMaterializer.WriteBytesAsync(path, content, ct);
                    if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        generationStarted = true;
                        cts.Cancel();
                        throw new OperationCanceledException(cts.Token);
                    }
                },
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ProductionSetGenerator.GenerateAsync(request, decorator, cts.Token));

            Assert.True(generationStarted, "Active cancellation requires proof that generation started before cancellation.");
            Assert.Empty(Directory.GetDirectories(outputPath, "PRODUCTION_*"));
            Assert.Empty(Directory.GetFiles(outputPath, "*.zip"));
            Assert.True(File.Exists(sentinelPath), "Pre-existing sentinel file must be preserved.");
            Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task ProductionSetGenerator_ActiveCancellationAfterFirstRollingSetCompletes_ThrowsAndCleansUpEarlierSetsAndPreservesSentinels()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "ProdRollingCancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);
        var sentinelPath = Path.Combine(outputPath, "sentinel.txt");
        var sentinelBytes = new byte[] { 0x99, 0xAA, 0xBB, 0xCC };
        await File.WriteAllBytesAsync(sentinelPath, sentinelBytes);

        try
        {
            using var cts = new CancellationTokenSource();
            var request = BuildProductionSetRequest(outputPath);
            request.Output = request.Output with { FileCount = 10 };
            request.Production = request.Production with
            {
                RollingCount = 2,
                ProductionId = "PROD_1,PROD_2",
                VolumeSize = 10,
            };

            bool generationStarted = false;
            var innerMaterializer = new ProductionFileMaterializer();
            var decorator = new FaultingMaterializerDecorator(innerMaterializer)
            {
                OnWriteBytesAsync = async (path, content, ct) =>
                {
                    if (path.Contains("PROD_2", StringComparison.Ordinal) && path.Contains("NATIVES", StringComparison.Ordinal))
                    {
                        var prod1Manifest = Path.Combine(outputPath, "PROD_1", "_manifest.json");
                        Assert.True(File.Exists(prod1Manifest), "Active cancellation requires proof that set 1 completed before cancellation.");
                        generationStarted = true;
                        cts.Cancel();
                        throw new OperationCanceledException(cts.Token);
                    }

                    await innerMaterializer.WriteBytesAsync(path, content, ct);
                },
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ProductionSetGenerator.GenerateAsync(request, decorator, cts.Token));

            Assert.True(generationStarted, "Active cancellation requires proof that set 1 completed before cancellation.");
            Assert.False(Directory.Exists(Path.Combine(outputPath, "PROD_1")), "Completed set 1 must be cleaned up on cancellation.");
            Assert.False(Directory.Exists(Path.Combine(outputPath, "PROD_2")), "Partial set 2 must be cleaned up on cancellation.");
            Assert.Empty(Directory.GetFiles(outputPath, "*.zip"));
            Assert.True(File.Exists(sentinelPath), "Pre-existing sentinel file must be preserved.");
            Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task ProductionSetGenerator_ActiveCancellationDuringZipCreation_ThrowsAndCleansUpZipAndDirectoryAndPreservesSentinels()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "ProdZipCancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);
        var sentinelPath = Path.Combine(outputPath, "sentinel.txt");
        var sentinelBytes = new byte[] { 0x33, 0x44, 0x55, 0x66 };
        await File.WriteAllBytesAsync(sentinelPath, sentinelBytes);

        try
        {
            using var cts = new CancellationTokenSource();
            var request = BuildProductionSetRequest(outputPath);
            request.Output = request.Output with { FileCount = 20 };
            request.Production = request.Production with
            {
                ProductionZip = true,
                ProductionId = "PROD_ZIP_TEST",
            };

            bool generationStarted = false;
            var innerMaterializer = new ProductionFileMaterializer();
            var decorator = new FaultingMaterializerDecorator(innerMaterializer)
            {
                OnCreateZipAsync = async (sourceDir, zipPath, method, ct) =>
                {
                    var manifestPath = Path.Combine(sourceDir, "_manifest.json");
                    Assert.True(File.Exists(manifestPath), "Active cancellation requires proof that production set files existed before zip cancellation.");
                    generationStarted = true;
                    cts.Cancel();
                    await innerMaterializer.CreateZipAsync(sourceDir, zipPath, method, cts.Token);
                },
            };

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                ProductionSetGenerator.GenerateAsync(request, decorator, cts.Token));

            Assert.True(generationStarted, "Active cancellation requires proof that production set files existed before zip cancellation.");
            Assert.Empty(Directory.GetDirectories(outputPath, "PROD_ZIP_TEST*"));
            Assert.Empty(Directory.GetFiles(outputPath, "*.zip"));
            Assert.True(File.Exists(sentinelPath), "Pre-existing sentinel file must be preserved.");
            Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinelPath));
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

    [Fact(Timeout = 15000)]
    public async Task GenerationRunner_ActiveCancellationDuringLoadfileOnlyMode_Returns130AndCleansUp()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "RunnerLFOCancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);
        var sentinelPath = Path.Combine(outputPath, "sentinel.txt");
        var sentinelBytes = new byte[] { 0x77, 0x88, 0x99, 0x00 };
        await File.WriteAllBytesAsync(sentinelPath, sentinelBytes);

        try
        {
            using var cts = new CancellationTokenSource();
            var request = BuildLoadfileOnlyRequest(outputPath);
            request.Output = request.Output with { FileCount = 5000 };
            request.LoadFile = request.LoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt } };

            bool generationStarted = false;
            using var monitorCts = new CancellationTokenSource();
            var monitorTask = Task.Run(async () =>
            {
                while (!monitorCts.Token.IsCancellationRequested && !cts.IsCancellationRequested)
                {
                    var dats = Directory.GetFiles(outputPath, "*.dat");
                    var jsons = Directory.GetFiles(outputPath, "*_properties.json");
                    if (dats.Length > 0 && jsons.Length > 0 && new FileInfo(dats[0]).Length > 0 && new FileInfo(jsons[0]).Length > 0)
                    {
                        generationStarted = true;
                        cts.Cancel();
                        break;
                    }
                    await Task.Delay(5);
                }
            });

            var originalError = Console.Error;
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);
            int exitCode;
            try
            {
                var mode = new LoadFileOnlyMode((req, ct) => LoadFileOnlyGenerator.GenerateAsync(req, ct));
                exitCode = await GenerationRunner.RunAsync(mode, request, cts.Token);
            }
            finally
            {
                Console.SetError(originalError);
            }

            monitorCts.Cancel();
            await monitorTask;

            Assert.True(generationStarted, "Active cancellation requires proof that generation started before cancellation.");
            Assert.Equal(130, exitCode);
            Assert.Contains("Operation cancelled.", errWriter.ToString(), StringComparison.Ordinal);
            Assert.Empty(Directory.GetFiles(outputPath, "*.dat"));
            Assert.Empty(Directory.GetFiles(outputPath, "*.opt"));
            Assert.Empty(Directory.GetFiles(outputPath, "*.json"));
            Assert.True(File.Exists(sentinelPath), "Pre-existing sentinel file must be preserved.");
            Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact(Timeout = 15000)]
    public async Task GenerationRunner_ActiveCancellationDuringProductionSetMode_Returns130AndCleansUp()
    {
        var outputPath = Path.Combine(Path.GetTempPath(), "RunnerProdCancel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);
        var sentinelPath = Path.Combine(outputPath, "sentinel.txt");
        var sentinelBytes = new byte[] { 0xAA, 0xBB, 0xCC, 0x11 };
        await File.WriteAllBytesAsync(sentinelPath, sentinelBytes);

        try
        {
            using var cts = new CancellationTokenSource();
            var request = BuildProductionSetRequest(outputPath);
            request.Output = request.Output with { FileCount = 10 };

            bool generationStarted = false;
            var innerMaterializer = new ProductionFileMaterializer();
            var decorator = new FaultingMaterializerDecorator(innerMaterializer)
            {
                OnWriteBytesAsync = async (path, content, ct) =>
                {
                    await innerMaterializer.WriteBytesAsync(path, content, ct);
                    if (path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        generationStarted = true;
                        cts.Cancel();
                        throw new OperationCanceledException(cts.Token);
                    }
                },
            };

            var originalError = Console.Error;
            using var errWriter = new StringWriter();
            Console.SetError(errWriter);
            int exitCode;
            try
            {
                var mode = new ProductionSetMode((req, ct) => ProductionSetGenerator.GenerateAsync(req, decorator, ct));
                exitCode = await GenerationRunner.RunAsync(mode, request, cts.Token);
            }
            finally
            {
                Console.SetError(originalError);
            }

            Assert.True(generationStarted, "Active cancellation requires proof that generation started before cancellation.");
            Assert.Equal(130, exitCode);
            Assert.Contains("Operation cancelled.", errWriter.ToString(), StringComparison.Ordinal);
            Assert.Empty(Directory.GetDirectories(outputPath, "PRODUCTION_*"));
            Assert.Empty(Directory.GetFiles(outputPath, "*.zip"));
            Assert.True(File.Exists(sentinelPath), "Pre-existing sentinel file must be preserved.");
            Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinelPath));
        }
        finally
        {
            if (Directory.Exists(outputPath)) Directory.Delete(outputPath, true);
        }
    }

    [Fact]
    public async Task ArchiveTestCliWorkflow_PreCancelledToken_Returns130AndLeavesNoDestination()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var destination = Path.Combine(Directory.GetCurrentDirectory(), $"atc-cancel-{Guid.NewGuid():N}");
        var modules = Cli.Modules.CliModules.Create();
        Assert.True(modules.Parse(new[] { "--archive-test-suite", "smoke", "--output-path", destination }));

        int exitCode = await ArchiveTests.ArchiveTestCliWorkflow.RunAsync(modules, cts.Token);

        Assert.Equal(130, exitCode);
        Assert.False(Directory.Exists(destination));
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
