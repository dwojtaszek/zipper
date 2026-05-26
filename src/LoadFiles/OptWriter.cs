using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes OPT (Opticon) format load files.
/// Supports Standard, Loadfile-Only, and Production Set output modes.
/// </summary>
internal class OptWriter : LoadFileWriterBase
{
    /// <summary>
    /// The writer mode.
    /// </summary>
    private readonly WriterMode mode;

    /// <summary>
    /// Initializes a new instance of the <see cref="OptWriter"/> class with the specified mode.
    /// </summary>
    /// <param name="mode">The writer mode to use.</param>
    internal OptWriter(WriterMode mode = WriterMode.Standard)
    {
        this.mode = mode;
    }

    /// <summary>
    /// Gets the name of the load file format.
    /// </summary>
    public override string FormatName => this.mode switch
    {
        WriterMode.LoadfileOnly => "OPT (Image)",
        WriterMode.ProductionSet => "Production Set OPT",
        _ => "OPT",
    };

    /// <summary>
    /// Gets the standard file extension for the load file, including the leading dot.
    /// </summary>
    public override string FileExtension => ".opt";

    /// <summary>
    /// Writes the OPT load file to the specified stream based on the current writer mode.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="request">The file generation request containing settings.</param>
    /// <param name="processedFiles">The list of file data processed during generation.</param>
    /// <param name="chaosEngine">The optional chaos engine to introduce synthetic errors.</param>
    /// <returns>A task representing the asynchronous write operation.</returns>
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

    /// <summary>
    /// Writes the load file using the standard mode.
    /// </summary>
    /// <param name="writer">The output stream writer.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="processedFiles">The processed files data.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
            bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;
            var (batesNumber, volume, imagePath, pageCount) = GetRowData(fileData, request, isProductionSet: false);

            var line = BuildOptRow(batesNumber, volume, imagePath, pageCount);
            buffer.AppendLine(line);
            rowCount++;

            if (hasAttachment)
            {
                string childBates = $"{batesNumber}_A001";
                string childImagePath = $"IMAGES\\{childBates}.tif";
                var childLine = BuildOptRow(childBates, volume, childImagePath, 1);
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

    /// <summary>
    /// Writes the load file using the loadfile-only mode.
    /// </summary>
    /// <param name="stream">The output stream.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="chaosEngine">The optional chaos engine.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Writes the load file using the production set mode.
    /// </summary>
    /// <param name="stream">The output stream.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="processedFiles">The processed files data.</param>
    /// <param name="chaosEngine">The optional chaos engine.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
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
                bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;
                var (batesNumber, volume, imagePath, pageCount) = GetRowData(fileData, request, isProductionSet: true);

                var line = BuildOptRow(batesNumber, volume, imagePath, pageCount);
                await writer.WriteAsync(line + eol);

                if (hasAttachment)
                {
                    string childBates = $"{batesNumber}_A001";
                    string childImagePath = Path.Combine("IMAGES", volume, $"{childBates}.tif").Replace(Path.DirectorySeparatorChar, '\\');
                    var childLine = BuildOptRow(childBates, volume, childImagePath, 1);
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
            bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;
            var (batesNum, volume, imagePath, pageCount) = GetRowData(fileData, request, isProductionSet: true);

            var optLine = BuildOptRow(batesNum, volume, imagePath, pageCount);
            rows.Add((currentLineNumber++, batesNum, optLine));

            if (hasAttachment)
            {
                string childBates = $"{batesNum}_A001";
                string childImagePath = Path.Combine("IMAGES", volume, $"{childBates}.tif").Replace(Path.DirectorySeparatorChar, '\\');
                var childLine = BuildOptRow(childBates, volume, childImagePath, 1);
                rows.Add((currentLineNumber++, childBates, childLine));
            }
        }

        await WriteRowsWithChaosAsync(stream, encoding, eol, rows, chaosEngine);
    }

    /// <summary>
    /// Gets the metadata row data elements for a given file.
    /// </summary>
    private static (string BatesNumber, string Volume, string ImagePath, int PageCount) GetRowData(
        FileData fileData,
        FileGenerationRequest request,
        bool isProductionSet)
    {
        var workItem = fileData.WorkItem;
        string batesNumber = isProductionSet
            ? BatesNumberGenerator.Generate(request.Bates!, workItem.Index - 1)
            : (request.Bates != null ? GenerateBatesNumber(request, workItem) : GenerateDocumentId(workItem));

        string volume = isProductionSet ? workItem.FolderName : "VOL001";

        string imagePath = isProductionSet
            ? workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
                .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif")
                .Replace(Path.DirectorySeparatorChar, '\\')
            : $"IMAGES\\{batesNumber}.tif";

        int pageCount = !isProductionSet && request.Tiff.ShouldIncludePageCount(request.Output) ? fileData.PageCount : 1;

        return (batesNumber, volume, imagePath, pageCount);
    }

    /// <summary>
    /// Builds a single OPT row formatted string.
    /// </summary>
    private static string BuildOptRow(string batesNumber, string volume, string imagePath, int pageCount)
    {
        return $"{batesNumber},{volume},{imagePath},Y,,,{pageCount}";
    }
}
