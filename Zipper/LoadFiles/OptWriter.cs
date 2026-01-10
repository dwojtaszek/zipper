using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes OPT (Opticon) format load files - tab-separated format used by Relativity
/// </summary>
internal class OptWriter : ILoadFileWriter
{
    public string FormatName => "OPT";
    public string FileExtension => ".opt";

    public async Task WriteAsync(Stream stream, FileGenerationRequest request, System.Collections.Generic.List<FileData> processedFiles)
    {
        using var writer = new StreamWriter(stream, Encoding.UTF8);

        const char tab = '\t';

        // Write header
        var headerBuilder = new StringBuilder();
        headerBuilder.Append($"Control Number{tab}File Path");

        if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
        {
            headerBuilder.Append($"{tab}Custodian{tab}Date Sent{tab}Author{tab}File Size");
        }

        if (request.FileType.ToLowerInvariant() == "eml")
        {
            headerBuilder.Append($"{tab}To{tab}From{tab}Subject{tab}Sent Date{tab}Attachment");
        }

        if (request.BatesConfig != null)
        {
            headerBuilder.Append($"{tab}Bates Number");
        }

        if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
        {
            headerBuilder.Append($"{tab}Page Count");
        }

        if (request.WithText)
        {
            headerBuilder.Append($"{tab}Extracted Text");
        }

        await writer.WriteLineAsync(headerBuilder.ToString());

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var docId = $"DOC{workItem.Index:D8}";

            var line = new StringBuilder();
            line.Append($"{docId}{tab}{workItem.FilePathInZip}");

            if (request.WithMetadata || request.FileType.ToLowerInvariant() == "eml")
            {
                var custodian = $"Custodian {workItem.FolderNumber}";
                var dateSent = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 365)).ToString("yyyy-MM-dd");
                var author = $"Author {Random.Shared.Next(1, 100):D3}";
                var fileSize = fileData.Data.Length;

                line.Append($"{tab}{custodian}{tab}{dateSent}{tab}{author}{tab}{fileSize}");
            }

            if (request.FileType.ToLowerInvariant() == "eml")
            {
                var to = $"recipient{workItem.Index}@example.com";
                var from = $"sender{workItem.Index}@example.com";
                var subject = $"Email Subject {workItem.Index}";
                var sentDate = System.DateTime.Now.AddDays(-Random.Shared.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
                var attachmentName = fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : "";

                line.Append($"{tab}{to}{tab}{from}{tab}{subject}{tab}{sentDate}{tab}{attachmentName}");
            }

            if (request.BatesConfig != null)
            {
                var batesNumber = BatesNumberGenerator.Generate(request.BatesConfig, workItem.Index - 1);
                line.Append($"{tab}{batesNumber}");
            }

            if (request.FileType.ToLowerInvariant() == "tiff" && request.TiffPageRange.HasValue)
            {
                line.Append($"{tab}{fileData.PageCount}");
            }

            if (request.WithText)
            {
                var textFilePath = workItem.FilePathInZip.Replace($".{request.FileType}", ".txt");
                line.Append($"{tab}{textFilePath}");
            }

            await writer.WriteLineAsync(line.ToString());
        }
    }
}
