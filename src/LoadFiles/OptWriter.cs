using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes OPT (Opticon) format load files.
/// Supports Standard, Loadfile-Only, and Production Set output modes.
/// </summary>
internal class OptWriter : LoadFileWriterBase
{
    private readonly WriterMode mode;

    internal OptWriter(WriterMode mode = WriterMode.Standard)
    {
        this.mode = mode;
    }

    public override string FormatName => this.mode switch
    {
        WriterMode.LoadfileOnly => "OPT (Image)",
        WriterMode.ProductionSet => "Production Set OPT",
        _ => "OPT",
    };

    public override string FileExtension => ".opt";

    public override async Task WriteAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        switch (this.mode)
        {
            case WriterMode.LoadfileOnly:
                await WriteLoadfileOnlyAsync(stream, request, chaosEngine);
                break;
            case WriterMode.ProductionSet:
                await WriteProductionSetAsync(stream, request, processedFiles, chaosEngine);
                break;
            default:
                {
                    // Use leaveOpen: true to avoid disposing the caller's stream
                    await using var writer = new StreamWriter(stream, EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding), leaveOpen: true);
                    await WriteStandardRowsAsync(writer, request, processedFiles);

                    // Flush to ensure data is written
                    await writer.FlushAsync();
                    break;
                }
        }
    }

    private static async Task WriteStandardRowsAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        System.Collections.Generic.List<FileData> processedFiles)
    {
        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            Console.Error.WriteLine("Warning: --with-metadata columns are not supported in Opticon format. The OPT file uses the standard 7-column layout.");
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
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
            bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;

            // Opticon 7-column format: BatesNumber,Volume,ImagePath,DocBreak,FolderBreak,BoxBreak,PageCount
            string batesNumber = request.Bates != null
                ? GenerateBatesNumber(request, workItem)
                : GenerateDocumentId(workItem);
            string volume = "VOL001";
            string imagePath = $"IMAGES\\{batesNumber}.tif";
            string docBreak = "Y";
            string folderBreak = string.Empty;
            string boxBreak = string.Empty;
            int pageCount = request.Tiff.ShouldIncludePageCount(request.Output) ? fileData.PageCount : 1;

            // Comma-separated, no header — Opticon standard
            var line = $"{batesNumber},{volume},{imagePath},{docBreak},{folderBreak},{boxBreak},{pageCount}";

            buffer.AppendLine(line);
            rowCount++;

            if (hasAttachment)
            {
                string childBates = $"{batesNumber}_A001";
                string childImagePath = $"IMAGES\\{childBates}.tif";
                var childLine = $"{childBates},{volume},{childImagePath},Y,,,{1}";
                buffer.AppendLine(childLine);
                rowCount++;
            }

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

    private static async Task WriteLoadfileOnlyAsync(
        Stream stream,
        FileGenerationRequest request,
        ChaosEngine? chaosEngine)
    {
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
        var eolString = GetEolString(request.Delimiters.EndOfLine);

        using var memStream = new MemoryStream();
        var preamble = encoding.GetPreamble();
        if (preamble.Length > 0)
        {
            await memStream.WriteAsync(preamble, 0, preamble.Length);
        }

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value + 1) : new Random();
#pragma warning restore S2245

        var buffer = new StringBuilder();

        for (long i = 1; i <= request.Output.FileCount; i++)
        {
            long lineNumber = i;
            string batesId = $"IMG{i:D8}";
            string volume = "VOL001";
            string imagePath = $"IMAGES\\{batesId}.tif";
            string docBreak = "Y";
            string folderBreak = string.Empty;
            string boxBreak = string.Empty;
            int pageCount = random.Next(1, 11);

            string line = $"{batesId},{volume},{imagePath},{docBreak},{folderBreak},{boxBreak},{pageCount}";

            line = ApplyChaosInterception(chaosEngine, lineNumber, line, batesId);

            buffer.Append(line);
            buffer.Append(eolString);

            if (buffer.Length > 1000 * 200)
            {
                var batchBytes = encoding.GetBytes(buffer.ToString());
                await memStream.WriteAsync(batchBytes);
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            var remainingBytes = encoding.GetBytes(buffer.ToString());
            await memStream.WriteAsync(remainingBytes);
        }

        memStream.Position = 0;
        await memStream.CopyToAsync(stream);
    }

    private static async Task WriteProductionSetAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var eol = GetEolString(request.Delimiters.EndOfLine);

        if (chaosEngine == null)
        {
            await using var writer = CreateWriter(stream, request);

            foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
            {
                var workItem = fileData.WorkItem;
                var batesNumber = BatesNumberGenerator.Generate(request.Bates!, workItem.Index - 1);
                var imagePath = workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
                    .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif")
                    .Replace(Path.DirectorySeparatorChar, '\\');

                var docBreak = "Y";
                var line = $"{batesNumber},{workItem.FolderName},{imagePath},{docBreak},,,1";

                await writer.WriteAsync(line + eol);

                bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;
                if (hasAttachment)
                {
                    var childLine = BuildProductionSetChildRow(batesNumber, workItem.FolderName);
                    await writer.WriteAsync(childLine + eol);
                }
            }

            await writer.FlushAsync();
            return;
        }

        // Chaos path: build rows then delegate to shared WriteRowsWithChaosAsync
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
        var rows = new List<(long LineNumber, string RecordId, string Line)>();

        long currentLineNumber = 1;
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var workItem = fileData.WorkItem;
            var batesNum = BatesNumberGenerator.Generate(request.Bates!, workItem.Index - 1);
            var imgPath = workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
                .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif")
                .Replace(Path.DirectorySeparatorChar, '\\');

            var optLine = $"{batesNum},{workItem.FolderName},{imgPath},Y,,,1";
            rows.Add((currentLineNumber++, batesNum, optLine));

            bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;
            if (hasAttachment)
            {
                var childLine = BuildProductionSetChildRow(batesNum, workItem.FolderName);
                rows.Add((currentLineNumber++, $"{batesNum}_A001", childLine));
            }
        }

        await WriteRowsWithChaosAsync(stream, encoding, eol, rows, chaosEngine);
    }

    private static string BuildProductionSetChildRow(string batesNumber, string folderName)
    {
        var childBates = $"{batesNumber}_A001";
        var childImagePath = Path.Combine("IMAGES", folderName, $"{childBates}.tif").Replace(Path.DirectorySeparatorChar, '\\');
        return $"{childBates},{folderName},{childImagePath},Y,,,1";
    }
}
