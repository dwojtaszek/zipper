using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes CONCORDANCE format load files - database import format with specific delimiters
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
        using var writer = new StreamWriter(stream, Zipper.EncodingHelper.GetEncodingOrDefault(request.Encoding));

        const char fieldDelim = '\x14';
        const char quote = '"';

        await WriteHeaderAsync(writer, request, fieldDelim, quote);
        await WriteRowsAsync(writer, request, processedFiles, fieldDelim, quote);
    }

    private static Task WriteHeaderAsync(StreamWriter writer, FileGenerationRequest request, char fieldDelim, char quote)
    {
        var header = new StringBuilder();
        header.Append($"BEGATTY{quote}{fieldDelim}");
        header.Append($"ENDDATTY{quote}{fieldDelim}");
        header.Append($"CONTROLNUMBER{quote}{fieldDelim}");
        header.Append($"PATH{quote}{fieldDelim}");

        if (ShouldIncludeMetadata(request))
        {
            header.Append($"CUSTODIAN{quote}{fieldDelim}");
            header.Append($"DATESENT{quote}{fieldDelim}");
            header.Append($"AUTHOR{quote}{fieldDelim}");
            header.Append($"FILESIZE{quote}{fieldDelim}");
        }

        if (ShouldIncludeEmlColumns(request))
        {
            header.Append($"TO{quote}{fieldDelim}");
            header.Append($"FROM{quote}{fieldDelim}");
            header.Append($"SUBJECT{quote}{fieldDelim}");
            header.Append($"SENTDATE{quote}{fieldDelim}");
            header.Append($"ATTACHMENT{quote}{fieldDelim}");
        }

        if (request.BatesConfig != null)
        {
            header.Append($"BATES{quote}{fieldDelim}");
        }

        if (ShouldIncludePageCount(request))
        {
            header.Append($"PAGECOUNT{quote}{fieldDelim}");
        }

        if (request.WithText)
        {
            header.Append($"TEXT_PATH{quote}{fieldDelim}");
        }

        return writer.WriteLineAsync(header.ToString());
    }

    private static async Task WriteRowsAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles,
        char fieldDelim,
        char quote)
    {
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var line = new StringBuilder();

            line.Append($"{quote}{quote}{fieldDelim}");
            line.Append($"{quote}{quote}{fieldDelim}");
            line.Append($"{quote}{GenerateDocumentId(workItem)}{quote}{fieldDelim}");
            line.Append($"{quote}{workItem.FilePathInZip}{quote}{fieldDelim}");

            if (ShouldIncludeMetadata(request))
            {
                var metadata = GenerateMetadataValues(workItem, fileData);
                line.Append($"{quote}{metadata.Custodian}{quote}{fieldDelim}");
                line.Append($"{quote}{metadata.DateSent}{quote}{fieldDelim}");
                line.Append($"{quote}{metadata.Author}{quote}{fieldDelim}");
                line.Append($"{quote}{metadata.FileSize}{quote}{fieldDelim}");
            }

            if (ShouldIncludeEmlColumns(request))
            {
                var eml = GenerateEmlValues(workItem, fileData);
                line.Append($"{quote}{eml.To}{quote}{fieldDelim}");
                line.Append($"{quote}{eml.From}{quote}{fieldDelim}");
                line.Append($"{quote}{eml.Subject}{quote}{fieldDelim}");
                line.Append($"{quote}{eml.SentDate}{quote}{fieldDelim}");
                line.Append($"{quote}{eml.Attachment}{quote}{fieldDelim}");
            }

            if (request.BatesConfig != null)
            {
                line.Append($"{quote}{GenerateBatesNumber(request, workItem)}{quote}{fieldDelim}");
            }

            if (ShouldIncludePageCount(request))
            {
                line.Append($"{quote}{fileData.PageCount}{quote}{fieldDelim}");
            }

            if (request.WithText)
            {
                line.Append($"{quote}{GenerateTextPath(request, workItem)}{quote}{fieldDelim}");
            }

            await writer.WriteLineAsync(line.ToString());
        }
    }
}
