using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes CSV format load files - comma-separated values with proper RFC 4180 escaping
/// </summary>
internal class CsvWriter : ILoadFileWriter
{
    public string FormatName => "CSV";
    public string FileExtension => ".csv";

    public async Task WriteAsync(Stream stream, FileGenerationRequest request, System.Collections.Generic.List<FileData> processedFiles)
    {
        using var writer = new StreamWriter(stream, Encoding.UTF8);

        var headers = new System.Collections.Generic.List<string> { "Control Number", "File Path" };

        if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
        {
            headers.AddRange(new[] { "Custodian", "Date Sent", "Author", "File Size" });
        }

        if (request.FileType.ToLowerInvariant() == "eml")
        {
            headers.AddRange(new[] { "To", "From", "Subject", "Sent Date", "Attachment" });
        }

        if (request.BatesConfig != null)
        {
            headers.Add("Bates Number");
        }

        if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
        {
            headers.Add("Page Count");
        }

        if (request.WithText)
        {
            headers.Add("Extracted Text");
        }

        await writer.WriteLineAsync(string.Join(",", headers));

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var values = new System.Collections.Generic.List<string>
            {
                EscapeCsvField($"DOC{workItem.Index:D8}"),
                EscapeCsvField(workItem.FilePathInZip)
            };

            if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
            {
                var custodian = $"Custodian {workItem.FolderNumber}";
                var dateSent = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd");
                var author = $"Author {Random.Shared.Next(1, 100):D3}";
                var fileSize = fileData.Data.Length;

                values.AddRange(new[] {
                    EscapeCsvField(custodian),
                    EscapeCsvField(dateSent),
                    EscapeCsvField(author),
                    fileSize.ToString()
                });
            }

            if (request.FileType.ToLowerInvariant() == "eml")
            {
                var to = $"recipient{workItem.Index}@example.com";
                var from = $"sender{workItem.Index}@example.com";
                var subject = $"Email Subject {workItem.Index}";
                var sentDate = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
                var attachmentName = fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : "";

                values.AddRange(new[] {
                    EscapeCsvField(to),
                    EscapeCsvField(from),
                    EscapeCsvField(subject),
                    EscapeCsvField(sentDate),
                    EscapeCsvField(attachmentName)
                });
            }

            if (request.BatesConfig != null)
            {
                var batesNumber = BatesNumberGenerator.Generate(request.BatesConfig, workItem.Index - 1);
                values.Add(EscapeCsvField(batesNumber));
            }

            if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
            {
                values.Add(fileData.PageCount.ToString());
            }

            if (request.WithText)
            {
                var textFilePath = workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
                values.Add(EscapeCsvField(textFilePath));
            }

            await writer.WriteLineAsync(string.Join(",", values));
        }
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return "";

        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
