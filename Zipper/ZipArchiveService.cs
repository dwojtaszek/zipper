using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Zipper
{
    /// <summary>
    /// Handles ZIP archive creation and file writing operations
    /// Extracted from ParallelFileGenerator to follow single responsibility principle
    /// </summary>
    internal static class ZipArchiveService
    {
        /// <summary>
        /// Creates a ZIP archive containing the generated files and optionally a load file
        /// </summary>
        /// <param name="zipFilePath">Path where the ZIP file should be created</param>
        /// <param name="loadFileName">Name of the load file (if included)</param>
        /// <param name="loadFilePath">Path where load file should be saved separately (if not included in ZIP)</param>
        /// <param name="request">File generation request parameters</param>
        /// <param name="fileDataReader">Channel reader for receiving generated file data</param>
        /// <returns>Task representing the archive creation operation</returns>
        public static async Task CreateArchiveAsync(
            string zipFilePath,
            string loadFileName,
            string loadFilePath,
            FileGenerationRequest request,
            ChannelReader<FileData> fileDataReader)
        {
            using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);

            var processedFiles = new ConcurrentBag<FileData>();

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
                WriteFileToArchive(archive, fileData);

                if (request.WithText)
                {
                    WriteExtractedTextToArchive(archive, fileData, request);
                }

                if (fileData.Attachment.HasValue)
                {
                    WriteAttachmentToArchive(archive, fileData);
                }

                if (fileData.Attachment.HasValue && request.WithText)
                {
                    WriteAttachmentTextToArchive(archive, fileData);
                }

                fileData.MemoryOwner?.Dispose();
            }

            // Write load file
            if (request.IncludeLoadFile)
            {
                await LoadFileGenerator.WriteLoadFileToArchiveAsync(archive, loadFileName, request, processedFiles.ToList());
            }
            else
            {
                await LoadFileGenerator.WriteLoadFileToDiskAsync(loadFilePath, request, processedFiles.ToList());
            }
        }

        /// <summary>
        /// Writes a single file to the ZIP archive (synchronous version)
        /// </summary>
        private static void WriteFileToArchive(ZipArchive archive, FileData fileData)
        {
            var entry = archive.CreateEntry(fileData.WorkItem.FilePathInZip, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            entryStream.Write(fileData.Data);
        }

        /// <summary>
        /// Writes an attachment file to the ZIP archive (synchronous version)
        /// </summary>
        private static void WriteAttachmentToArchive(ZipArchive archive, FileData fileData)
        {
            if (!fileData.Attachment.HasValue) return;

            var attachmentEntry = archive.CreateEntry(
                $"{fileData.WorkItem.FolderName}/{fileData.Attachment.Value.filename}",
                CompressionLevel.Optimal);

            using var attachmentStream = attachmentEntry.Open();
            attachmentStream.Write(fileData.Attachment.Value.content);
        }

        /// <summary>
        /// Writes the extracted text for an attachment to the ZIP archive.
        /// </summary>
        private static void WriteAttachmentTextToArchive(ZipArchive archive, FileData fileData)
        {
            if (!fileData.Attachment.HasValue) return;

            var attachmentTextFileName = $"{Path.GetFileNameWithoutExtension(fileData.Attachment.Value.filename)}.txt";
            var attachmentTextEntry = archive.CreateEntry(
                $"{fileData.WorkItem.FolderName}/{attachmentTextFileName}",
                CompressionLevel.Optimal);

            using var attachmentTextStream = attachmentTextEntry.Open();
            attachmentTextStream.Write(PlaceholderFiles.ExtractedText);
        }

        /// <summary>
        /// Writes an extracted text version of a file to the ZIP archive (synchronous version)
        /// </summary>
        private static void WriteExtractedTextToArchive(ZipArchive archive, FileData fileData, FileGenerationRequest request)
        {
            if (!request.WithText) return;

            var textFileName = fileData.WorkItem.FileName.Replace($".{request.FileType}", ".txt");
            var textFilePathInZip = $"{fileData.WorkItem.FolderName}/{textFileName}";

            var textEntry = archive.CreateEntry(textFilePathInZip, CompressionLevel.Optimal);
            using var textEntryStream = textEntry.Open();

            var textContent = GetExtractedTextContent(request.FileType);
            textEntryStream.Write(Encoding.UTF8.GetBytes(textContent));
        }

        /// <summary>
        /// Gets the appropriate extracted text content for different file types
        /// </summary>
        private static string GetExtractedTextContent(string fileType)
        {
            return fileType.ToLowerInvariant() switch
            {
                "pdf" => System.Text.Encoding.UTF8.GetString(PlaceholderFiles.ExtractedText),
                "jpg" or "jpeg" => System.Text.Encoding.UTF8.GetString(PlaceholderFiles.ExtractedText),
                "tiff" => System.Text.Encoding.UTF8.GetString(PlaceholderFiles.ExtractedText),
                "eml" => System.Text.Encoding.UTF8.GetString(PlaceholderFiles.EmlExtractedText),
                _ => System.Text.Encoding.UTF8.GetString(PlaceholderFiles.ExtractedText)
            };
        }
    }
}
