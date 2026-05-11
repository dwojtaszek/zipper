using System.Text;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes DAT (Concordance) load files.
/// Supports Standard, Loadfile-Only, and Production Set output modes.
/// </summary>
internal class DatWriter : LoadFileWriterBase
{
    private readonly WriterMode mode;

    internal DatWriter(WriterMode mode = WriterMode.Standard)
    {
        this.mode = mode;
    }

    public override string FormatName => this.mode switch
    {
        WriterMode.LoadfileOnly => "DAT (Metadata)",
        WriterMode.ProductionSet => "Production Set DAT",
        _ => "DAT",
    };

    public override string FileExtension => ".dat";

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
                await WriteStandardAsync(stream, request, processedFiles, chaosEngine);
                break;
        }
    }

    /// <summary>
    /// Writes header and rows to the configured writer. Extracted so internal
    /// callers can drive it with a pre-built StreamWriter when needed.
    /// </summary>
    internal static async Task WriteContentAsync(
        StreamWriter writer,
        FileGenerationRequest request,
        List<FileData> processedFiles)
    {
        // Defensive guards to prevent IndexOutOfRangeException when delimiters are unset
        char colDelim = !string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? request.Delimiters.ColumnDelimiter[0] : '\u0014';
        char quote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? request.Delimiters.QuoteDelimiter[0] : '\u00fe';

        await writer.WriteLineAsync(BuildStandardHeader(request, colDelim, quote));

        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var generator = GetEffectiveProfileGenerator(request, now);

        var buffer = new StringBuilder();
        int rowCount = 0;

        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            var profileValues = generator?.GenerateRow(fileData.WorkItem, fileData);
            var line = BuildStandardRow(fileData, request, colDelim, quote, profileValues);
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

    private static async Task WriteStandardAsync(
        Stream stream,
        FileGenerationRequest request,
        List<FileData> processedFiles,
        ChaosEngine? chaosEngine = null)
    {
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);

        if (chaosEngine == null)
        {
            // Use leaveOpen: true to avoid disposing the caller's stream
            await using var writer = new StreamWriter(stream, encoding, leaveOpen: true);
            await WriteContentAsync(writer, request, processedFiles);
            await writer.FlushAsync();
            return;
        }

        // Chaos path: build rows then delegate to shared WriteRowsWithChaosAsync
        var eolString = GetEolString(request.Delimiters.EndOfLine);
        char colDelim = !string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? request.Delimiters.ColumnDelimiter[0] : '\u0014';
        char quote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? request.Delimiters.QuoteDelimiter[0] : '\u00fe';

        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var generator = GetEffectiveProfileGenerator(request, now);

        var rows = new List<(long LineNumber, string RecordId, string Line)>();
        rows.Add((1, "HEADER", BuildStandardHeader(request, colDelim, quote)));

        int rowIdx = 0;
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            long lineNumber = rowIdx + 2;
            var recordId = $"DOC{fileData.WorkItem.Index:D8}";
            var profileValues = generator?.GenerateRow(fileData.WorkItem, fileData);
            var line = BuildStandardRow(fileData, request, colDelim, quote, profileValues);
            rows.Add((lineNumber, recordId, line));
            rowIdx++;
        }

        await WriteRowsWithChaosAsync(stream, encoding, eolString, rows, chaosEngine);
    }

    private static async Task WriteLoadfileOnlyAsync(
        Stream stream,
        FileGenerationRequest request,
        ChaosEngine? chaosEngine)
    {
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
        var eolString = GetEolString(request.Delimiters.EndOfLine);
        char colDelim = !string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? request.Delimiters.ColumnDelimiter[0] : '\u0014';
        char quote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? request.Delimiters.QuoteDelimiter[0] : '\u00fe';
        bool hasQuote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter);

        using var memStream = new MemoryStream();

        // Build header
        var header = BuildLoadfileOnlyHeader(colDelim, quote, hasQuote);

        // Apply chaos to header (line 1)
        header = ApplyChaosInterception(chaosEngine, 1, header, "HEADER");

        var headerBytes = encoding.GetBytes(header + eolString);
        await memStream.WriteAsync(headerBytes);

        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value + 1) : new Random();
