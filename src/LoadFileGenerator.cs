using System.Text;

namespace Zipper
{
    /// <summary>
    /// Handles load file generation operations for file metadata tracking
    /// Extracted from ParallelFileGenerator to follow single responsibility principle.
    /// </summary>
    internal static class LoadFileGenerator
    {
        /// <summary>
        /// Writes the load file content with appropriate headers and data.
        /// </summary>
        /// <param name="writer">Stream writer to output to.</param>
        /// <param name="request">File generation request parameters.</param>
        /// <param name="processedFiles">List of processed file data.</param>
        /// <returns>Task representing the content writing operation.</returns>
        public static async Task WriteLoadFileContent(
            StreamWriter writer,
            FileGenerationRequest request,
            List<FileData> processedFiles)
        {
            // Defensive guards to prevent IndexOutOfRangeException
            char colDelim = !string.IsNullOrEmpty(request.ColumnDelimiter) ? request.ColumnDelimiter[0] : '\u0014';
            char quote = !string.IsNullOrEmpty(request.QuoteDelimiter) ? request.QuoteDelimiter[0] : '\u00fe';

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
        /// Writes a single file record to the load file.
        /// </summary>
        private static async Task WriteFileRecord(
            StreamWriter writer,
            FileData fileData,
            FileGenerationRequest request,
            char colDelim,
            char quote)
        {
            var workItem = fileData.WorkItem;
            var docId = SanitizeField($"DOC{workItem.Index:D8}", request.NewlineDelimiter);

            var lineBuilder = new StringBuilder();
            lineBuilder.Append($"{quote}{docId}{quote}{colDelim}{quote}{SanitizeField(workItem.FilePathInZip, request.NewlineDelimiter)}{quote}");

            // Add metadata columns if requested
            if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
            {
                var metadataColumns = GetMetadataColumns(workItem, fileData, request, colDelim, quote);
                lineBuilder.Append(metadataColumns);
            }

            // Add EML-specific columns if needed
            if (request.FileType.ToLowerInvariant() == "eml")
            {
                var emlColumns = GetEmlColumns(workItem, fileData, request, colDelim, quote);
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
        /// Gets metadata columns for file records.
        /// </summary>
        private static string GetMetadataColumns(FileWorkItem workItem, FileData fileData, FileGenerationRequest request, char colDelim, char quote)
        {
            var custodian = SanitizeField($"Custodian {workItem.FolderNumber}", request.NewlineDelimiter);
            var dateSent = DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd");
            var author = SanitizeField($"Author {Random.Shared.Next(1, 100):D3}", request.NewlineDelimiter);
            var fileSize = fileData.Data.Length;

            return $"{colDelim}{quote}{custodian}{quote}{colDelim}{quote}{dateSent}{quote}{colDelim}{quote}{author}{quote}{colDelim}{quote}{fileSize}{quote}";
        }

        /// <summary>
        /// Gets EML-specific columns for email file records.
        /// </summary>
        private static string GetEmlColumns(FileWorkItem workItem, FileData fileData, FileGenerationRequest request, char colDelim, char quote)
        {
            var to = SanitizeField($"recipient{workItem.Index}@example.com", request.NewlineDelimiter);
            var from = SanitizeField($"sender{workItem.Index}@example.com", request.NewlineDelimiter);
            var subject = SanitizeField($"Email Subject {workItem.Index}", request.NewlineDelimiter);
            var sentDate = DateTime.Now.AddDays(-Random.Shared.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
            var attachmentName = SanitizeField(fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : string.Empty, request.NewlineDelimiter);

            return $"{colDelim}{quote}{to}{quote}{colDelim}{quote}{from}{quote}{colDelim}{quote}{subject}{quote}{colDelim}{quote}{sentDate}{quote}{colDelim}{quote}{attachmentName}{quote}";
        }

        /// <summary>
        /// Sanitizes a field value by replacing newline characters with the configured delimiter.
        /// </summary>
        /// <param name="value">The field value to sanitize.</param>
        /// <param name="newlineDelimiter">The newline replacement character.</param>
        /// <returns>Sanitized field value.</returns>
        private static string SanitizeField(string value, string newlineDelimiter)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Replace("\r\n", newlineDelimiter)
                        .Replace("\n", newlineDelimiter)
                        .Replace("\r", newlineDelimiter);
        }

        /// <summary>
        /// Gets appropriate text encoding for the specified encoding name.
        /// </summary>
        /// <param name="encodingName">Name of the encoding.</param>
        /// <returns>Encoding instance.</returns>
        public static Encoding GetEncoding(string encodingName) => EncodingHelper.GetEncodingOrDefault(encodingName);
    }
}
