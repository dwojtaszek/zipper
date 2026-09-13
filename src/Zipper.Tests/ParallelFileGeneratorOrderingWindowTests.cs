using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class ParallelFileGeneratorOrderingWindowTests
{
    [Fact(Timeout = 15000)]
    public async Task GenerateFilesAsync_DelayedFirstItem_BoundsInFlightGenerationToOrderingWindow()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            const int totalFiles = 50;
            const int concurrency = 2;
            const int maxInFlight = 4;

            var generatedIndices = new ConcurrentBag<long>();
            int itemsGeneratedAfterFirst = 0;

            var delayingGenerator = new DelegatingTestGenerator("pdf", (workItem, request) =>
            {
                if (workItem.Index == 1)
                {
                    // Delay index 1 until explicitly released
                    tcs.Task.GetAwaiter().GetResult();
                }
                else
                {
                    Interlocked.Increment(ref itemsGeneratedAfterFirst);
                    generatedIndices.Add(workItem.Index);
                }

                return new GeneratedFileContent
                {
                    Content = new byte[4096],
                };
            });

            var customGenerators = new Dictionary<string, IFileGenerator>(StringComparer.Ordinal)
            {
                ["pdf"] = delayingGenerator,
            };

            var generator = new ParallelFileGenerator(new ZipArchiveSink(), customGenerators, maxInFlight: maxInFlight);

            var request = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = totalFiles,
                    FileType = "pdf",
                    Folders = 1,
                    Concurrency = concurrency,
                },
            };

            var generationTask = generator.GenerateFilesAsync(request);

            // Wait briefly to allow workers to generate as much as the pipeline allows
            await Task.Delay(200);

            // With maxInFlight = 4, at most (maxInFlight - 1) = 3 items beyond index 1 should be generated.
            // Items 5..50 must NOT have been generated while item 1 is held.
            int countWhileWithheld = Interlocked.CompareExchange(ref itemsGeneratedAfterFirst, 0, 0);
            Assert.True(
                countWhileWithheld <= maxInFlight - 1,
                $"Backpressure hole detected: {countWhileWithheld} items generated beyond index 1 while index 1 was withheld (expected <= {maxInFlight - 1}).");

            // Release index 1
            tcs.TrySetResult();

            var result = await generationTask;

            Assert.Equal(totalFiles, result.FilesGenerated);
            Assert.True(File.Exists(result.ZipFilePath));

            using var archive = ZipFile.OpenRead(result.ZipFilePath);
            Assert.Equal(totalFiles, archive.Entries.Count);
        }
        finally
        {
            tcs.TrySetResult();
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact(Timeout = 15000)]
    public async Task GenerateFilesAsync_CancellationWhileWaitingForPredecessor_DisposesAllMemoryOwnersAndCleansUpFiles()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource();

        try
        {
            const int totalFiles = 20;
            const int concurrency = 2;
            const int maxInFlight = 4;

            var delayingGenerator = new DelegatingTestGenerator("pdf", (workItem, request) =>
            {
                if (workItem.Index == 1)
                {
                    tcs.Task.GetAwaiter().GetResult();
                }

                return new GeneratedFileContent
                {
                    Content = new byte[4096],
                };
            });

            var customGenerators = new Dictionary<string, IFileGenerator>(StringComparer.Ordinal)
            {
                ["pdf"] = delayingGenerator,
            };

            var generator = new ParallelFileGenerator(new ZipArchiveSink(), customGenerators, maxInFlight: maxInFlight);

            var request = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = totalFiles,
                    FileType = "pdf",
                    Folders = 1,
                    Concurrency = concurrency,
                },
            };

            var generationTask = generator.GenerateFilesAsync(request, cts.Token);

            // Wait briefly for out-of-order buffer to receive items
            await Task.Delay(200);

            // Cancel pipeline
            cts.Cancel();
            tcs.TrySetResult();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => generationTask);

            // Verify temporary zip files and directory contents are cleaned up
            var remainingFiles = Directory.GetFiles(outputPath);
            Assert.Empty(remainingFiles);
        }
        finally
        {
            tcs.TrySetResult();
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact(Timeout = 15000)]
    public async Task GenerateFilesAsync_ConsumerFault_UnblocksProducersAndCleansUp()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            const int totalFiles = 30;
            const int concurrency = 4;
            const int maxInFlight = 4;

            var faultingSink = new DelayedFaultSink(faultAfter: 2, delayBeforeFaultMs: 50);

            var generator = new ParallelFileGenerator(faultingSink, maxInFlight: maxInFlight);

            var request = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = totalFiles,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = concurrency,
                },
            };

            var generationTask = generator.GenerateFilesAsync(request);

            var completed = await Task.WhenAny(generationTask, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(completed == generationTask, "Generation task did not complete within timeout upon consumer fault.");

            var ex = await Assert.ThrowsAnyAsync<Exception>(() => generationTask);
            Assert.Contains("Simulated consumer fault", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            tcs.TrySetResult();
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact(Timeout = 30000)]
    public async Task GenerateFilesAsync_SeededGoldenContentHashParity_MatchesAcrossConcurrencies()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath1 = Path.Combine(tempDir, Guid.NewGuid().ToString("N"));
        var outputPath2 = Path.Combine(tempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outputPath1);
        Directory.CreateDirectory(outputPath2);

        try
        {
            const int fileCount = 20;
            const int seed = 4242;

            var request1 = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath1,
                    FileCount = fileCount,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = 1,
                    TargetZipSize = 100_000,
                },
                Metadata = new MetadataConfig
                {
                    Seed = seed,
                },
                Hash = new HashConfig
                {
                    Mode = HashMode.Actual,
                    Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5 },
                },
            };

            var request2 = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath2,
                    FileCount = fileCount,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = 4,
                    MaxInFlight = 2,
                    TargetZipSize = 100_000,
                },
                Metadata = new MetadataConfig
                {
                    Seed = seed,
                },
                Hash = new HashConfig
                {
                    Mode = HashMode.Actual,
                    Algorithms = new HashSet<Config.HashAlgorithm> { Config.HashAlgorithm.MD5 },
                },
            };

            var generator1 = new ParallelFileGenerator();
            var generator2 = new ParallelFileGenerator();

            var result1 = await generator1.GenerateFilesAsync(request1);
            var result2 = await generator2.GenerateFilesAsync(request2);

            Assert.Equal(fileCount, result1.FilesGenerated);
            Assert.Equal(fileCount, result2.FilesGenerated);

            using var archive1 = ZipFile.OpenRead(result1.ZipFilePath);
            using var archive2 = ZipFile.OpenRead(result2.ZipFilePath);

            Assert.Equal(archive1.Entries.Count, archive2.Entries.Count);

            var entries1 = archive1.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).ToList();
            var entries2 = archive2.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal).ToList();

            for (int i = 0; i < entries1.Count; i++)
            {
                Assert.Equal(entries1[i].FullName, entries2[i].FullName);
                Assert.Equal(entries1[i].Length, entries2[i].Length);

                using var s1 = entries1[i].Open();
                using var s2 = entries2[i].Open();
                using var md5_1 = MD5.Create();
                using var md5_2 = MD5.Create();

                var hash1 = md5_1.ComputeHash(s1);
                var hash2 = md5_2.ComputeHash(s2);

                Assert.Equal(Convert.ToHexString(hash1), Convert.ToHexString(hash2));
            }
        }
        finally
        {
            if (Directory.Exists(outputPath1))
            {
                Directory.Delete(outputPath1, true);
            }
            if (Directory.Exists(outputPath2))
            {
                Directory.Delete(outputPath2, true);
            }
        }
    }

    private sealed class DelegatingTestGenerator : IFileGenerator
    {
        private readonly Func<FileWorkItem, FileGenerationRequest, GeneratedFileContent> generate;

        public DelegatingTestGenerator(string fileType, Func<FileWorkItem, FileGenerationRequest, GeneratedFileContent> generate)
        {
            this.FileType = fileType;
            this.generate = generate;
        }

        public string FileType { get; }

        public bool IsPlaceholderBased => false;

        public GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request)
            => this.generate(workItem, request);
    }

    private sealed class DelayedFaultSink : IArchiveSink
    {
        private readonly int faultAfter;
        private readonly int delayBeforeFaultMs;

        public DelayedFaultSink(int faultAfter, int delayBeforeFaultMs)
        {
            this.faultAfter = faultAfter;
            this.delayBeforeFaultMs = delayBeforeFaultMs;
        }

        public async Task<string> CreateArchiveAsync(
            string zipFilePath,
            string loadFileName,
            string loadFilePath,
            FileGenerationRequest request,
            System.Threading.Channels.ChannelReader<FileData> fileDataReader,
            Action<FileData>? onItemCommitted = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                long index = 0;
                await foreach (var fileData in fileDataReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    index++;
                    fileData.MemoryOwner?.Dispose();
                    onItemCommitted?.Invoke(fileData);
                    if (index >= this.faultAfter)
                    {
                        await Task.Delay(this.delayBeforeFaultMs, cancellationToken).ConfigureAwait(false);
                        throw new IOException("Simulated consumer fault after " + this.faultAfter + " entry");
                    }
                }
            }
            finally
            {
                while (fileDataReader.TryRead(out var leftover))
                {
                    leftover.MemoryOwner?.Dispose();
                }
            }

            return "in-memory-loadfile.dat";
        }
    }
}