#pragma warning restore S2245

        var buffer = new StringBuilder();

        for (long i = 1; i <= request.Output.FileCount; i++)
        {
            long lineNumber = i + 1;
            string recordId = $"DOC{i:D8}";

            var line = BuildLoadfileOnlyRow(i, recordId, colDelim, quote, hasQuote, now, random);

            line = ApplyChaosInterception(chaosEngine, lineNumber, line, recordId);

            buffer.Append(line);
            buffer.Append(eolString);

            // Handle encoding anomalies between lines
            if (chaosEngine != null && i < request.Output.FileCount)
            {
                // Flush current buffer first
                var bufferedBytes = encoding.GetBytes(buffer.ToString());
                await memStream.WriteAsync(bufferedBytes);
                buffer.Clear();

                await WriteEncodingAnomalyBytesAsync(memStream, chaosEngine, lineNumber, lineNumber + 1, encoding);
            }

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
        var col = string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? "\u0014" : request.Delimiters.ColumnDelimiter;
        var quote = string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? "\u00fe" : request.Delimiters.QuoteDelimiter;
        var eol = GetEolString(request.Delimiters.EndOfLine);
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);

        var headers = new[] { "DOCID", "BATES_NUMBER", "VOLUME", "NATIVE_PATH", "TEXT_PATH", "IMAGE_PATH", "CUSTODIAN", "DATE_CREATED", "FILE_SIZE", "FILE_TYPE" };
        var headerLine = string.Join(col, headers.Select(h => $"{quote}{h}{quote}"));

        if (chaosEngine == null)
        {
            await using var writer = CreateWriter(stream, request);
            await writer.WriteAsync(headerLine + eol);

            foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
            {
                var dataRow = BuildProductionSetRow(fileData, request, col, quote);
                await writer.WriteAsync(dataRow + eol);
            }

            await writer.FlushAsync();
            return;
        }

        // Chaos path: build rows then delegate to shared WriteRowsWithChaosAsync
        var rows = new List<(long LineNumber, string RecordId, string Line)>();
        rows.Add((1, "HEADER", headerLine));

        int rowIdx = 0;
        foreach (var fileData in processedFiles.OrderBy(f => f.WorkItem.Index))
        {
            long lineNumber = rowIdx + 2;
            var workItem = fileData.WorkItem;
            var batesNum = BatesNumberGenerator.Generate(request.Bates!, workItem.Index - 1);

            var dataRow = BuildProductionSetRow(fileData, request, col, quote);
            rows.Add((lineNumber, batesNum, dataRow));
            rowIdx++;
        }

        await WriteRowsWithChaosAsync(stream, encoding, eol, rows, chaosEngine);
    }

    private static string BuildProductionSetRow(FileData fileData, FileGenerationRequest request, string col, string quote)
    {
        var workItem = fileData.WorkItem;
        var batesNumber = BatesNumberGenerator.Generate(request.Bates!, workItem.Index - 1);
        var imagePath = workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
            .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif");

        var nativePath = workItem.FilePathInZip.Replace(Path.DirectorySeparatorChar, '\\');
        var textPath = nativePath.Replace($".{request.Output.FileType}", ".txt");
        var imagesPath = imagePath.Replace(Path.DirectorySeparatorChar, '\\');

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value + (int)workItem.Index) : Random.Shared;
#pragma warning restore S2245
        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var maxCustodians = Math.Max(2, request.Metadata.CustodianCountOverride ?? 10);
        var custodianProd = $"Custodian {random.Next(1, maxCustodians + 1)}";
        var dateCreated = now.AddDays(-random.Next(1, 730)).ToString("yyyy-MM-dd");

        var fields = new[]
        {
            batesNumber,
            batesNumber,
            workItem.FolderName,
            nativePath,
            textPath,
            imagesPath,
            custodianProd,
            dateCreated,
            fileData.DataLength.ToString(),
            request.Output.FileType.ToUpperInvariant(),
        };

        return string.Join(col, fields.Select(f => $"{quote}{EscapeDatField(f, quote[0], request.Delimiters.NewlineDelimiter)}{quote}"));
    }

    private static Zipper.Profiles.DataGenerator? GetEffectiveProfileGenerator(FileGenerationRequest request, DateTime now)
    {
        var profile = request.Metadata.ColumnProfile;
        if (profile == null && request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            profile = request.Output.IsEml
                ? Zipper.Profiles.BuiltInProfiles.LegacyEml
                : Zipper.Profiles.BuiltInProfiles.LegacyWithMetadata;
        }

        return profile != null ? new Zipper.Profiles.DataGenerator(profile, request.Metadata.Seed, now) : null;
    }

    private static string BuildStandardHeader(FileGenerationRequest request, char colDelim, char quote)
    {
        var sb = new StringBuilder();
        sb.Append($"{quote}Control Number{quote}{colDelim}{quote}File Path{quote}");

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            sb.Append($"{colDelim}{quote}Custodian{quote}{colDelim}{quote}Date Sent{quote}{colDelim}{quote}Author{quote}{colDelim}{quote}File Size{quote}");
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            sb.Append($"{colDelim}{quote}To{quote}{colDelim}{quote}From{quote}{colDelim}{quote}Subject{quote}{colDelim}{quote}Sent Date{quote}{colDelim}{quote}Attachment{quote}");
        }

        if (request.Bates != null)
        {
            sb.Append($"{colDelim}{quote}Bates Number{quote}");
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            sb.Append($"{colDelim}{quote}Page Count{quote}");
        }

        if (request.Output.WithText)
        {
            sb.Append($"{colDelim}{quote}Extracted Text{quote}");
        }

        return sb.ToString();
    }

    private static string BuildStandardRow(
        FileData fileData,
        FileGenerationRequest request,
        char colDelim,
        char quote,
        System.Collections.Generic.Dictionary<string, string>? profileValues)
    {
        var workItem = fileData.WorkItem;
        var docId = EscapeDatField($"DOC{workItem.Index:D8}", quote, request.Delimiters.NewlineDelimiter);

        var sb = new StringBuilder();
        sb.Append($"{quote}{docId}{quote}{colDelim}{quote}{EscapeDatField(workItem.FilePathInZip, quote, request.Delimiters.NewlineDelimiter)}{quote}");

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            var custodian = EscapeDatField(profileValues?.GetValueOrDefault("CUSTODIAN") ?? string.Empty, quote, request.Delimiters.NewlineDelimiter);
            var dateSent = profileValues?.GetValueOrDefault("DATESENT") ?? string.Empty;
            var author = EscapeDatField(profileValues?.GetValueOrDefault("AUTHOR") ?? string.Empty, quote, request.Delimiters.NewlineDelimiter);
            var fileSize = profileValues?.GetValueOrDefault("FILESIZE") ?? fileData.DataLength.ToString();
            sb.Append($"{colDelim}{quote}{custodian}{quote}{colDelim}{quote}{dateSent}{quote}{colDelim}{quote}{author}{quote}{colDelim}{quote}{fileSize}{quote}");
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            var to = EscapeDatField(profileValues?.GetValueOrDefault("EMAILTO") ?? $"recipient{workItem.Index}@example.com", quote, request.Delimiters.NewlineDelimiter);
            var from = EscapeDatField(profileValues?.GetValueOrDefault("EMAILFROM") ?? $"sender{workItem.Index}@example.com", quote, request.Delimiters.NewlineDelimiter);
            var subject = EscapeDatField(profileValues?.GetValueOrDefault("EMAILSUBJECT") ?? $"Email Subject {workItem.Index}", quote, request.Delimiters.NewlineDelimiter);
            var sentDate = profileValues?.GetValueOrDefault("EMAILSENTDATE") ?? string.Empty;
            var attachment = EscapeDatField(profileValues?.GetValueOrDefault("EMAILATTACHMENT") ?? string.Empty, quote, request.Delimiters.NewlineDelimiter);
            sb.Append($"{colDelim}{quote}{to}{quote}{colDelim}{quote}{from}{quote}{colDelim}{quote}{subject}{quote}{colDelim}{quote}{sentDate}{quote}{colDelim}{quote}{attachment}{quote}");
        }

        if (request.Bates != null)
        {
            sb.Append($"{colDelim}{quote}{BatesNumberGenerator.Generate(request.Bates, workItem.Index - 1)}{quote}");
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            sb.Append($"{colDelim}{quote}{fileData.PageCount}{quote}");
        }

        if (request.Output.WithText)
        {
            var sourceSuffix = $".{request.Output.FileType}";
            var textPath = workItem.FilePathInZip.EndsWith(sourceSuffix, StringComparison.OrdinalIgnoreCase)
                ? workItem.FilePathInZip[..^sourceSuffix.Length] + ".txt"
                : workItem.FilePathInZip;
            sb.Append($"{colDelim}{quote}{textPath}{quote}");
        }

        return sb.ToString();
    }

    private static string BuildLoadfileOnlyHeader(
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

    private static string BuildLoadfileOnlyRow(
        long index,
        string recordId,
        char colDelim,
        char quote,
        bool hasQuote,
        DateTime now,
        Random random)
    {
        var sb = new StringBuilder();
        var custodian = $"Custodian {(index % 10) + 1}";
        var dateSent = now.AddDays(-random.Next(1, 365)).ToString("yyyy-MM-dd");
        var author = $"Author {random.Next(1, 100):D3}";
        var fileSize = random.Next(1024, 10485760).ToString();
        var emailSubject = $"Email Subject {index}";
        var emailFrom = $"sender{index}@example.com";
        var emailTo = $"recipient{index}@example.com";
        var emailSentDate = now.AddDays(-random.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss");
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
        AppendField(sb, emailSubject, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, emailFrom, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, emailTo, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, emailSentDate, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, extractedText, quote, hasQuote);

        return sb.ToString();
    }
}
