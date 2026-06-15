using System.Threading.Channels;

namespace Zipper
{
    internal interface IArchiveSink
    {
        Task<string> CreateArchiveAsync(
            string zipFilePath,
            string loadFileName,
            string loadFilePath,
            FileGenerationRequest request,
            ChannelReader<FileData> fileDataReader,
            CancellationToken cancellationToken = default);
    }
}
