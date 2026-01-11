// <copyright file="CsvWriter.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

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

        await WriteHeaderAsync(writer, request);
        await WriteRowsAsync(writer, request, processedFiles);
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
        System.Collections.Generic.List<FileData> processedFiles)
    {
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var values = new System.Collections.Generic.List<string>
            {
                EscapeField(GenerateDocumentId(workItem)),
                EscapeField(workItem.FilePathInZip),
            };

            if (ShouldIncludeMetadata(request))
            {
                var metadata = GenerateMetadataValues(workItem, fileData);
                values.AddRange(new[]
                {
                    EscapeField(metadata.Custodian),
                    EscapeField(metadata.DateSent),
                    EscapeField(metadata.Author),
                    metadata.FileSize.ToString(),
                });
            }

            if (ShouldIncludeEmlColumns(request))
            {
                var eml = GenerateEmlValues(workItem, fileData);
                values.AddRange(new[]
                {
                    EscapeField(eml.To),
                    EscapeField(eml.From),
                    EscapeField(eml.Subject),
                    EscapeField(eml.SentDate),
                    EscapeField(eml.Attachment),
                });
            }

            if (request.BatesConfig != null)
            {
                values.Add(EscapeField(GenerateBatesNumber(request, workItem)));
            }

            if (ShouldIncludePageCount(request))
            {
                values.Add(fileData.PageCount.ToString());
            }

            if (request.WithText)
            {
                values.Add(EscapeField(GenerateTextPath(request, workItem)));
            }

            await writer.WriteLineAsync(string.Join(",", values));
        }
    }

    private static string EscapeField(string field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}
