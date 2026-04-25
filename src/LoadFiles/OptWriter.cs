using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes OPT (Opticon) format load files — comma-separated, no header, 7-column standard.
/// Opticon specification: BatesNumber,Volume,ImagePath,DocBreak(Y/blank),BoxBreak,FolderBreak,PageCount
/// </summary>
internal class OptWriter : LoadFileWriterBase
{
    public override string FormatName => "OPT";

    public override string FileExtension => ".opt";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles)
    {
        // Use leaveOpen: true to avoid disposing the caller's stream
        await using var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true);

#pragma warning disable S2245
        var random = request.Seed.HasValue ? new Random(request.Seed.Value) : Random.Shared;
#pragma warning restore S2245

        await WriteRowsAsync(writer, request, processedFiles, random);

        // Flush to ensure data is written
        await writer.FlushAsync();
    }

    private static async Task WriteRowsAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles,
        Random random)
    {
        var buffer = new StringBuilder();
        int rowCount = 0;

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;

            // Opticon 7-column format: BatesNumber,Volume,ImagePath,DocBreak,BoxBreak,FolderBreak,PageCount
            string batesNumber = request.BatesConfig != null
                ? GenerateBatesNumber(request, workItem)
                : GenerateDocumentId(workItem);
            string volume = "VOL001";
            string imagePath = $"IMAGES\\{batesNumber}.tif";
            string docBreak = "Y";
            string boxBreak = string.Empty;
            string folderBreak = string.Empty;
            int pageCount = ShouldIncludePageCount(request) ? fileData.PageCount : 1;

            // Comma-separated, no header — Opticon standard
            var line = $"{batesNumber},{volume},{imagePath},{docBreak},{boxBreak},{folderBreak},{pageCount}";

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
