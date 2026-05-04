using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes the standard pipeline DAT (Concordance) load file format —
/// quote-wrapped fields separated by configurable column delimiters,
/// with a header row and optional metadata, EML, Bates, page count, and extracted text columns.
/// </summary>
internal class DatWriter : LoadFileWriterBase
{
    public override string FormatName => "DAT";

    public override string FileExtension => ".dat";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);

        // Use leaveOpen: true to avoid disposing the caller's stream
        await using var writer = new StreamWriter(stream, encoding, leaveOpen: true);

        await WriteContentAsync(writer, request, processedFiles);

        await writer.FlushAsync();
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

        await writer.WriteLineAsync(BuildHeader(request, colDelim, quote));

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value) : Random.Shared;
#pragma warning restore S2245
        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var builder = new MetadataRowBuilder(request, random, now);

        var buffer = new StringBuilder();
        int rowCount = 0;

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var line = BuildRow(fileData, request, colDelim, quote, builder);
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

    private static string BuildHeader(FileGenerationRequest request, char colDelim, char quote)
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

    private static string BuildRow(
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
}
