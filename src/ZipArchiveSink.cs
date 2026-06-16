using System.IO.Compression;
using System.Threading.Channels;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Handles ZIP archive creation and file writing operations
/// Extracted from ParallelFileGenerator to follow single responsibility principle.
/// </summary>
internal class ZipArchiveSink : IArchiveSink
{
    /// <summary>
    /// Creates a ZIP archive containing the generated files and optionally a load file.
    /// </summary>
    /// <param name="zipFilePath">Path where the ZIP file should be created.</param>
    /// <param name="loadFileName">Name of the load file (if included).</param>
    /// <param name="loadFilePath">Path where load file should be saved separately (if not included in ZIP).</param>
    /// <param name="request">File generation request parameters.</param>
    /// <param name="fileDataReader">Channel reader for receiving generated file data.</param>
    /// <returns>The actual load file path that was created (or original if included in ZIP).</returns>
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

        var extractedTextContent = request.Output.WithText
            ? (string.Equals(request.Output.FileType, "eml", StringComparison.OrdinalIgnoreCase)
                ? PlaceholderFiles.EmlExtractedText
                : PlaceholderFiles.ExtractedText)
            : null;

        var outOfOrderBuffer = new Dictionary<long, FileData>();

        try
        {
            await DrainReaderAndOrderFilesAsync(archive, fileDataReader, request, extractedTextContent, usedEntryPaths, processedFiles, outOfOrderBuffer, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CleanupOperations(outOfOrderBuffer, fileDataReader);
        }

        var formatsToGenerate = request.LoadFile.LoadFileFormats?.Count > 0
            ? request.LoadFile.LoadFileFormats
            : new List<LoadFileFormat> { request.LoadFile.LoadFileFormat };

        string actualLoadFilePath = loadFilePath;
        var baseFileName = Path.GetFileNameWithoutExtension(loadFileName);
        var baseFilePath = Path.GetDirectoryName(loadFilePath) ?? string.Empty;

        foreach (var format in formatsToGenerate)
        {
            actualLoadFilePath = await GenerateLoadFileAndAuditAsync(
                archive, request, processedFiles, format, baseFileName, baseFilePath, loadFileName, actualLoadFilePath).ConfigureAwait(false);
        }

        return actualLoadFilePath;
    }

