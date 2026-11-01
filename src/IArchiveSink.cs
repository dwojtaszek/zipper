using System.Threading.Channels;

namespace Zipper;

internal interface IArchiveSink
{
    Task<string> CreateArchiveAsync(
        string zipFilePath,
        string loadFileName,
        string loadFilePath,
        FileGenerationRequest request,
        ChannelReader<FileData> fileDataReader,
        Action<FileData>? onItemCommitted = null,
        CancellationToken cancellationToken = default);

    Task<string> CreateArchiveAsync(
        string zipFilePath,
        string loadFileName,
        string loadFilePath,
        FileGenerationRequest request,
        ChannelReader<FileData> fileDataReader,
        Action<FileData>? onItemCommitted,
        CancellationToken cancellationToken,
        IReadOnlyList<ChaosAnomaly>? anomalies)
        => CreateArchiveAsync(zipFilePath, loadFileName, loadFilePath, request, fileDataReader, onItemCommitted, cancellationToken);
}
