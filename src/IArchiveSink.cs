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

    /// <summary>
    /// Creates the archive and audits the chaos anomalies returned by <paramref name="anomalyProvider"/>.
    /// The provider is called after every item has been drained, so anomalies recorded while producers run are included.
    /// Sinks that do not audit chaos ignore the provider.
    /// </summary>
    Task<string> CreateArchiveAsync(
        string zipFilePath,
        string loadFileName,
        string loadFilePath,
        FileGenerationRequest request,
        ChannelReader<FileData> fileDataReader,
        Action<FileData>? onItemCommitted,
        CancellationToken cancellationToken,
        Func<IReadOnlyList<ChaosAnomaly>>? anomalyProvider)
        => CreateArchiveAsync(zipFilePath, loadFileName, loadFilePath, request, fileDataReader, onItemCommitted, cancellationToken);
}