    private async Task DrainReaderAndOrderFilesAsync(
        ZipArchive archive,
        ChannelReader<FileData> fileDataReader,
        FileGenerationRequest request,
        byte[]? extractedTextContent,
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
                ProcessFileData(archive, incomingFileData, request, extractedTextContent, usedEntryPaths, processedFiles);
                nextExpectedIndex++;

                while (outOfOrderBuffer.Remove(nextExpectedIndex, out var buffered))
                {
                    ProcessFileData(archive, buffered, request, extractedTextContent, usedEntryPaths, processedFiles);
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

    private async Task<string> GenerateLoadFileAndAuditAsync(
        ZipArchive archive,
        FileGenerationRequest request,
        DiskBackedFileDataList processedFiles,
        LoadFileFormat format,
        string baseFileName,
        string baseFilePath,
        string loadFileName,
        string actualLoadFilePath)
    {
        var loadFileWriter = LoadFileWriterFactory.CreateWriter(format);
        var actualLoadFileName = baseFileName + loadFileWriter.FileExtension;

        var auditContext = LoadfileAuditWriter.CreateContext(request, processedFiles, format);
        var chaosEngine = auditContext.ChaosEngine;

        if (request.Output.IncludeLoadFile)
        {
            await GenerateLoadFileToArchiveAsync(archive, request, processedFiles, loadFileWriter, chaosEngine, actualLoadFileName).ConfigureAwait(false);
            await EmitAuditToArchiveAsync(archive, request, auditContext, actualLoadFileName).ConfigureAwait(false);
            return actualLoadFileName;
        }
        else
        {
            var currentFilePath = Path.Combine(baseFilePath, actualLoadFileName);
            await GenerateLoadFileToDiskAsync(request, processedFiles, loadFileWriter, chaosEngine, currentFilePath).ConfigureAwait(false);
            await EmitAuditToDiskAsync(request, auditContext, currentFilePath).ConfigureAwait(false);
            return currentFilePath;
        }
    }

    private async Task GenerateLoadFileToArchiveAsync(
        ZipArchive archive,
        FileGenerationRequest request,
        DiskBackedFileDataList processedFiles,
        ILoadFileWriter loadFileWriter,
        ChaosEngine? chaosEngine,
        string actualLoadFileName)
    {
        var loadFileEntry = archive.CreateEntry(actualLoadFileName, CompressionLevel.Optimal);
        using var loadFileStream = loadFileEntry.Open();
        await loadFileWriter.WriteAsync(loadFileStream, request, processedFiles, chaosEngine).ConfigureAwait(false);
    }

    private async Task GenerateLoadFileToDiskAsync(
        FileGenerationRequest request,
        DiskBackedFileDataList processedFiles,
        ILoadFileWriter loadFileWriter,
        ChaosEngine? chaosEngine,
        string currentFilePath)
    {
        var fileStream = new FileStream(currentFilePath, FileMode.Create);
        await using (fileStream.ConfigureAwait(false))
        {
            await loadFileWriter.WriteAsync(fileStream, request, processedFiles, chaosEngine).ConfigureAwait(false);
            await fileStream.FlushAsync().ConfigureAwait(false);
        }
    }

    private async Task EmitAuditToArchiveAsync(
        ZipArchive archive,
        FileGenerationRequest request,
        LoadfileAuditWriter.AuditContext context,
        string actualLoadFileName)
    {
        var auditJson = LoadfileAuditWriter.GenerateAuditJson(actualLoadFileName, request, context);
        var propertiesEntry = archive.CreateEntry(actualLoadFileName + "_properties.json", CompressionLevel.Optimal);
        using var propertiesStream = propertiesEntry.Open();
        using var propertiesWriter = new StreamWriter(propertiesStream);
        await propertiesWriter.WriteAsync(auditJson).ConfigureAwait(false);
    }

    private async Task EmitAuditToDiskAsync(
        FileGenerationRequest request,
        LoadfileAuditWriter.AuditContext context,
        string currentFilePath)
    {
        var auditJson = LoadfileAuditWriter.GenerateAuditJson(currentFilePath, request, context);
        await File.WriteAllTextAsync(currentFilePath + "_properties.json", auditJson).ConfigureAwait(false);
    }

    private static void ProcessFileData(ZipArchive archive, FileData fileData, FileGenerationRequest request, byte[]? extractedTextContent, HashSet<string> usedEntryPaths, DiskBackedFileDataList processedFiles)
    {
        try
        {
            processedFiles.Add(fileData);

            WriteFileToArchive(archive, fileData, usedEntryPaths);

            if (request.Output.WithText)
            {
                WriteExtractedTextToArchive(archive, fileData, request, extractedTextContent!, usedEntryPaths);
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
        var entryPath = fileData.WorkItem.FilePathInZip;
        if (!usedEntryPaths.Add(entryPath))
        {
            return;
        }

        var entry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var entryStream = entry.Open();
        entryStream.Write(fileData.Data.Span);
    }

    /// <summary>
    /// Writes an attachment file to the ZIP archive. Skips if the entry path already exists.
    /// </summary>
    private static void WriteAttachmentToArchive(ZipArchive archive, FileData fileData, HashSet<string> usedEntryPaths)
    {
        if (!fileData.Attachment.HasValue)
        {
            return;
        }

        var entryPath = $"{fileData.WorkItem.FolderName}/{fileData.WorkItem.Index}_{fileData.Attachment.Value.filename}";
        if (!usedEntryPaths.Add(entryPath))
        {
            return;
        }

        var attachmentEntry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var attachmentStream = attachmentEntry.Open();
        attachmentStream.Write(fileData.Attachment.Value.content);
    }

    /// <summary>
    /// Writes the extracted text for an attachment to the ZIP archive. Skips if the entry path already exists.
    /// </summary>
    private static void WriteAttachmentTextToArchive(ZipArchive archive, FileData fileData, HashSet<string> usedEntryPaths)
    {
        if (!fileData.Attachment.HasValue)
        {
            return;
        }

        var attachmentTextFileName = $"{Path.GetFileNameWithoutExtension(fileData.Attachment.Value.filename)}.txt";
        var entryPath = $"{fileData.WorkItem.FolderName}/{fileData.WorkItem.Index}_{attachmentTextFileName}";
        if (!usedEntryPaths.Add(entryPath))
        {
            return;
        }

        var attachmentTextEntry = archive.CreateEntry(entryPath, CompressionLevel.Optimal);
        using var attachmentTextStream = attachmentTextEntry.Open();
        attachmentTextStream.Write(PlaceholderFiles.ExtractedText);
    }

    /// <summary>
    /// Writes an extracted text version of a file to the ZIP archive. Skips if the entry path already exists.
    /// </summary>
    private static void WriteExtractedTextToArchive(ZipArchive archive, FileData fileData, FileGenerationRequest request, byte[] textContent, HashSet<string> usedEntryPaths)
    {
        System.Diagnostics.Debug.Assert(request.Output.WithText, "Should only be called when WithText is true");

        var textFileName = fileData.WorkItem.FileName.Replace($".{request.Output.FileType}", ".txt", StringComparison.Ordinal);
        var entryPath = $"{fileData.WorkItem.FolderName}/{textFileName}";

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
