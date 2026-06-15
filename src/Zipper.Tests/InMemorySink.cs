using System.Threading.Channels;

namespace Zipper
{
    internal class InMemorySink : IArchiveSink
    {
        public List<FileData> CapturedFiles { get; } = new();
        public Exception? InjectedFault { get; set; }

        public async Task<string> CreateArchiveAsync(
            string zipFilePath,
            string loadFileName,
            string loadFilePath,
            FileGenerationRequest request,
            ChannelReader<FileData> fileDataReader,
            CancellationToken cancellationToken = default)
        {
            if (InjectedFault != null)
            {
                throw InjectedFault;
            }

            await foreach (var file in fileDataReader.ReadAllAsync(cancellationToken))
            {
                CapturedFiles.Add(file);
                // The real sink disposes MemoryOwner when done
                file.MemoryOwner?.Dispose();
            }

            return loadFilePath;
        }
    }
}
