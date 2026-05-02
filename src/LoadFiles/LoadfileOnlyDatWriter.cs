using System.Text;

namespace Zipper.LoadFiles;

internal class LoadfileOnlyDatWriter : LoadFileWriterBase
{
    public override string FormatName => "DAT (Metadata)";

    public override string FileExtension => ".dat";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var encoding = EncodingHelper.GetEncodingOrDefault(request.Encoding);
        var eolString = GetEolString(request.EndOfLine);
        char colDelim = !string.IsNullOrEmpty(request.ColumnDelimiter) ? request.ColumnDelimiter[0] : '\u0014';
        char quote = !string.IsNullOrEmpty(request.QuoteDelimiter) ? request.QuoteDelimiter[0] : '\u00fe';
        bool hasQuote = !string.IsNullOrEmpty(request.QuoteDelimiter);

        using var memStream = new MemoryStream();

        // Build header
        var header = BuildDatHeader(colDelim, quote, hasQuote);

        // Apply chaos to header (line 1)
        header = ApplyChaosInterception(chaosEngine, 1, header, "HEADER");

        var headerBytes = encoding.GetBytes(header + eolString);
        await memStream.WriteAsync(headerBytes);

        var now = request.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

#pragma warning disable S2245
        var random = request.Seed.HasValue ? new Random(request.Seed.Value + 1) : new Random();
#pragma warning restore S2245

        var builder = new MetadataRowBuilder(request, random, now);
        var buffer = new StringBuilder();

        for (long i = 1; i <= request.FileCount; i++)
        {
            long lineNumber = i + 1;
            string recordId = builder.GetControlNumber(i);

            var line = BuildDatRow(i, recordId, colDelim, quote, hasQuote, builder);

            line = ApplyChaosInterception(chaosEngine, lineNumber, line, recordId);

            buffer.Append(line);
            buffer.Append(eolString);

            // Handle encoding anomalies between lines
            if (chaosEngine != null && i < request.FileCount)
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

    private static string BuildDatHeader(
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

    private static string BuildDatRow(
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
