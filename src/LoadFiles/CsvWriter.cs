using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes CSV format load files - comma-separated values with proper RFC 4180 escaping.
/// </summary>
internal class CsvWriter : LoadFileWriterBase
{
    public override string FormatName => "CSV";

    public override string FileExtension => ".csv";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles)
    {
        // Use leaveOpen: true to avoid disposing the caller's stream
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

        var random = request.Seed.HasValue ? new Random(request.Seed.Value) : Random.Shared;
        var now = DateTime.UtcNow;

        await WriteHeaderAsync(writer, request);
        await WriteRowsAsync(writer, request, processedFiles, random, now);

        // Flush to ensure data is written
        await writer.FlushAsync();
    }

    private static Task WriteHeaderAsync(StreamWriter writer, FileGenerationRequest request)
    {
        var headers = new System.Collections.Generic.List<string> { "Control Number", "File Path" };

        if (ShouldIncludeMetadata(request))
        {
            headers.AddRange(new[] { "Custodian", "Date Sent", "Author", "File Size" });
        }

        if (ShouldIncludeEmlColumns(request))
        {
            headers.AddRange(new[] { "To", "From", "Subject", "Sent Date", "Attachment" });
        }

        if (request.BatesConfig != null)
        {
            headers.Add("Bates Number");
        }

        if (ShouldIncludePageCount(request))
        {
            headers.Add("Page Count");
        }

        if (request.WithText)
        {
            headers.Add("Extracted Text");
        }

        return writer.WriteLineAsync(string.Join(",", headers));
    }

    private static async Task WriteRowsAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles,
        Random random,
        DateTime now)
    {
        var buffer = new StringBuilder();
        int rowCount = 0;

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var values = new System.Collections.Generic.List<string>
            {
                EscapeCsvField(GenerateDocumentId(workItem)),
                EscapeCsvField(workItem.FilePathInZip),
            };

            if (ShouldIncludeMetadata(request))
            {
                var metadata = GenerateMetadataValues(workItem, fileData, random, now);
                values.AddRange(new[]
                {
                    EscapeCsvField(metadata.Custodian),
                    EscapeCsvField(metadata.DateSent),
                    EscapeCsvField(metadata.Author),
                    metadata.FileSize.ToString(),
                });
            }

            if (ShouldIncludeEmlColumns(request))
            {
                var eml = GenerateEmlValues(workItem, fileData, random, now);
                values.AddRange(new[]
                {
                    EscapeCsvField(eml.To),
                    EscapeCsvField(eml.From),
                    EscapeCsvField(eml.Subject),
                    EscapeCsvField(eml.SentDate),
                    EscapeCsvField(eml.Attachment),
                });
            }

            if (request.BatesConfig != null)
            {
                values.Add(EscapeCsvField(GenerateBatesNumber(request, workItem)));
            }

            if (ShouldIncludePageCount(request))
            {
                values.Add(fileData.PageCount.ToString());
            }

            if (request.WithText)
            {
                values.Add(EscapeCsvField(GenerateTextPath(request, workItem)));
            }

            buffer.AppendLine(string.Join(",", values));
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
