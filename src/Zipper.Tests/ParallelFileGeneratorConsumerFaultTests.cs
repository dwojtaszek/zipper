using System.Threading.Channels;
using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

/// <summary>
/// Regression test for #788: when the ZIP consumer faults, the feeder task must
/// not leak/block forever. The pipeline should return promptly with the consumer's
/// exception, even when the bounded work channel is full and producers have exited.
/// </summary>
public class ParallelFileGeneratorConsumerFaultTests
{
    [Fact(Timeout = 15000)]
    public async Task GenerateFilesAsync_ConsumerFaultsAfterOneItem_FeederDoesNotBlock()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var sink = new DelayedFaultSink(faultAfter: 1, delayBeforeFaultMs: 50);
            var generator = new ParallelFileGenerator(sink);

            var generationTask = generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 100,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = 4,
                },
            });

            var completed = await Task.WhenAny(generationTask, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(completed == generationTask, "GenerateFilesAsync did not fail within timeout (feeder likely blocked).");

            var ex = await Assert.ThrowsAnyAsync<Exception>(() => generationTask);
            Assert.Equal("Simulated consumer fault after 1 entry", ex.Message);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
    }

    [Fact(Timeout = 15000)]
    public async Task GenerateFilesAsync_ConsumerFaultsImmediately_FeederDoesNotBlock()
    {
        var tempDir = Directory.GetCurrentDirectory();
        var outputPath = Path.Combine(tempDir, Guid.NewGuid().ToString());
        Directory.CreateDirectory(outputPath);

        try
        {
            var sink = new ImmediateFaultSink();
            var generator = new ParallelFileGenerator(sink);

            var generationTask = generator.GenerateFilesAsync(new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    OutputPath = outputPath,
                    FileCount = 100,
                    FileType = "pdf",
                    Folders = 2,
                    Concurrency = 4,
                },
            });

            var completed = await Task.WhenAny(generationTask, Task.Delay(TimeSpan.FromSeconds(10)));
            Assert.True(completed == generationTask, "GenerateFilesAsync did not fail within timeout (feeder likely blocked).");

            var ex = await Assert.ThrowsAnyAsync<Exception>(() => generationTask);
            Assert.Equal("Simulated consumer fault", ex.Message);
        }
        finally
        {
            if (Directory.Exists(outputPath))
            {
                Directory.Delete(outputPath, true);
            }
        }
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

        public async Task<string> CreateArchiveAsync(string zipFilePath, string loadFileName, string loadFilePath, FileGenerationRequest request, ChannelReader<FileData> fileDataReader, CancellationToken cancellationToken = default)
        {
            try
            {
                long index = 0;
                await foreach (var fileData in fileDataReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    index++;
                    fileData.MemoryOwner?.Dispose();
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

    private sealed class ImmediateFaultSink : IArchiveSink
    {
        public Task<string> CreateArchiveAsync(string zipFilePath, string loadFileName, string loadFilePath, FileGenerationRequest request, ChannelReader<FileData> fileDataReader, CancellationToken cancellationToken = default)
        {
            throw new IOException("Simulated consumer fault");
        }
    }
}
