using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes DAT (Concordance) load files.
/// Supports Standard, Loadfile-Only, and Production Set output modes.
/// </summary>
internal class DatWriter : LoadFileWriterBase
{
    private readonly WriterMode mode;

    internal DatWriter(WriterMode mode = WriterMode.Standard)
    {
        this.mode = mode;
    }

    public override string FormatName => this.mode switch
    {
        WriterMode.LoadfileOnly => "DAT (Metadata)",
        WriterMode.ProductionSet => "Production Set DAT",
        _ => "DAT",
    };

    public override string FileExtension => ".dat";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        switch (this.mode)
        {
            case WriterMode.LoadfileOnly:
                await WriteLoadfileOnlyAsync(stream, request, chaosEngine);
                break;
            case WriterMode.ProductionSet:
                await WriteProductionSetAsync(stream, request, processedFiles);
                break;
            default:
                await WriteStandardAsync(stream, request, processedFiles);
                break;
        }
    }

    /// <summary>
    /// Writes header and rows to the configured writer. Extracted so internal
    /// callers can drive it with a pre-built StreamWriter when needed.
    /// </summary>
    internal static async Task WriteContentAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        List<FileData> processedFiles)
    {
        // Defensive guards to prevent IndexOutOfRangeException when delimiters are unset
        char colDelim = !string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? request.Delimiters.ColumnDelimiter[0] : '\u0014';
        char quote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? request.Delimiters.QuoteDelimiter[0] : '\u00fe';

        await writer.WriteLineAsync(BuildStandardHeader(request, colDelim, quote));

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value) : Random.Shared;
#pragma warning restore S2245
        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var builder = new MetadataRowBuilder(request, random, now);

        var buffer = new StringBuilder();
        int rowCount = 0;

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var line = BuildStandardRow(fileData, request, colDelim, quote, builder);
            buffer.AppendLine(line);
            rowCount++;

            if (rowCount >= 1000)
            {
                await writer.WriteAsync(buffer.ToString());
                buffer.Clear();
                rowCount = 0;
            }
        }

        if (buffer.Length > 0)
        {
            await writer.WriteAsync(buffer.ToString());
        }
    }

    private static async Task WriteStandardAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles)
    {
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);

        // Use leaveOpen: true to avoid disposing the caller's stream
        await using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
        await WriteContentAsync(writer, request, processedFiles);
        await writer.FlushAsync();
    }

    private static async Task WriteLoadfileOnlyAsync(
        Stream stream,
        FileGenerationRequest request,
        ChaosEngine? chaosEngine)
    {
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
        var eolString = GetEolString(request.Delimiters.EndOfLine);
        char colDelim = !string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? request.Delimiters.ColumnDelimiter[0] : '\u0014';
        char quote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? request.Delimiters.QuoteDelimiter[0] : '\u00fe';
        bool hasQuote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter);

        using var memStream = new MemoryStream();

        // Build header
        var header = BuildLoadfileOnlyHeader(colDelim, quote, hasQuote);

        // Apply chaos to header (line 1)
        header = ApplyChaosInterception(chaosEngine, 1, header, "HEADER");

        var headerBytes = encoding.GetBytes(header + eolString);
        await memStream.WriteAsync(headerBytes);

        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value + 1) : new Random();
