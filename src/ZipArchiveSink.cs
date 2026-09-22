using System.IO.Compression;
using System.Threading.Channels;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Handles Archive creation and Native File writing operations
/// Extracted from ParallelFileGenerator to follow single responsibility principle.
/// </summary>
internal class ZipArchiveSink : IArchiveSink
{
    /// <summary>
    /// Gets or sets an existing archive stream to use, if provided.
    /// </summary>
    public Stream? ArchiveStream { get; set; }

    /// <summary>
    /// Creates an Archive containing the generated Native Files and optionally a Load File.
    /// </summary>
    /// <param name="zipFilePath">Path where the Archive should be created.</param>
    /// <param name="loadFileName">Name of the Load File (if included).</param>
    /// <param name="loadFilePath">Path where Load File should be saved separately (if not included in Archive).</param>
    /// <param name="request">File generation request parameters.</param>
    /// <param name="fileDataReader">Channel reader for receiving generated file data.</param>
    /// <param name="onItemCommitted">Callback invoked when an item is committed and disposed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The actual Load File path that was created (or original if included in Archive).</returns>
    public async Task<string> CreateArchiveAsync(
        string zipFilePath,
        string loadFileName,
        string loadFilePath,
        FileGenerationRequest request,
        ChannelReader<FileData> fileDataReader,
        Action<FileData>? onItemCommitted = null,
        CancellationToken cancellationToken = default)
    {
        var archiveStream = this.ArchiveStream;
        if (archiveStream is null)
        {
            archiveStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, PerformanceConstants.DefaultBufferSize, useAsync: true);
        }

        var createdSidecars = new List<string>();

