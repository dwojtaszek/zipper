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
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
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
                    await using var writer = new StreamWriter(stream, GetOptEncoding(request), leaveOpen: true);
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
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles)
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

        foreach (var fileData in processedFiles)
        {
            foreach (var (_, line) in BuildOptRowsForFile(fileData, request, isProductionSet: false))
            {
                buffer.AppendLine(line);
                rowCount++;

                if (rowCount >= 1000)
                {
                    await writer.WriteAsync(buffer.ToString());
                    buffer.Clear();
                    rowCount = 0;
                }
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
        var encoding = GetOptEncoding(request);
        var eolString = GetEolString(request.Delimiters.EndOfLine);

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value + 1) : new Random();
#pragma warning restore S2245

        await using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
        long currentLineNumber = 1;

        for (long i = 1; i <= request.Output.FileCount; i++)
        {
            string batesId = request.Bates != null
                ? BatesNumberGenerator.Generate(request.Bates, i - 1)
                : $"IMG{i:D8}";
            string volume = "VOL001";
            string imagePath = $"IMAGES\\{batesId}.tif";
            int pageCount = random.Next(1, 11);

            foreach (var entry in GeneratePageEntries(batesId, imagePath, pageCount))
            {
                string line = $"{entry.Bates},{volume},{entry.ImagePath},{entry.DocBreak},,,{entry.PageCountStr}";

                string interceptedLine = ApplyChaosInterception(chaosEngine, currentLineNumber, line, entry.Bates);
                await writer.WriteAsync(interceptedLine + eolString);

                if (chaosEngine != null)
                {
                    var anomaly = chaosEngine.GetEncodingAnomaly(currentLineNumber, currentLineNumber + 1, encoding);
                    if (anomaly != null)
                    {
                        await writer.FlushAsync();
                        await stream.WriteAsync(anomaly);
                    }
                }

                currentLineNumber++;
            }
        }

        await writer.FlushAsync();
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
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var eol = GetEolString(request.Delimiters.EndOfLine);
        var encoding = GetOptEncoding(request);
        var rows = new List<(long LineNumber, string RecordId, string Line)>();

        int rowIdx = 0;
        foreach (var fileData in processedFiles)
        {
            foreach (var (recordId, line) in BuildOptRowsForFile(fileData, request, isProductionSet: true))
            {
                long lineNumber = rowIdx + 1;
                rows.Add((lineNumber, recordId, line));
                rowIdx++;
            }
        }

        if (chaosEngine == null)
        {
            await using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
            foreach (var row in rows)
            {
                await writer.WriteAsync(row.Line + eol);
            }

            await writer.FlushAsync();
        }
        else
        {
            await WriteRowsWithChaosAsync(stream, encoding, eol, rows, chaosEngine);
        }
    }

    /// <summary>
    /// Builds the OPT rows (parent pages and optional child attachments) for a single file.
    /// </summary>
    /// <param name="fileData">The file data.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="isProductionSet">A value indicating whether this is for production set mode.</param>
    /// <returns>A sequence of tuples containing the record ID and the formatted line.</returns>
    private static IEnumerable<(string RecordId, string Line)> BuildOptRowsForFile(
        FileData fileData,
        FileGenerationRequest request,
        bool isProductionSet)
    {
        var workItem = fileData.WorkItem;
        string baseBatesNumber;
        if (isProductionSet)
        {
            baseBatesNumber = BatesNumberGenerator.Generate(request.Bates!, workItem.Index - 1);
        }
        else if (request.Bates != null)
        {
            baseBatesNumber = GenerateBatesNumber(request, workItem);
        }
        else
        {
            baseBatesNumber = GenerateDocumentId(workItem);
        }

        string volume = isProductionSet ? workItem.FolderName : "VOL001";

        string baseImagePath = isProductionSet
            ? workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
                .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif")
                .Replace(Path.DirectorySeparatorChar, '\\')
            : $"IMAGES\\{baseBatesNumber}.tif";

        int actualPages = request.Tiff.ShouldIncludePageCount(request.Output) ? Math.Max(1, fileData.PageCount) : 1;
        bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;

        // Generate parent pages
        foreach (var entry in GeneratePageEntries(baseBatesNumber, baseImagePath, actualPages))
        {
            var parentLine = $"{entry.Bates},{volume},{entry.ImagePath},{entry.DocBreak},,,{entry.PageCountStr}";
            yield return (entry.Bates, parentLine);
        }

        if (hasAttachment)
        {
            string childBates = $"{baseBatesNumber}_A001";
            string childImagePath = isProductionSet
                ? Path.Combine("IMAGES", volume, $"{childBates}.tif").Replace(Path.DirectorySeparatorChar, '\\')
                : $"IMAGES\\{childBates}.tif";
            var childLine = $"{childBates},{volume},{childImagePath},Y,,,1";
            yield return (childBates, childLine);
        }
    }

    /// <summary>
    /// Generates page-level metadata entries for a multi-page file or single-page file.
    /// </summary>
    /// <param name="baseBates">The base Bates number or Control Number.</param>
    /// <param name="baseImagePath">The base image relative path.</param>
    /// <param name="actualPages">The actual page count of the file.</param>
    /// <returns>A sequence of page-level metadata entries.</returns>
    private static System.Collections.Generic.IEnumerable<(string Bates, string ImagePath, string DocBreak, string PageCountStr)> GeneratePageEntries(
        string baseBates,
        string baseImagePath,
        int actualPages)
    {
        if (actualPages > 1)
        {
            var ext = Path.GetExtension(baseImagePath);
            var pathWithoutExt = baseImagePath.Length >= ext.Length
                ? baseImagePath.Substring(0, baseImagePath.Length - ext.Length)
                : baseImagePath;

            for (int pageIdx = 1; pageIdx <= actualPages; pageIdx++)
            {
                var pageBates = $"{baseBates}_{pageIdx:D3}";
                var pageImagePath = $"{pathWithoutExt}_{pageIdx:D3}{ext}";
                var docBreak = pageIdx == 1 ? "Y" : string.Empty;
                var pageCountStr = pageIdx == 1 ? actualPages.ToString() : string.Empty;

                yield return (pageBates, pageImagePath, docBreak, pageCountStr);
            }
        }
        else
        {
            yield return (baseBates, baseImagePath, "Y", "1");
        }
    }

    /// <summary>
    /// Gets the encoding for the OPT file, defaulting to Windows-1252 (ANSI) if not explicitly specified.
    /// </summary>
    private static Encoding GetOptEncoding(FileGenerationRequest request)
    {
        var resolvedEncoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
        return (request.LoadFile.IsEncodingExplicit || resolvedEncoding != Encoding.UTF8)
            ? resolvedEncoding
            : EncodingHelper.GetEncoding("ANSI") ?? Encoding.UTF8;
    }
}
