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

                // Write main file
                await WriteFileToArchiveAsync(archive, fileData);

                // Write attachment if it exists (for EML)
                if (fileData.Attachment.HasValue)
                {
                    await WriteAttachmentToArchiveAsync(archive, fileData, request);
                }

                // Write extracted text file if requested
                if (request.WithText)
                {
                    await WriteExtractedTextToArchiveAsync(archive, fileData, request);
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
        /// Writes a single file to the ZIP archive
        /// </summary>
        private static async Task WriteFileToArchiveAsync(ZipArchive archive, FileData fileData)
        {
            var entry = archive.CreateEntry(fileData.WorkItem.FilePathInZip, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            await entryStream.WriteAsync(fileData.Data);
        }

        /// <summary>
        /// Writes an attachment file to the ZIP archive (for EML files)
        /// </summary>
        private static async Task WriteAttachmentToArchiveAsync(ZipArchive archive, FileData fileData, FileGenerationRequest request)
        {
            if (!fileData.Attachment.HasValue) return;

            var attachmentEntry = archive.CreateEntry(
                $"{fileData.WorkItem.FolderName}/{fileData.Attachment.Value.filename}",
                CompressionLevel.Optimal);

            using var attachmentStream = attachmentEntry.Open();
            await attachmentStream.WriteAsync(fileData.Attachment.Value.content);

            // Write extracted text for attachment if requested
            if (request.WithText)
            {
                var attachmentTextFileName = $"{Path.GetFileNameWithoutExtension(fileData.Attachment.Value.filename)}.txt";
                var attachmentTextEntry = archive.CreateEntry(
                    $"{fileData.WorkItem.FolderName}/{attachmentTextFileName}",
                    CompressionLevel.Optimal);

                using var attachmentTextStream = attachmentTextEntry.Open();
                await attachmentTextStream.WriteAsync(PlaceholderFiles.ExtractedText);
            }
        }

        /// <summary>
        /// Writes an extracted text version of a file to the ZIP archive
        /// </summary>
        private static async Task WriteExtractedTextToArchiveAsync(ZipArchive archive, FileData fileData, FileGenerationRequest request)
        {
            if (!request.WithText) return;

            var textFileName = fileData.WorkItem.FileName.Replace($".{request.FileType}", ".txt");
            var textFilePathInZip = $"{fileData.WorkItem.FolderName}/{textFileName}";

            var textEntry = archive.CreateEntry(textFilePathInZip, CompressionLevel.Optimal);
            using var textEntryStream = textEntry.Open();

            var textContent = GetExtractedTextContent(request.FileType);
            await textEntryStream.WriteAsync(Encoding.UTF8.GetBytes(textContent));
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