        try
        {
            await using (archiveStream.ConfigureAwait(false))
            using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                using var processedFiles = new DiskBackedFileDataList();
                var usedEntryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Cached per-type text payloads; the per-record File Type selects which one is written.
                var standardTextContent = request.Output.WithText ? PlaceholderFiles.ExtractedText : Array.Empty<byte>();
                var emlTextContent = request.Output.WithText ? PlaceholderFiles.EmlExtractedText : Array.Empty<byte>();

                var outOfOrderBuffer = new Dictionary<long, FileData>();
                var compressionLevel = ResolveCompressionLevel(request.Output.CompressionMethod);
                var processingContext = new ZipProcessingContext(
                    archive,
                    request,
                    standardTextContent,
                    emlTextContent,
                    usedEntryPaths,
                    processedFiles,
                    onItemCommitted,
                    compressionLevel);

                try
                {
                    await DrainReaderAndOrderFilesAsync(processingContext, fileDataReader, outOfOrderBuffer, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    CleanupOperations(outOfOrderBuffer, fileDataReader);
                }

                var formatsToGenerate = (request.LoadFile.Formats is not null && request.LoadFile.Formats.Count > 0)
                    ? request.LoadFile.Formats
                    : new List<LoadFileFormat> { LoadFileFormat.Dat };

                var baseFileName = Path.GetFileNameWithoutExtension(loadFileName);
                var baseFilePath = Path.GetDirectoryName(loadFilePath) ?? string.Empty;

                // Load Files land inside the ZIP (IncludeLoadFile) or next to it on disk; the
                // orchestrator owns the per-format write + audit loop in either case.
                // CancellationToken.None: prior behavior, this phase was non-cancellable, so a
                // cancellation cannot leave partial DAT/OPT sidecars behind in the output dir.
                Func<string, Stream> openTarget = request.Output.IncludeLoadFile
                    ? target => CreateTrackedEntry(archive, target, usedEntryPaths, compressionLevel).Open()
                    : target =>
                    {
                        var fullPath = Path.Combine(baseFilePath, target);
                        var stream = new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, PerformanceConstants.DefaultBufferSize, useAsync: true);
                        createdSidecars.Add(fullPath);
                        return stream;
                    };

                var actualLoadFileTarget = await LoadFileOrchestrator.EmitAllAsync(
                    request,
                    processedFiles,
                    formatsToGenerate,
                    WriterMode.Standard,
                    (format, writer) =>
                    {
                        var actualLoadFileName = baseFileName + writer.FileExtension;
                        return (actualLoadFileName, actualLoadFileName + "_properties.json");
                    },
                    openTarget,
                    CancellationToken.None).ConfigureAwait(false);

                return request.Output.IncludeLoadFile
                    ? actualLoadFileTarget
                    : Path.Combine(baseFilePath, actualLoadFileTarget);
            }
        }
        catch
        {
            foreach (var sidecar in createdSidecars)
            {
                if (File.Exists(sidecar))
                {
                    try
                    {
                        File.Delete(sidecar);
                    }
#pragma warning disable CA1031
#pragma warning disable RCS1075
                    catch (Exception)
                    {
                        // Suppress cleanup exceptions to preserve original error
                    }
#pragma warning restore RCS1075
#pragma warning restore CA1031
                }
            }

            throw;
        }
    }

    private static async Task DrainReaderAndOrderFilesAsync(
        ZipProcessingContext context,
        ChannelReader<FileData> fileDataReader,
        Dictionary<long, FileData> outOfOrderBuffer,
        CancellationToken cancellationToken)
    {
        long nextExpectedIndex = 1;

        await foreach (var incomingFileData in fileDataReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (incomingFileData.WorkItem.Index == nextExpectedIndex)
            {
                ProcessFileData(context, incomingFileData);
                nextExpectedIndex++;

                while (outOfOrderBuffer.Remove(nextExpectedIndex, out var buffered))
                {
                    ProcessFileData(context, buffered);
                    nextExpectedIndex++;
                }
            }
            else
            {
                outOfOrderBuffer[incomingFileData.WorkItem.Index] = incomingFileData;
            }
        }
    }

    private static void CleanupOperations(Dictionary<long, FileData> outOfOrderBuffer, ChannelReader<FileData> fileDataReader)
    {
        foreach (var buffered in outOfOrderBuffer.Values)
        {
            buffered.MemoryOwner?.Dispose();
        }
        outOfOrderBuffer.Clear();

        while (fileDataReader.TryRead(out var leftover))
        {
            leftover.MemoryOwner?.Dispose();
        }
    }

    private static void ProcessFileData(ZipProcessingContext context, FileData fileData)
    {
        try
        {
            context.ProcessedFiles.Add(fileData);

            WriteFileToArchive(context.Archive, fileData, context.UsedEntryPaths, context.CompressionLevel);

            if (context.Request.Output.WithText)
            {
                var textContent = string.Equals(fileData.WorkItem.EffectiveFileType(context.Request), "eml", StringComparison.Ordinal)
                    ? context.EmlTextContent
                    : context.StandardTextContent;
                WriteExtractedTextToArchive(context.Archive, fileData, context.Request, textContent, context.UsedEntryPaths, context.CompressionLevel);
            }

            if (fileData.Attachment.HasValue)
            {
                WriteAttachmentToArchive(context.Archive, fileData, context.UsedEntryPaths, context.CompressionLevel);
            }

            if (fileData.Attachment.HasValue && context.Request.Output.WithText)
            {
                WriteAttachmentTextToArchive(context.Archive, fileData, context.UsedEntryPaths, context.CompressionLevel);
            }
        }
        finally
        {
            fileData.MemoryOwner?.Dispose();
            context.OnItemCommitted?.Invoke(fileData);
        }
    }

    private static CompressionLevel ResolveCompressionLevel(Config.ZipCompressionMethod method) => method switch
    {
        Config.ZipCompressionMethod.Store => CompressionLevel.NoCompression,
        Config.ZipCompressionMethod.Deflate => CompressionLevel.Optimal,
        // No wildcard fallback: an unsupported method is a programming error (a new codec or a
        // value the CLI failed to reject), not a silent downgrade to the wrong compression.
        // REQ-223: fail loudly with the supported values instead of silently writing Optimal.
        _ => throw new ArgumentOutOfRangeException(
            nameof(method),
            method,
            $"Unsupported compression method '{method}'. Supported values: store, deflate."),
    };

    private sealed record ZipProcessingContext(
        ZipArchive Archive,
        FileGenerationRequest Request,
        byte[] StandardTextContent,
        byte[] EmlTextContent,
        HashSet<string> UsedEntryPaths,
        DiskBackedFileDataList ProcessedFiles,
        Action<FileData>? OnItemCommitted,
        CompressionLevel CompressionLevel);

    private static void WriteFileToArchive(ZipArchive archive, FileData fileData, HashSet<string> usedEntryPaths, CompressionLevel compressionLevel)
    {
        var entry = CreateTrackedEntry(archive, fileData.WorkItem.FilePathInZip, usedEntryPaths, compressionLevel);
        using var entryStream = entry.Open();
        entryStream.Write(fileData.Data.Span);
    }

    /// <summary>
    /// Writes an Attachment to the Archive. Fails if the entry path already exists.
    /// </summary>
    private static void WriteAttachmentToArchive(ZipArchive archive, FileData fileData, HashSet<string> usedEntryPaths, CompressionLevel compressionLevel)
    {
        if (!fileData.Attachment.HasValue)
        {
            return;
        }

        var sanitizedFilename = Path.GetFileName(fileData.Attachment.Value.filename.Replace('\\', '/'));
        var rawEntryPath = $"{fileData.WorkItem.FolderPrefix}{fileData.WorkItem.Index}_{sanitizedFilename}";
        var attachmentEntry = CreateTrackedEntry(archive, rawEntryPath, usedEntryPaths, compressionLevel);
        using var attachmentStream = attachmentEntry.Open();
        attachmentStream.Write(fileData.Attachment.Value.content);
    }

    /// <summary>
    /// Writes the extracted text for an Attachment to the Archive. Fails if the entry path already exists.
    /// </summary>
    private static void WriteAttachmentTextToArchive(ZipArchive archive, FileData fileData, HashSet<string> usedEntryPaths, CompressionLevel compressionLevel)
    {
        if (!fileData.Attachment.HasValue)
        {
            return;
        }

        var sanitizedFilename = Path.GetFileName(fileData.Attachment.Value.filename.Replace('\\', '/'));
        var attachmentTextFileName = $"{Path.GetFileNameWithoutExtension(sanitizedFilename)}.txt";
        var rawEntryPath = $"{fileData.WorkItem.FolderPrefix}{fileData.WorkItem.Index}_{attachmentTextFileName}";
        var attachmentTextEntry = CreateTrackedEntry(archive, rawEntryPath, usedEntryPaths, compressionLevel);
        using var attachmentTextStream = attachmentTextEntry.Open();
        attachmentTextStream.Write(PlaceholderFiles.ExtractedText);
    }

    /// <summary>
    /// Writes an extracted text version of a Native File to the Archive. Fails if the entry path already exists.
    /// </summary>
    private static void WriteExtractedTextToArchive(ZipArchive archive, FileData fileData, FileGenerationRequest request, byte[] textContent, HashSet<string> usedEntryPaths, CompressionLevel compressionLevel)
    {
        System.Diagnostics.Debug.Assert(request.Output.WithText, "Should only be called when WithText is true");

        var textFileName = LoadFiles.TextPathHelper.GetTextPath(fileData.WorkItem.FileName);
        var rawEntryPath = $"{fileData.WorkItem.FolderPrefix}{textFileName}";
        var textEntry = CreateTrackedEntry(archive, rawEntryPath, usedEntryPaths, compressionLevel);
        using var textEntryStream = textEntry.Open();

        // O(1): write pre-computed byte[] directly, no string round-trip
        textEntryStream.Write(textContent);
    }

    private static ZipArchiveEntry CreateTrackedEntry(ZipArchive archive, string rawPath, HashSet<string> usedEntryPaths, CompressionLevel compressionLevel)
    {
        var entryPath = rawPath.Replace('\\', '/');
        if (!usedEntryPaths.Add(entryPath))
        {
            throw new InvalidOperationException($"Archive entry path collision: '{entryPath}' conflicts with an existing entry.");
        }

        return archive.CreateEntry(entryPath, compressionLevel);
    }
}