#pragma warning restore S2245

        var builder = new MetadataRowBuilder(request, random, now);
        var buffer = new StringBuilder();

        for (long i = 1; i <= request.Output.FileCount; i++)
        {
            long lineNumber = i + 1;
            string recordId = builder.GetControlNumber(i);

            var line = BuildLoadfileOnlyRow(i, recordId, colDelim, quote, hasQuote, builder);

            line = ApplyChaosInterception(chaosEngine, lineNumber, line, recordId);

            buffer.Append(line);
            buffer.Append(eolString);

            // Handle encoding anomalies between lines
            if (chaosEngine != null && i < request.Output.FileCount)
            {
                // Flush current buffer first
                var bufferedBytes = encoding.GetBytes(buffer.ToString());
                await memStream.WriteAsync(bufferedBytes);
                buffer.Clear();

                await WriteEncodingAnomalyBytesAsync(memStream, chaosEngine, lineNumber, lineNumber + 1, encoding);
            }

            if (buffer.Length > 1000 * 200)
            {
                var batchBytes = encoding.GetBytes(buffer.ToString());
                await memStream.WriteAsync(batchBytes);
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            var remainingBytes = encoding.GetBytes(buffer.ToString());
            await memStream.WriteAsync(remainingBytes);
        }

        memStream.Position = 0;
        await memStream.CopyToAsync(stream);
    }

    private static async Task WriteProductionSetAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles)
    {
        var col = string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? "\u0014" : request.Delimiters.ColumnDelimiter;
        var quote = string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? "\u00fe" : request.Delimiters.QuoteDelimiter;
        var eol = GetEolString(request.Delimiters.EndOfLine);

        await using var writer = CreateWriter(stream, request);

        // Header
        var headers = new[] { "DOCID", "BATES_NUMBER", "VOLUME", "NATIVE_PATH", "TEXT_PATH", "IMAGE_PATH", "CUSTODIAN", "DATE_CREATED", "FILE_SIZE", "FILE_TYPE" };
        await writer.WriteAsync(string.Join(col, headers.Select(h => $"{quote}{h}{quote}")) + eol);

        // Data rows
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var batesNumber = BatesNumberGenerator.Generate(request.Bates!, workItem.Index - 1);
            var imagePath = workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
                .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif");

            var nativePath = workItem.FilePathInZip.Replace(Path.DirectorySeparatorChar, '\\');
            var textPath = nativePath.Replace($".{request.Output.FileType}", ".txt");
            var imagesPath = imagePath.Replace(Path.DirectorySeparatorChar, '\\');

#pragma warning disable S2245
            var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value + (int)workItem.Index) : Random.Shared;
