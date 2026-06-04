using System.Text;
using Zipper.Utils;

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
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        // Use leaveOpen: true to avoid disposing the caller's stream
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
        await using var writer = new StreamWriter(stream, encoding, leaveOpen: true);

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value) : Random.Shared;
#pragma warning restore S2245
        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

        await WriteHeaderAsync(writer, request);
        await WriteRowsAsync(writer, request, processedFiles, random, now);

        // Flush to ensure data is written
        await writer.FlushAsync();
    }

    private static Task WriteHeaderAsync(StreamWriter writer, FileGenerationRequest request)
    {
        var namingConvention = request.Metadata.ColumnProfile?.FieldNamingConvention;
        var headers = new System.Collections.Generic.List<string>
        {
            NamingConventionHelper.ApplyConvention("Control Number", namingConvention),
            NamingConventionHelper.ApplyConvention("File Path", namingConvention)
        };

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            headers.AddRange(new[]
            {
                NamingConventionHelper.ApplyConvention("Custodian", namingConvention),
                NamingConventionHelper.ApplyConvention("Date Sent", namingConvention),
                NamingConventionHelper.ApplyConvention("Author", namingConvention),
                NamingConventionHelper.ApplyConvention("File Size", namingConvention)
            });
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            headers.AddRange(new[]
            {
                NamingConventionHelper.ApplyConvention("To", namingConvention),
                NamingConventionHelper.ApplyConvention("From", namingConvention),
                NamingConventionHelper.ApplyConvention("Subject", namingConvention),
                NamingConventionHelper.ApplyConvention("Sent Date", namingConvention),
                NamingConventionHelper.ApplyConvention("Attachment", namingConvention)
            });
        }

        if (request.Bates != null)
        {
            headers.Add(NamingConventionHelper.ApplyConvention("Bates Number", namingConvention));
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            headers.Add(NamingConventionHelper.ApplyConvention("Page Count", namingConvention));
        }

        if (request.Output.WithText)
        {
            headers.Add(NamingConventionHelper.ApplyConvention("Extracted Text", namingConvention));
        }

        return writer.WriteLineAsync(string.Join(",", headers));
    }

    private static async Task WriteRowsAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
        Random random,
        DateTime now)
    {
        var buffer = new StringBuilder();
        int rowCount = 0;

        foreach (var fileData in processedFiles)
        {
            var workItem = fileData.WorkItem;
            var values = new System.Collections.Generic.List<string>
            {
                EscapeCsvField(GenerateDocumentId(workItem)),
                EscapeCsvField(workItem.FilePathInZip),
            };

            if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
            {
                var metadata = GenerateMetadataValues(workItem, fileData, random, now, request);
                values.AddRange(new[]
                {
                    EscapeCsvField(metadata.Custodian),
                    EscapeCsvField(metadata.DateSent),
                    EscapeCsvField(metadata.Author),
                    metadata.FileSize.ToString(),
                });
            }

            if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
            {
                var eml = GenerateEmlValues(workItem, fileData, random, now, request);
                values.AddRange(new[]
                {
                    EscapeCsvField(eml.To),
                    EscapeCsvField(eml.From),
                    EscapeCsvField(eml.Subject),
                    EscapeCsvField(eml.SentDate),
                    EscapeCsvField(eml.Attachment),
                });
            }

            if (request.Bates != null)
            {
                values.Add(EscapeCsvField(GenerateBatesNumber(request, workItem)));
            }

            if (request.Tiff.ShouldIncludePageCount(request.Output))
            {
                values.Add(fileData.PageCount.ToString());
            }

            if (request.Output.WithText)
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
