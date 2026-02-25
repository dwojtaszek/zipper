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
        var stopwatch = Stopwatch.StartNew();

        Directory.CreateDirectory(request.OutputPath);

        var baseFileName = $"loadfile_{DateTime.Now:yyyyMMdd_HHmmss}";
        var extension = request.LoadFileFormat == LoadFileFormat.Opt ? ".opt" : ".dat";
        var loadFilePath = Path.Combine(request.OutputPath, $"{baseFileName}{extension}");

        var encoding = EncodingHelper.GetEncodingOrDefault(request.Encoding);
        var eolString = GetEolString(request.EndOfLine);
        int totalLines = (int)request.FileCount + 1; // +1 for header

        // Initialize chaos engine if enabled
        ChaosEngine? chaosEngine = null;
        if (request.ChaosMode)
        {
            chaosEngine = new ChaosEngine(
                totalLines,
                request.ChaosAmount,
                request.ChaosTypes,
                request.LoadFileFormat,
                request.ColumnDelimiter,
                request.QuoteDelimiter,
                request.Seed);
        }

        var random = request.Seed.HasValue ? new Random(request.Seed.Value + 1) : new Random();

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
        var propertiesPath = await LoadfileAuditWriter.WriteAsync(
            loadFilePath,
            request,
            request.FileCount,
            chaosEngine?.Anomalies);

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
        var buffer = new StringBuilder();
        int batchSize = 1000;

        for (long i = 1; i <= request.FileCount; i++)
        {
            int lineNumber = (int)i + 1; // Line 1 is header, data starts at line 2
            string recordId = $"DOC{i:D8}";

            var line = BuildDatRow(i, recordId, request, colDelim, quote, hasQuote, random, now);

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
        // BatesID,Volume,ImagePath,DocBreak(Y/blank),BoxBreak,FolderBreak,PageCount
        var now = request.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var buffer = new StringBuilder();
        long pageCounter = 1;

        for (long i = 1; i <= request.FileCount; i++)
        {
            int lineNumber = (int)i; // No header in Opticon format
            string batesId = $"IMG{i:D8}";
            string volume = "VOL001";
            string imagePath = $"IMAGES\\{batesId}.tif";
            string docBreak = "Y"; // First page of each document
            string boxBreak = string.Empty;
            string folderBreak = string.Empty;
            int pageCount = random.Next(1, 11);

            string line = $"{batesId},{volume},{imagePath},{docBreak},{boxBreak},{folderBreak},{pageCount}";

            // Apply chaos if targeted
            if (chaos != null && chaos.ShouldIntercept(lineNumber))
            {
                string recordId = batesId;
                line = chaos.Intercept(lineNumber, line, recordId);
            }

            buffer.Append(line);
            buffer.Append(eol);
            pageCounter += pageCount;

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
        AppendField(sb, "Control Number", quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, "File Path", quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, "Custodian", quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, "Date Sent", quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, "Author", quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, "File Size", quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, "EmailSubject", quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, "EmailFrom", quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, "EmailTo", quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, "EmailSentDate", quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, "ExtractedText", quote, hasQuote);

        return sb.ToString();
    }

    private static string BuildDatRow(
        long index,
        string recordId,
        FileGenerationRequest request,
        char colDelim,
        char quote,
        bool hasQuote,
        Random random,
        DateTime now)
    {
        var sb = new StringBuilder();
        var custodian = $"Custodian {(index % 10) + 1}";
        var dateSent = now.AddDays(-random.Next(1, 365)).ToString("yyyy-MM-dd");
        var author = $"Author {random.Next(1, 100):D3}";
        var fileSize = random.Next(1024, 10485760).ToString();
        var subject = $"RE: Document Review - Batch {random.Next(1, 500)}";
        var from = $"user{random.Next(1, 200):D3}@example.com";
        var to = $"recipient{random.Next(1, 200):D3}@example.com";
        var sentDate = now.AddDays(-random.Next(1, 365)).ToString("yyyy-MM-dd HH:mm:ss");
        var filePath = $"NATIVES\\{(index % 50) + 1:D3}\\{recordId}.pdf";
        var extractedText = $"Sample extracted text content for document {recordId}.";

        AppendField(sb, recordId, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, filePath, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, custodian, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, dateSent, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, author, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, fileSize, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, subject, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, from, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, to, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, sentDate, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, extractedText, quote, hasQuote);

        return sb.ToString();
    }

    private static void AppendField(StringBuilder sb, string value, char quote, bool hasQuote)
    {
        if (hasQuote)
        {
            sb.Append(quote);
            sb.Append(value);
            sb.Append(quote);
        }
        else
        {
            sb.Append(value);
        }
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
