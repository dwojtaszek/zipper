using System.Threading.Channels;

namespace Zipper.Tests;

/// <summary>
/// Test sink that faults after consuming a fixed number of entries, delaying the fault
/// so bounded-channel producers observe backpressure while the consumer fails.
/// </summary>
internal sealed class DelayedFaultSink : IArchiveSink
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
        ChannelReader<FileData> fileDataReader,
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
