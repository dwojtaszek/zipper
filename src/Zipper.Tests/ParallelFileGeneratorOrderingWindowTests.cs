using System.Collections.Concurrent;
using System.IO.Compression;
using System.Threading.Channels;
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

        try
        {
            const int totalFiles = 50;
            const int concurrency = 2;
            const int maxInFlight = 4;

            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
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
            tcs.SetResult();

            var result = await generationTask;

            Assert.Equal(totalFiles, result.FilesGenerated);
            Assert.True(File.Exists(result.ZipFilePath));

            using var archive = ZipFile.OpenRead(result.ZipFilePath);
            Assert.Equal(totalFiles, archive.Entries.Count);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    private sealed class DelegatingTestGenerator : IFileGenerator
    {
        private readonly Func<FileWorkItem, FileGenerationRequest, GeneratedFileContent> _generate;

        public DelegatingTestGenerator(string fileType, Func<FileWorkItem, FileGenerationRequest, GeneratedFileContent> generate)
        {
            FileType = fileType;
            _generate = generate;
        }

        public string FileType { get; }

        public bool IsPlaceholderBased => false;

        public GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request)
            => _generate(workItem, request);
    }
}
