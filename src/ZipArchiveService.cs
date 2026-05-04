using System.IO.Compression;
using System.Threading.Channels;
using Zipper.LoadFiles;

namespace Zipper
{
    /// <summary>
    /// Handles ZIP archive creation and file writing operations
    /// Extracted from ParallelFileGenerator to follow single responsibility principle.
    /// </summary>
    internal static class ZipArchiveService
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
        public static async Task<string> CreateArchiveAsync(
            string zipFilePath,
            string loadFileName,
            string loadFilePath,
            FileGenerationRequest request,
            ChannelReader<FileData> fileDataReader)
        {
            using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);

            var processedFiles = new List<FileData>();
            var usedEntryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Pre-compute the extracted text content selection once, outside the loop
            var extractedTextContent = request.Output.WithText
                ? (string.Equals(request.Output.FileType, "eml", StringComparison.OrdinalIgnoreCase)
                    ? PlaceholderFiles.EmlExtractedText
                    : PlaceholderFiles.ExtractedText)
                : null;

            // Process generated files and write to archive
            await foreach (var fileData in fileDataReader.ReadAllAsync())
            {
                processedFiles.Add(fileData);

                // Sequentially create ZIP entries to avoid conflicts.
                // The order is:
                // 1. Main file (e.g., .eml)
                // 2. Main file's extracted text (if requested)
                // 3. Attachment (if it exists)
                // 4. Attachment's extracted text (if it exists and text is requested)
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

                // Dispose memory owner immediately after writing to archive to bound memory usage.
                // The FileData record remains in processedFiles for load file generation,
                // but the large byte arrays are released since they've been written to the ZIP.
                fileData.MemoryOwner?.Dispose();
            }

            var formatsToGenerate = request.LoadFile.LoadFileFormats?.Any() == true
                ? request.LoadFile.LoadFileFormats
                : new List<LoadFileFormat> { request.LoadFile.LoadFileFormat };

            string actualLoadFilePath = loadFilePath;
            var baseFileName = Path.GetFileNameWithoutExtension(loadFileName);
            var baseFilePath = Path.GetDirectoryName(loadFilePath) ?? string.Empty;

            foreach (var format in formatsToGenerate)
            {
                var loadFileWriter = LoadFileWriterFactory.CreateWriter(format);
                var actualLoadFileName = baseFileName + loadFileWriter.FileExtension;

                if (request.Output.IncludeLoadFile)
                {
                    var loadFileEntry = archive.CreateEntry(actualLoadFileName, CompressionLevel.Optimal);
                    using var loadFileStream = loadFileEntry.Open();
                    await loadFileWriter.WriteAsync(loadFileStream, request, processedFiles, null);

                    // Return path within the ZIP archive when load file is included
                    actualLoadFilePath = actualLoadFileName;
                }
                else
                {
                    var currentFilePath = Path.Combine(baseFilePath, actualLoadFileName);
                    await using var fileStream = new FileStream(currentFilePath, FileMode.Create);
                    await loadFileWriter.WriteAsync(fileStream, request, processedFiles, null);
                    await fileStream.FlushAsync();

                    actualLoadFilePath = currentFilePath;
                }
            }

            return actualLoadFilePath;
        }

        /// <summary>
        /// Writes a single file to the ZIP archive. Skips if the entry path already exists.
        /// </summary>
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

            var textFileName = fileData.WorkItem.FileName.Replace($".{request.Output.FileType}", ".txt");
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
}
