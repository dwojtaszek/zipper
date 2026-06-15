using System.Threading.Channels;

namespace Zipper;

internal class InMemorySink : IArchiveSink
{
    public List<FileData> CapturedEntries { get; } = new List<FileData>();
    public bool IsFaulted { get; set; }
    public Exception? FaultException { get; set; }

    public async Task<string> CreateArchiveAsync(
        string zipFilePath,
        string loadFileName,
        string loadFilePath,
        FileGenerationRequest request,
        ChannelReader<FileData> fileDataReader,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (IsFaulted && FaultException != null)
            {
                throw FaultException;
            }

            await foreach (var fileData in fileDataReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var snapshot = fileData.Data.ToArray();
                CapturedEntries.Add(fileData with
                {
                    Data = snapshot,
                    DataLength = snapshot.Length,
                    MemoryOwner = null,
                });
                fileData.MemoryOwner?.Dispose();
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
