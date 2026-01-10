using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zipper
{
    /// <summary>
    /// Handles load file generation operations for file metadata tracking
    /// Extracted from ParallelFileGenerator to follow single responsibility principle
    /// </summary>
    internal static class LoadFileGenerator
    {
        /// <summary>
        /// Writes load file content to a ZIP archive
        /// </summary>
        /// <param name="archive">The ZIP archive to write to</param>
        /// <param name="loadFileName">Name of the load file in the archive</param>
        /// <param name="request">File generation request parameters</param>
        /// <param name="processedFiles">List of processed file data</param>
        /// <returns>Task representing the load file writing operation</returns>
        public static async Task WriteLoadFileToArchiveAsync(
            ZipArchive archive,
            string loadFileName,
            FileGenerationRequest request,
            List<FileData> processedFiles)
        {
            var loadFileEntry = archive.CreateEntry(loadFileName, CompressionLevel.Optimal);
            using var loadFileStream = loadFileEntry.Open();
            using var writer = new StreamWriter(loadFileStream, GetEncoding(request.Encoding));

            await WriteLoadFileContent(writer, request, processedFiles);
        }

        /// <summary>
        /// Writes load file content to disk
        /// </summary>
        /// <param name="loadFilePath">Path where the load file should be saved</param>
        /// <param name="request">File generation request parameters</param>
        /// <param name="processedFiles">List of processed file data</param>
        /// <returns>Task representing the load file writing operation</returns>
        public static async Task WriteLoadFileToDiskAsync(
            string loadFilePath,
            FileGenerationRequest request,
            List<FileData> processedFiles)
        {
            using var fileStream = new FileStream(loadFilePath, FileMode.Create);
            using var writer = new StreamWriter(fileStream, GetEncoding(request.Encoding));

            await WriteLoadFileContent(writer, request, processedFiles);
        }

        /// <summary>
        /// Writes the load file content with appropriate headers and data
        /// </summary>
        /// <param name="writer">Stream writer to output to</param>
        /// <param name="request">File generation request parameters</param>
        /// <param name="processedFiles">List of processed file data</param>
        /// <returns>Task representing the content writing operation</returns>
        public static async Task WriteLoadFileContent(
            StreamWriter writer,
            FileGenerationRequest request,
            List<FileData> processedFiles)
        {
            const char colDelim = (char)20;
            const char quote = (char)254;

            // Build header based on options
            var headerBuilder = new StringBuilder();
            headerBuilder.Append($"{quote}Control Number{quote}{colDelim}{quote}File Path{quote}");

            if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
            {
                headerBuilder.Append($"{colDelim}{quote}Custodian{quote}{colDelim}{quote}Date Sent{quote}{colDelim}{quote}Author{quote}{colDelim}{quote}File Size{quote}");
            }

            if (request.FileType.ToLowerInvariant() == "eml")
            {
                headerBuilder.Append($"{colDelim}{quote}To{quote}{colDelim}{quote}From{quote}{colDelim}{quote}Subject{quote}{colDelim}{quote}Sent Date{quote}{colDelim}{quote}Attachment{quote}");
            }

            // Add Bates Number column if configured
            if (request.BatesConfig != null)
            {
                headerBuilder.Append($"{colDelim}{quote}Bates Number{quote}");
            }

            // Add Page Count column for TIFF with page range
            if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
            {
                headerBuilder.Append($"{colDelim}{quote}Page Count{quote}");
            }

            if (request.WithText)
            {
                headerBuilder.Append($"{colDelim}{quote}Extracted Text{quote}");
            }

            await writer.WriteLineAsync(headerBuilder.ToString());

            // Write file records
            foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
            {
                await WriteFileRecord(writer, fileData, request, colDelim, quote);
            }
        }

        /// <summary>
        /// Writes a single file record to the load file
        /// </summary>
        private static async Task WriteFileRecord(
            StreamWriter writer,
            FileData fileData,
            FileGenerationRequest request,
            char colDelim,
            char quote)
        {
            var workItem = fileData.WorkItem;
            var docId = $"DOC{workItem.Index:D8}";

            var lineBuilder = new StringBuilder();
            lineBuilder.Append($"{quote}{docId}{quote}{colDelim}{quote}{workItem.FilePathInZip}{quote}");

            // Add metadata columns if requested
            if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
            {
                var metadataColumns = GetMetadataColumns(workItem, fileData);
                lineBuilder.Append(metadataColumns);
            }

            // Add EML-specific columns if needed
            if (request.FileType.ToLowerInvariant() == "eml")
            {
                var emlColumns = GetEmlColumns(workItem, fileData);
                lineBuilder.Append(emlColumns);
            }

            // Add Bates Number column if configured
            if (request.BatesConfig != null)
            {
                var batesNumber = BatesNumberGenerator.Generate(request.BatesConfig, workItem.Index - 1);
                lineBuilder.Append($"{colDelim}{quote}{batesNumber}{quote}");
            }

            // Add Page Count column for TIFF with page range
            if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
            {
                lineBuilder.Append($"{colDelim}{quote}{fileData.PageCount}{quote}");
            }

            // Add extracted text column if requested
            if (request.WithText)
            {
                var textFilePath = workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
                lineBuilder.Append($"{colDelim}{quote}{textFilePath}{quote}");
            }

            await writer.WriteLineAsync(lineBuilder.ToString());
        }

        /// <summary>
        /// Gets metadata columns for file records
        /// </summary>
        private static string GetMetadataColumns(FileWorkItem workItem, FileData fileData)
        {
            const char colDelim = (char)20;
            const char quote = (char)254;

            var custodian = $"Custodian {workItem.FolderNumber}";
            var dateSent = DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd");
            var author = $"Author {Random.Shared.Next(1, 100):D3}";
            var fileSize = fileData.Data.Length;

            return $"{colDelim}{quote}{custodian}{quote}{colDelim}{quote}{dateSent}{quote}{colDelim}{quote}{author}{quote}{colDelim}{quote}{fileSize}{quote}";
        }

        /// <summary>
        /// Gets EML-specific columns for email file records
        /// </summary>
        private static string GetEmlColumns(FileWorkItem workItem, FileData fileData)
        {
            const char colDelim = (char)20;
            const char quote = (char)254;

            var to = $"recipient{workItem.Index}@example.com";
            var from = $"sender{workItem.Index}@example.com";
            var subject = $"Email Subject {workItem.Index}";
            var sentDate = DateTime.Now.AddDays(-Random.Shared.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
            var attachmentName = fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : "";

            return $"{colDelim}{quote}{to}{quote}{colDelim}{quote}{from}{quote}{colDelim}{quote}{subject}{quote}{colDelim}{quote}{sentDate}{quote}{colDelim}{quote}{attachmentName}{quote}";
        }

        /// <summary>
        /// Gets appropriate text encoding for the specified encoding name
        /// </summary>
        /// <param name="encodingName">Name of the encoding</param>
        /// <returns>Encoding instance</returns>
        public static Encoding GetEncoding(string encodingName) => EncodingHelper.GetEncodingOrDefault(encodingName);
    }
}