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
        System.Collections.Generic.List<FileData> processedFiles)
    {
        // Use leaveOpen: true to avoid disposing the caller's stream
        await using var writer = new StreamWriter(stream, Zipper.EncodingHelper.GetEncodingOrDefault(request.Encoding), leaveOpen: true);

        // Concordance DAT format uses comma delimiter with CSV escaping
        const char fieldDelim = ',';
        const char quote = '"';

        var random = request.Seed.HasValue ? new Random(request.Seed.Value) : Random.Shared;
        var now = DateTime.UtcNow;

        await WriteHeaderAsync(writer, request, fieldDelim, quote);
        await WriteRowsAsync(writer, request, processedFiles, fieldDelim, quote, random, now);

        // Flush to ensure data is written
        await writer.FlushAsync();
    }

    private static Task WriteHeaderAsync(StreamWriter writer, FileGenerationRequest request, char fieldDelim, char quote)
    {
        var header = new StringBuilder();

        // Concordance format headers are unquoted, comma-delimited
        header.Append($"BEGATTY{fieldDelim}");
        header.Append($"ENDDATTY{fieldDelim}");
        header.Append($"CONTROLNUMBER{fieldDelim}");
        header.Append($"PATH{fieldDelim}");

        if (ShouldIncludeMetadata(request))
        {
            header.Append($"CUSTODIAN{fieldDelim}");
            header.Append($"DATESENT{fieldDelim}");
            header.Append($"AUTHOR{fieldDelim}");
            header.Append($"FILESIZE{fieldDelim}");
        }

        if (ShouldIncludeEmlColumns(request))
        {
            header.Append($"TO{fieldDelim}");
            header.Append($"FROM{fieldDelim}");
            header.Append($"SUBJECT{fieldDelim}");
            header.Append($"SENTDATE{fieldDelim}");
            header.Append($"ATTACHMENT{fieldDelim}");
        }

        if (request.BatesConfig != null)
        {
            header.Append($"BATES{fieldDelim}");
        }

        if (ShouldIncludePageCount(request))
        {
            header.Append($"PAGECOUNT{fieldDelim}");
        }

        if (request.WithText)
        {
            header.Append($"TEXT_PATH{fieldDelim}");
        }

        return writer.WriteLineAsync(header.ToString().TrimEnd(fieldDelim));
    }

    private static async Task WriteRowsAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles,
        char fieldDelim,
        char quote,
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
            line.Append($"{fieldDelim}");  // BEGATTY field
            line.Append($"{fieldDelim}");  // ENDDATTY field
            line.Append($"{EscapeCsvField(GenerateDocumentId(workItem))}{fieldDelim}");
            line.Append($"{EscapeCsvField(workItem.FilePathInZip)}{fieldDelim}");

            if (ShouldIncludeMetadata(request))
            {
                var metadata = GenerateMetadataValues(workItem, fileData, random, now);
                line.Append($"{EscapeCsvField(metadata.Custodian)}{fieldDelim}");
                line.Append($"{EscapeCsvField(metadata.DateSent)}{fieldDelim}");
                line.Append($"{EscapeCsvField(metadata.Author)}{fieldDelim}");
                line.Append($"{EscapeCsvField(metadata.FileSize.ToString())}{fieldDelim}");
            }

            if (ShouldIncludeEmlColumns(request))
            {
                var eml = GenerateEmlValues(workItem, fileData, random, now);
                line.Append($"{EscapeCsvField(eml.To)}{fieldDelim}");
                line.Append($"{EscapeCsvField(eml.From)}{fieldDelim}");
                line.Append($"{EscapeCsvField(eml.Subject)}{fieldDelim}");
                line.Append($"{EscapeCsvField(eml.SentDate)}{fieldDelim}");
                line.Append($"{EscapeCsvField(eml.Attachment)}{fieldDelim}");
            }

            if (request.BatesConfig != null)
            {
                line.Append($"{EscapeCsvField(GenerateBatesNumber(request, workItem))}{fieldDelim}");
            }

            if (ShouldIncludePageCount(request))
            {
                line.Append($"{EscapeCsvField(fileData.PageCount.ToString())}{fieldDelim}");
            }

            if (request.WithText)
            {
                line.Append($"{EscapeCsvField(GenerateTextPath(request, workItem))}{fieldDelim}");
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
