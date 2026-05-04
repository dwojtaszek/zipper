using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes OPT (Opticon) format load files — comma-separated, no header, 7-column standard.
/// Opticon specification: BatesNumber,Volume,ImagePath,DocBreak(Y/blank),FolderBreak,BoxBreak,PageCount
/// </summary>
internal class OptWriter : LoadFileWriterBase
{
    public override string FormatName => "OPT";

    public override string FileExtension => ".opt";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        // Use leaveOpen: true to avoid disposing the caller's stream
        await using var writer = new StreamWriter(stream, Zipper.EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding), leaveOpen: true);

        await WriteRowsAsync(writer, request, processedFiles);

        // Flush to ensure data is written
        await writer.FlushAsync();
    }

    private static async Task WriteRowsAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles)
    {
        if (ShouldIncludeMetadata(request))
        {
            Console.Error.WriteLine("Warning: --with-metadata columns are not supported in Opticon format. The OPT file uses the standard 7-column layout.");
        }

        if (ShouldIncludeEmlColumns(request))
        {
            Console.Error.WriteLine("Warning: Email metadata columns are not supported in Opticon format. The OPT file uses the standard 7-column layout.");
        }

        if (request.Output.WithText)
        {
            Console.Error.WriteLine("Warning: --with-text is not supported in Opticon format. The OPT file uses the standard 7-column layout.");
        }

        if (request.Bates != null)
        {
            Console.Error.WriteLine("Warning: The Bates number column is part of the standard Opticon 7-column format (column 1). Other Bates configuration is ignored.");
        }

        var buffer = new StringBuilder();
        int rowCount = 0;

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;

            // Opticon 7-column format: BatesNumber,Volume,ImagePath,DocBreak,FolderBreak,BoxBreak,PageCount
            string batesNumber = request.Bates != null
                ? GenerateBatesNumber(request, workItem)
                : GenerateDocumentId(workItem);
            string volume = "VOL001";
            string imagePath = $"IMAGES\\{batesNumber}.tif";
            string docBreak = "Y";
            string folderBreak = string.Empty;
            string boxBreak = string.Empty;
            int pageCount = ShouldIncludePageCount(request) ? fileData.PageCount : 1;

            // Comma-separated, no header — Opticon standard
            var line = $"{batesNumber},{volume},{imagePath},{docBreak},{folderBreak},{boxBreak},{pageCount}";

            buffer.AppendLine(line);
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
