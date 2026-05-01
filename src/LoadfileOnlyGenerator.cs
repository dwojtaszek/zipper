using System.Diagnostics;
using System.Text;

namespace Zipper;

/// <summary>
/// Generates standalone load files (DAT or OPT) without creating native files or ZIP archives.
/// Used when --loadfile-only flag is specified.
/// </summary>
internal static class LoadfileOnlyGenerator
{
    /// <summary>
    /// Generates a standalone load file and its companion properties JSON.
    /// </summary>
    /// <param name="request">File generation request with loadfile-only settings.</param>
    /// <returns>Result containing generated file paths and performance metrics.</returns>
    public static async Task<LoadfileOnlyResult> GenerateAsync(FileGenerationRequest request)
    {
        // Clone to avoid mutating the caller's request object
        request = request.Clone();

        var stopwatch = Stopwatch.StartNew();

        Directory.CreateDirectory(request.OutputPath);

        var baseFileName = $"loadfile_{DateTime.Now:yyyyMMdd_HHmmss}";
        var extension = request.LoadFileFormat == LoadFileFormat.Opt ? ".opt" : ".dat";
        var loadFilePath = Path.Combine(request.OutputPath, $"{baseFileName}{extension}");

        var encoding = EncodingHelper.GetEncodingOrDefault(request.Encoding);
        var eolString = GetEolString(request.EndOfLine);
        long totalLines = request.LoadFileFormat == LoadFileFormat.Opt
            ? request.FileCount // OPT has no header
            : (long)request.FileCount + 1; // DAT: +1 for header; cast to long to avoid overflow

        // Initialize chaos engine if enabled
        ChaosEngine? chaosEngine = null;
        if (request.ChaosMode)
        {
            // Resolve chaos scenario to types and amount
            string? resolvedTypes = request.ChaosTypes;
            string? resolvedAmount = request.ChaosAmount;

            if (!string.IsNullOrEmpty(request.ChaosScenario))
            {
                var scenario = ChaosScenarios.GetByName(request.ChaosScenario);
                if (scenario != null)
                {
                    // Scenario types replace manual types; empty string means "all types"
                    resolvedTypes = string.IsNullOrEmpty(scenario.ChaosTypes) ? null : scenario.ChaosTypes;

                    // Use scenario default amount unless user explicitly set one
                    if (string.IsNullOrEmpty(resolvedAmount))
                    {
                        resolvedAmount = scenario.DefaultAmount;
                    }

                    // Persist resolved values back to request for accurate audit metadata
                    request.ChaosAmount = resolvedAmount;
                    request.ChaosTypes = resolvedTypes;

                    Console.WriteLine(string.Format("  Chaos Scenario: {0} ({1})", scenario.Name, scenario.Description));
                }
            }

            string chaosColDelim = request.LoadFileFormat == LoadFileFormat.Opt ? "," : request.ColumnDelimiter;
            string chaosQuoteDelim = request.LoadFileFormat == LoadFileFormat.Opt ? string.Empty : request.QuoteDelimiter;

            chaosEngine = new ChaosEngine(
                totalLines,
                resolvedAmount,
                resolvedTypes,
                request.LoadFileFormat,
                chaosColDelim,
                chaosQuoteDelim,
                eolString,
                request.Seed);
        }

#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        var random = request.Seed.HasValue ? new Random(request.Seed.Value + 1) : new Random();
#pragma warning restore S2245

        // Write the load file
        await using (var fileStream = new FileStream(loadFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            if (request.LoadFileFormat == LoadFileFormat.Opt)
            {
                await WriteOptFile(fileStream, request, encoding, eolString, chaosEngine, random);
            }
            else
            {
                await WriteDatFile(fileStream, request, encoding, eolString, chaosEngine, random);
            }
        }

        // Write the companion properties JSON
        string propertiesPath;
        try
        {
            propertiesPath = await LoadfileAuditWriter.WriteAsync(
                loadFilePath,
                request,
                request.FileCount,
                chaosEngine?.Anomalies);
        }
        catch
        {
            // Clean up partial output on failure
            if (File.Exists(loadFilePath))
            {
                File.Delete(loadFilePath);
            }

            throw;
        }

        stopwatch.Stop();

        return new LoadfileOnlyResult
        {
            LoadFilePath = loadFilePath,
            PropertiesFilePath = propertiesPath,
            TotalRecords = request.FileCount,
            GenerationTime = stopwatch.Elapsed,
        };
    }

    private static async Task WriteDatFile(
        FileStream stream,
        FileGenerationRequest request,
        Encoding encoding,
        string eol,
        ChaosEngine? chaos,
        Random random)
    {
        char colDelim = !string.IsNullOrEmpty(request.ColumnDelimiter) ? request.ColumnDelimiter[0] : '\u0014';
        char quote = !string.IsNullOrEmpty(request.QuoteDelimiter) ? request.QuoteDelimiter[0] : '\u00fe';
        bool hasQuote = !string.IsNullOrEmpty(request.QuoteDelimiter);

        // Build header
        var header = BuildDatHeader(request, colDelim, quote, hasQuote);

        // Apply chaos to header (line 1)
        if (chaos != null && chaos.ShouldIntercept(1))
        {
            header = chaos.Intercept(1, header, "HEADER");
        }

        var headerBytes = encoding.GetBytes(header + eol);
        await stream.WriteAsync(headerBytes);

        var now = request.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var builder = new MetadataRowBuilder(request, random, now);
        var buffer = new StringBuilder();
        int batchSize = 1000;

        for (long i = 1; i <= request.FileCount; i++)
        {
            long lineNumber = i + 1; // Line 1 is header, data starts at line 2
            string recordId = builder.GetControlNumber(i);

            var line = BuildDatRow(i, recordId, request, colDelim, quote, hasQuote, builder);

            // Apply chaos if targeted
            if (chaos != null && chaos.ShouldIntercept(lineNumber))
            {
                line = chaos.Intercept(lineNumber, line, recordId);
            }

            buffer.Append(line);
            buffer.Append(eol);

            // Handle encoding anomalies between lines
            if (chaos != null && i < request.FileCount)
            {
                var encodingAnomaly = chaos.GetEncodingAnomaly(lineNumber, lineNumber + 1, encoding);
                if (encodingAnomaly != null)
                {
                    // Flush current buffer first
                    var bufferedBytes = encoding.GetBytes(buffer.ToString());
                    await stream.WriteAsync(bufferedBytes);
                    buffer.Clear();

                    // Write invalid bytes directly
                    await stream.WriteAsync(encodingAnomaly);
                }
            }

            if (buffer.Length > batchSize * 200)
            {
                var batchBytes = encoding.GetBytes(buffer.ToString());
                await stream.WriteAsync(batchBytes);
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            var remainingBytes = encoding.GetBytes(buffer.ToString());
            await stream.WriteAsync(remainingBytes);
        }
    }

    private static async Task WriteOptFile(
        FileStream stream,
        FileGenerationRequest request,
        Encoding encoding,
        string eol,
        ChaosEngine? chaos,
        Random random)
    {
        // Opticon 7-column comma-separated format:
        // BatesNumber,Volume,ImagePath,DocBreak(Y/blank),FolderBreak,BoxBreak,PageCount
        var now = request.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var buffer = new StringBuilder();

        for (long i = 1; i <= request.FileCount; i++)
        {
            long lineNumber = i; // No header in Opticon format
            string batesId = $"IMG{i:D8}";
            string volume = "VOL001";
            string imagePath = $"IMAGES\\{batesId}.tif";
            string docBreak = "Y"; // First page of each document
            string folderBreak = string.Empty;
            string boxBreak = string.Empty;
            int pageCount = random.Next(1, 11);

            string line = $"{batesId},{volume},{imagePath},{docBreak},{folderBreak},{boxBreak},{pageCount}";

            // Apply chaos if targeted
            if (chaos != null && chaos.ShouldIntercept(lineNumber))
            {
                string recordId = batesId;
                line = chaos.Intercept(lineNumber, line, recordId);
            }

            buffer.Append(line);
            buffer.Append(eol);

            if (buffer.Length > 1000 * 200)
            {
                var batchBytes = encoding.GetBytes(buffer.ToString());
                await stream.WriteAsync(batchBytes);
                buffer.Clear();
            }
        }

        if (buffer.Length > 0)
        {
            var remainingBytes = encoding.GetBytes(buffer.ToString());
            await stream.WriteAsync(remainingBytes);
        }
    }

    private static string BuildDatHeader(
        FileGenerationRequest request,
        char colDelim,
        char quote,
        bool hasQuote)
    {
        var sb = new StringBuilder();
        MetadataRowBuilder.AppendField(sb, "Control Number", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "File Path", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "Custodian", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "Date Sent", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "Author", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "File Size", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "EmailSubject", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "EmailFrom", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "EmailTo", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "EmailSentDate", quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, "ExtractedText", quote, hasQuote);

        return sb.ToString();
    }

    private static string BuildDatRow(
        long index,
        string recordId,
        FileGenerationRequest request,
        char colDelim,
        char quote,
        bool hasQuote,
        MetadataRowBuilder builder)
    {
        var workItem = new FileWorkItem { Index = index };
        var dummyFileData = new FileData { WorkItem = workItem };
        var sb = new StringBuilder();
        var custodian = builder.GetCustodianByIndex(index);
        var dateSent = builder.GetDateSent();
        var author = builder.GetAuthor();
        var fileSize = builder.GetFileSize();
        var filePath = $"NATIVES\\{(index % 50) + 1:D3}\\{recordId}.pdf";
        var extractedText = $"Sample extracted text content for document {recordId}.";

        MetadataRowBuilder.AppendField(sb, recordId, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, filePath, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, custodian, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, dateSent, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, author, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, fileSize, quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, builder.GetEmailSubject(workItem, dummyFileData), quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, builder.GetEmailFrom(workItem, dummyFileData), quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, builder.GetEmailTo(workItem, dummyFileData), quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, builder.GetEmailSentDate(workItem, dummyFileData), quote, hasQuote);
        sb.Append(colDelim);
        MetadataRowBuilder.AppendField(sb, extractedText, quote, hasQuote);

        return sb.ToString();
    }

    private static string GetEolString(string eol)
    {
        return eol?.ToUpperInvariant() switch
        {
            "LF" => "\n",
            "CR" => "\r",
            _ => "\r\n", // CRLF default
        };
    }
}

/// <summary>
/// Result of a loadfile-only generation operation.
/// </summary>
internal class LoadfileOnlyResult
{
    /// <summary>
    /// Gets or sets the path to the generated load file.
    /// </summary>
    public string LoadFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path to the properties JSON file.
    /// </summary>
    public string PropertiesFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total records written.
    /// </summary>
    public long TotalRecords { get; set; }

    /// <summary>
    /// Gets or sets the generation time.
    /// </summary>
    public TimeSpan GenerationTime { get; set; }
}
