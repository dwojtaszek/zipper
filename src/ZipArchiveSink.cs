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
    /// Creates an Archive containing the generated Native Files and optionally a Load File.
    /// </summary>
    /// <param name="zipFilePath">Path where the Archive should be created.</param>
    /// <param name="loadFileName">Name of the Load File (if included).</param>
    /// <param name="loadFilePath">Path where Load File should be saved separately (if not included in Archive).</param>
    /// <param name="request">File generation request parameters.</param>
    /// <param name="fileDataReader">Channel reader for receiving generated file data.</param>
    /// <returns>The actual Load File path that was created (or original if included in Archive).</returns>
    public async Task<string> CreateArchiveAsync(
        string zipFilePath,
        string loadFileName,
        string loadFilePath,
        FileGenerationRequest request,
        ChannelReader<FileData> fileDataReader,
        CancellationToken cancellationToken = default)
    {
        using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
        using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);

        using var processedFiles = new DiskBackedFileDataList();
        var usedEntryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Cached per-type text payloads; the per-record File Type selects which one is written.
        var standardTextContent = request.Output.WithText ? PlaceholderFiles.ExtractedText : null;
        var emlTextContent = request.Output.WithText ? PlaceholderFiles.EmlExtractedText : null;

        var outOfOrderBuffer = new Dictionary<long, FileData>();

        try
        {
            await DrainReaderAndOrderFilesAsync(archive, fileDataReader, request, standardTextContent, emlTextContent, usedEntryPaths, processedFiles, outOfOrderBuffer, cancellationToken).ConfigureAwait(false);
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
        Func<string, Stream> openTarget = request.Output.IncludeLoadFile
            ? target => archive.CreateEntry(target, CompressionLevel.Optimal).Open()
            : target => new FileStream(Path.Combine(baseFilePath, target), FileMode.Create);

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
            cancellationToken).ConfigureAwait(false);

        return request.Output.IncludeLoadFile
            ? actualLoadFileTarget
            : Path.Combine(baseFilePath, actualLoadFileTarget);
    }

    private async Task DrainReaderAndOrderFilesAsync(
        ZipArchive archive,
        ChannelReader<FileData> fileDataReader,
        FileGenerationRequest request,
        byte[]? standardTextContent,
        byte[]? emlTextContent,
        HashSet<string> usedEntryPaths,
        DiskBackedFileDataList processedFiles,
        Dictionary<long, FileData> outOfOrderBuffer,
        CancellationToken cancellationToken)
    {
        long nextExpectedIndex = 1;

        await foreach (var incomingFileData in fileDataReader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (incomingFileData.WorkItem.Index == nextExpectedIndex)
            {
                ProcessFileData(archive, incomingFileData, request, standardTextContent, emlTextContent, usedEntryPaths, processedFiles);
                nextExpectedIndex++;

                while (outOfOrderBuffer.Remove(nextExpectedIndex, out var buffered))
                {
                    ProcessFileData(archive, buffered, request, standardTextContent, emlTextContent, usedEntryPaths, processedFiles);
                    nextExpectedIndex++;
                }
            }
            else
            {
                outOfOrderBuffer[incomingFileData.WorkItem.Index] = incomingFileData;
            }
        }
    }

    private void CleanupOperations(Dictionary<long, FileData> outOfOrderBuffer, ChannelReader<FileData> fileDataReader)
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

    private static void ProcessFileData(ZipArchive archive, FileData fileData, FileGenerationRequest request, byte[]? standardTextContent, byte[]? emlTextContent, HashSet<string> usedEntryPaths, DiskBackedFileDataList processedFiles)
    {
        try
        {
            processedFiles.Add(fileData);

            WriteFileToArchive(archive, fileData, usedEntryPaths);

            if (request.Output.WithText)
            {
                var textContent = string.Equals(fileData.WorkItem.EffectiveFileType(request), "eml", StringComparison.Ordinal)
                    ? emlTextContent!
                    : standardTextContent!;
                WriteExtractedTextToArchive(archive, fileData, request, textContent, usedEntryPaths);
            }

            if (fileData.Attachment.HasValue)
            {
                WriteAttachmentToArchive(archive, fileData, usedEntryPaths);
            }

            if (fileData.Attachment.HasValue && request.Output.WithText)
            {
                WriteAttachmentTextToArchive(archive, fileData, usedEntryPaths);
            }
        }
        finally
        {
            fileData.MemoryOwner?.Dispose();
        }
    }

    private static void WriteFileToArchive(ZipArchive archive, FileData fileData, HashSet<string> usedEntryPaths)
    {
        var entryPath = fileData.WorkItem.FilePathInZip.Replace('\\', '/');
        if (!usedEntryPaths.Add(entryPath))
        {
            return;
        }

        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(fileData.Data.Span);
    }

    /// <summary>
    /// Writes an Attachment to the Archive. Skips if the entry path already exists.
    /// </summary>
    private static void WriteAttachmentToArchive(ZipArchive archive, FileData fileData, HashSet<string> usedEntryPaths)
    {
        if (!fileData.Attachment.HasValue)
        {
            return;
        }

        var sanitizedFilename = Path.GetFileName(fileData.Attachment.Value.filename.Replace('\\', '/'));
        var entryPath = $"{fileData.WorkItem.FolderPrefix}{fileData.WorkItem.Index}_{sanitizedFilename}".Replace('\\', '/');
        if (!usedEntryPaths.Add(entryPath))
        {
            return;
        }

        var attachmentEntry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var attachmentStream = attachmentEntry.Open();
        attachmentStream.Write(fileData.Attachment.Value.content);
    }

    /// <summary>
    /// Writes the extracted text for an Attachment to the Archive. Skips if the entry path already exists.
    /// </summary>
    private static void WriteAttachmentTextToArchive(ZipArchive archive, FileData fileData, HashSet<string> usedEntryPaths)
    {
        if (!fileData.Attachment.HasValue)
        {
            return;
        }

        var sanitizedFilename = Path.GetFileName(fileData.Attachment.Value.filename.Replace('\\', '/'));
        var attachmentTextFileName = $"{Path.GetFileNameWithoutExtension(sanitizedFilename)}.txt";
        var entryPath = $"{fileData.WorkItem.FolderPrefix}{fileData.WorkItem.Index}_{attachmentTextFileName}".Replace('\\', '/');
        if (!usedEntryPaths.Add(entryPath))
        {
            return;
        }

        var attachmentTextEntry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var attachmentTextStream = attachmentTextEntry.Open();
        attachmentTextStream.Write(PlaceholderFiles.ExtractedText);
    }

    /// <summary>
    /// Writes an extracted text version of a Native File to the Archive. Skips if the entry path already exists.
    /// </summary>
    private static void WriteExtractedTextToArchive(ZipArchive archive, FileData fileData, FileGenerationRequest request, byte[] textContent, HashSet<string> usedEntryPaths)
    {
        System.Diagnostics.Debug.Assert(request.Output.WithText, "Should only be called when WithText is true");

        var textFileName = LoadFiles.TextPathHelper.GetTextPath(fileData.WorkItem.FileName);
        var entryPath = $"{fileData.WorkItem.FolderPrefix}{textFileName}".Replace('\\', '/');

        if (!usedEntryPaths.Add(entryPath))
        {
            return;
        }

        var textEntry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var textEntryStream = textEntry.Open();

        // O(1): write pre-computed byte[] directly, no string round-trip
        textEntryStream.Write(textContent);
    }
}
