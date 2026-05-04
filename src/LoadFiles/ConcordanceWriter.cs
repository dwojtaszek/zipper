using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes CONCORDANCE format load files - database import format with specific delimiters.
/// </summary>
internal class ConcordanceWriter : LoadFileWriterBase
{
    public override string FormatName => "CONCORDANCE";

    public override string FileExtension => ".dat";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        // Use leaveOpen: true to avoid disposing the caller's stream
        await using var writer = new StreamWriter(stream, Zipper.EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding), leaveOpen: true);

        // Concordance DAT format uses request.Delimiters.ColumnDelimiter with DAT escaping (þ quote char doubled)
        // Default column delimiter is ASCII 20 (DC4), quote delimiter is ASCII 254 (þ)
        char fieldDelim = !string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? request.Delimiters.ColumnDelimiter[0] : ',';
        char quoteDelim = '\u00fe'; // ASCII 254 — Concordance standard quote character

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value) : Random.Shared;
#pragma warning restore S2245
        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

        await WriteHeaderAsync(writer, request, fieldDelim, quoteDelim);
        await WriteRowsAsync(writer, request, processedFiles, fieldDelim, quoteDelim, random, now);

        // Flush to ensure data is written
        await writer.FlushAsync();
    }

    private static Task WriteHeaderAsync(StreamWriter writer, FileGenerationRequest request, char fieldDelim, char quoteDelim)
    {
        var header = new StringBuilder();

        // Concordance format headers are wrapped in quote delimiter, comma-delimited
        header.Append($"{quoteDelim}BEGATTY{quoteDelim}{fieldDelim}");
        header.Append($"{quoteDelim}ENDDATTY{quoteDelim}{fieldDelim}");
        header.Append($"{quoteDelim}CONTROLNUMBER{quoteDelim}{fieldDelim}");
        header.Append($"{quoteDelim}PATH{quoteDelim}{fieldDelim}");

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            header.Append($"{quoteDelim}CUSTODIAN{quoteDelim}{fieldDelim}");
            header.Append($"{quoteDelim}DATESENT{quoteDelim}{fieldDelim}");
            header.Append($"{quoteDelim}AUTHOR{quoteDelim}{fieldDelim}");
            header.Append($"{quoteDelim}FILESIZE{quoteDelim}{fieldDelim}");
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            header.Append($"{quoteDelim}TO{quoteDelim}{fieldDelim}");
            header.Append($"{quoteDelim}FROM{quoteDelim}{fieldDelim}");
            header.Append($"{quoteDelim}SUBJECT{quoteDelim}{fieldDelim}");
            header.Append($"{quoteDelim}SENTDATE{quoteDelim}{fieldDelim}");
            header.Append($"{quoteDelim}ATTACHMENT{quoteDelim}{fieldDelim}");
        }

        if (request.Bates != null)
        {
            header.Append($"{quoteDelim}BATES{quoteDelim}{fieldDelim}");
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            header.Append($"{quoteDelim}PAGECOUNT{quoteDelim}{fieldDelim}");
        }

        if (request.Output.WithText)
        {
            header.Append($"{quoteDelim}TEXT_PATH{quoteDelim}{fieldDelim}");
        }

        return writer.WriteLineAsync(header.ToString().TrimEnd(fieldDelim));
    }

    private static async Task WriteRowsAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles,
        char fieldDelim,
        char quoteDelim,
        Random random,
        DateTime now)
    {
        var buffer = new StringBuilder();
        int rowCount = 0;

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var line = new StringBuilder();

            // Note: BEGATTY and ENDDATTY (beginning/ending attachment Bates numbers)
            // are intentionally left empty. The format requires these fields,
            // but this generator does not track attachment parent/child ranges.
            line.Append($"{quoteDelim}{quoteDelim}{fieldDelim}");  // BEGATTY field (empty)
            line.Append($"{quoteDelim}{quoteDelim}{fieldDelim}");  // ENDDATTY field (empty)
            line.Append($"{quoteDelim}{EscapeDatField(GenerateDocumentId(workItem), quoteDelim)}{quoteDelim}{fieldDelim}");
            line.Append($"{quoteDelim}{EscapeDatField(workItem.FilePathInZip, quoteDelim)}{quoteDelim}{fieldDelim}");

            if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
            {
                var metadata = GenerateMetadataValues(workItem, fileData, random, now, request);
                line.Append($"{quoteDelim}{EscapeDatField(metadata.Custodian, quoteDelim)}{quoteDelim}{fieldDelim}");
                line.Append($"{quoteDelim}{EscapeDatField(metadata.DateSent, quoteDelim)}{quoteDelim}{fieldDelim}");
                line.Append($"{quoteDelim}{EscapeDatField(metadata.Author, quoteDelim)}{quoteDelim}{fieldDelim}");
                line.Append($"{quoteDelim}{EscapeDatField(metadata.FileSize.ToString(), quoteDelim)}{quoteDelim}{fieldDelim}");
            }

            if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
            {
                var eml = GenerateEmlValues(workItem, fileData, random, now, request);
                line.Append($"{quoteDelim}{EscapeDatField(eml.To, quoteDelim)}{quoteDelim}{fieldDelim}");
                line.Append($"{quoteDelim}{EscapeDatField(eml.From, quoteDelim)}{quoteDelim}{fieldDelim}");
                line.Append($"{quoteDelim}{EscapeDatField(eml.Subject, quoteDelim)}{quoteDelim}{fieldDelim}");
                line.Append($"{quoteDelim}{EscapeDatField(eml.SentDate, quoteDelim)}{quoteDelim}{fieldDelim}");
                line.Append($"{quoteDelim}{EscapeDatField(eml.Attachment, quoteDelim)}{quoteDelim}{fieldDelim}");
            }

            if (request.Bates != null)
            {
                line.Append($"{quoteDelim}{EscapeDatField(GenerateBatesNumber(request, workItem), quoteDelim)}{quoteDelim}{fieldDelim}");
            }

            if (request.Tiff.ShouldIncludePageCount(request.Output))
            {
                line.Append($"{quoteDelim}{EscapeDatField(fileData.PageCount.ToString(), quoteDelim)}{quoteDelim}{fieldDelim}");
            }

            if (request.Output.WithText)
            {
                line.Append($"{quoteDelim}{EscapeDatField(GenerateTextPath(request, workItem), quoteDelim)}{quoteDelim}{fieldDelim}");
            }

            buffer.AppendLine(line.ToString().TrimEnd(fieldDelim));
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
}
