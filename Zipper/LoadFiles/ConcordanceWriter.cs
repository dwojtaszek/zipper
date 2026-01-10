using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes CONCORDANCE format load files - database import format with specific delimiters
/// </summary>
internal class ConcordanceWriter : ILoadFileWriter
{
    public string FormatName => "CONCORDANCE";
    public string FileExtension => ".dat";

    public async Task WriteAsync(Stream stream, FileGenerationRequest request, System.Collections.Generic.List<FileData> processedFiles)
    {
        using var writer = new StreamWriter(stream, EncodingHelper.GetEncodingOrDefault(request.Encoding));

        const char fieldDelim = '\x14';
        const char quote = '"';

        var headerBuilder = new StringBuilder();
        headerBuilder.Append($"BEGATTY{quote}{fieldDelim}");
        headerBuilder.Append($"ENDDATTY{quote}{fieldDelim}");
        headerBuilder.Append($"CONTROLNUMBER{quote}{fieldDelim}");
        headerBuilder.Append($"PATH{quote}{fieldDelim}");

        if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
        {
            headerBuilder.Append($"CUSTODIAN{quote}{fieldDelim}");
            headerBuilder.Append($"DATESENT{quote}{fieldDelim}");
            headerBuilder.Append($"AUTHOR{quote}{fieldDelim}");
            headerBuilder.Append($"FILESIZE{quote}{fieldDelim}");
        }

        if (request.FileType.ToLowerInvariant() == "eml")
        {
            headerBuilder.Append($"TO{quote}{fieldDelim}");
            headerBuilder.Append($"FROM{quote}{fieldDelim}");
            headerBuilder.Append($"SUBJECT{quote}{fieldDelim}");
            headerBuilder.Append($"SENTDATE{quote}{fieldDelim}");
            headerBuilder.Append($"ATTACHMENT{quote}{fieldDelim}");
        }

        if (request.BatesConfig != null)
        {
            headerBuilder.Append($"BATES{quote}{fieldDelim}");
        }

        if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
        {
            headerBuilder.Append($"PAGECOUNT{quote}{fieldDelim}");
        }

        if (request.WithText)
        {
            headerBuilder.Append($"TEXT_PATH{quote}{fieldDelim}");
        }

        await writer.WriteLineAsync(headerBuilder.ToString());

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var lineBuilder = new StringBuilder();

            lineBuilder.Append($"{quote}{quote}{fieldDelim}");
            lineBuilder.Append($"{quote}{quote}{fieldDelim}");
            lineBuilder.Append($"{quote}DOC{workItem.Index:D8}{quote}{fieldDelim}");
            lineBuilder.Append($"{quote}{workItem.FilePathInZip}{quote}{fieldDelim}");

            if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
            {
                var custodian = $"Custodian {workItem.FolderNumber}";
                var dateSent = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd");
                var author = $"Author {Random.Shared.Next(1, 100):D3}";
                var fileSize = fileData.Data.Length;

                lineBuilder.Append($"{quote}{custodian}{quote}{fieldDelim}");
                lineBuilder.Append($"{quote}{dateSent}{quote}{fieldDelim}");
                lineBuilder.Append($"{quote}{author}{quote}{fieldDelim}");
                lineBuilder.Append($"{quote}{fileSize}{quote}{fieldDelim}");
            }

            if (request.FileType.ToLowerInvariant() == "eml")
            {
                var to = $"recipient{workItem.Index}@example.com";
                var from = $"sender{workItem.Index}@example.com";
                var subject = $"Email Subject {workItem.Index}";
                var sentDate = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
                var attachmentName = fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : "";

                lineBuilder.Append($"{quote}{to}{quote}{fieldDelim}");
                lineBuilder.Append($"{quote}{from}{quote}{fieldDelim}");
                lineBuilder.Append($"{quote}{subject}{quote}{fieldDelim}");
                lineBuilder.Append($"{quote}{sentDate}{quote}{fieldDelim}");
                lineBuilder.Append($"{quote}{attachmentName}{quote}{fieldDelim}");
            }

            if (request.BatesConfig != null)
            {
                var batesNumber = BatesNumberGenerator.Generate(request.BatesConfig, workItem.Index - 1);
                lineBuilder.Append($"{quote}{batesNumber}{quote}{fieldDelim}");
            }

            if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
            {
                lineBuilder.Append($"{quote}{fileData.PageCount}{quote}{fieldDelim}");
            }

            if (request.WithText)
            {
                var textFilePath = workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
                lineBuilder.Append($"{quote}{textFilePath}{quote}{fieldDelim}");
            }

            await writer.WriteLineAsync(lineBuilder.ToString());
        }
    }
}