#pragma warning restore S2245
            var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
            var builder = new MetadataRowBuilder(request, random, now);

            var fields = new[]
            {
                batesNumber,
                batesNumber,
                workItem.FolderName,
                nativePath,
                textPath,
                imagesPath,
                builder.GetCustodian(),
                builder.GetDateCreated(),
                fileData.DataLength.ToString(),
                request.Output.FileType.ToUpperInvariant(),
            };
            await writer.WriteAsync(string.Join(col, fields.Select(f => $"{quote}{f}{quote}")) + eol);
        }

        await writer.FlushAsync();
    }

    private static string BuildStandardHeader(FileGenerationRequest request, char colDelim, char quote)
    {
        var sb = new StringBuilder();
        sb.Append($"{quote}Control Number{quote}{colDelim}{quote}File Path{quote}");

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            sb.Append($"{colDelim}{quote}Custodian{quote}{colDelim}{quote}Date Sent{quote}{colDelim}{quote}Author{quote}{colDelim}{quote}File Size{quote}");
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            sb.Append($"{colDelim}{quote}To{quote}{colDelim}{quote}From{quote}{colDelim}{quote}Subject{quote}{colDelim}{quote}Sent Date{quote}{colDelim}{quote}Attachment{quote}");
        }

        if (request.Bates != null)
        {
            sb.Append($"{colDelim}{quote}Bates Number{quote}");
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            sb.Append($"{colDelim}{quote}Page Count{quote}");
        }

        if (request.Output.WithText)
        {
            sb.Append($"{colDelim}{quote}Extracted Text{quote}");
        }

        return sb.ToString();
    }

    private static string BuildStandardRow(
        FileData fileData,
        FileGenerationRequest request,
        char colDelim,
        char quote,
        MetadataRowBuilder builder)
    {
        var workItem = fileData.WorkItem;
        var docId = MetadataRowBuilder.SanitizeField(builder.GetControlNumber(workItem), request.Delimiters.NewlineDelimiter);

        var sb = new StringBuilder();
        sb.Append($"{quote}{docId}{quote}{colDelim}{quote}{MetadataRowBuilder.SanitizeField(workItem.FilePathInZip, request.Delimiters.NewlineDelimiter)}{quote}");

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            var custodian = MetadataRowBuilder.SanitizeField(builder.GetCustodian(workItem.FolderNumber), request.Delimiters.NewlineDelimiter);
            var dateSent = builder.GetDateSent();
            var author = MetadataRowBuilder.SanitizeField(builder.GetAuthor(), request.Delimiters.NewlineDelimiter);
            var fileSize = builder.GetFileSize(fileData);
            sb.Append($"{colDelim}{quote}{custodian}{quote}{colDelim}{quote}{dateSent}{quote}{colDelim}{quote}{author}{quote}{colDelim}{quote}{fileSize}{quote}");
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            var to = MetadataRowBuilder.SanitizeField(builder.GetEmailTo(workItem, fileData), request.Delimiters.NewlineDelimiter);
            var from = MetadataRowBuilder.SanitizeField(builder.GetEmailFrom(workItem, fileData), request.Delimiters.NewlineDelimiter);
            var subject = MetadataRowBuilder.SanitizeField(builder.GetEmailSubject(workItem, fileData), request.Delimiters.NewlineDelimiter);
            var sentDate = builder.GetEmailSentDate(workItem, fileData);
            var attachment = MetadataRowBuilder.SanitizeField(builder.GetEmailAttachment(fileData), request.Delimiters.NewlineDelimiter);
            sb.Append($"{colDelim}{quote}{to}{quote}{colDelim}{quote}{from}{quote}{colDelim}{quote}{subject}{quote}{colDelim}{quote}{sentDate}{quote}{colDelim}{quote}{attachment}{quote}");
        }

        if (request.Bates != null)
        {
            sb.Append($"{colDelim}{quote}{builder.GetBatesNumber(workItem)}{quote}");
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            sb.Append($"{colDelim}{quote}{fileData.PageCount}{quote}");
        }

        if (request.Output.WithText)
        {
            sb.Append($"{colDelim}{quote}{builder.GetTextPath(workItem)}{quote}");
        }

        return sb.ToString();
    }

    private static string BuildLoadfileOnlyHeader(
        char colDelim,
        char quote,
        bool hasQuote)
    {
        var sb = new StringBuilder();
        MetadataRowBuilder.AppendField(sb, "Control Number", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "File Path", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "Custodian", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "Date Sent", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "Author", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "File Size", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "EmailSubject", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "EmailFrom", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "EmailTo", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "EmailSentDate", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "ExtractedText", quote, hasQuote);

        return sb.ToString();
    }

    private static string BuildLoadfileOnlyRow(
        long index,
        string recordId,
        char colDelim,
        char quote,
        bool hasQuote,
        MetadataRowBuilder builder)
    {
        var workItem = new FileWorkItem { Index = index };
        var sb = new StringBuilder();
        var custodian = builder.GetCustodianByIndex(index);
        var dateSent = builder.GetDateSent();
        var author = builder.GetAuthor();
        var fileSize = builder.GetFileSize();
        var filePath = $"NATIVES\\{(index % 50) + 1:D3}\\{recordId}.pdf";
        var extractedText = $"Sample extracted text content for document {recordId}.";

        MetadataRowBuilder.AppendField(sb, recordId, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, filePath, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, custodian, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, dateSent, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, author, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, fileSize, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, builder.GetEmailSubject(workItem), quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, builder.GetEmailFrom(workItem), quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, builder.GetEmailTo(workItem), quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, builder.GetEmailSentDate(workItem), quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, extractedText, quote, hasQuote);

        return sb.ToString();
    }
}
