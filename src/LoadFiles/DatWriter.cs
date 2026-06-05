using System.Text;
using Zipper.Utils;

namespace Zipper.LoadFiles;

/// <summary>
/// Writes DAT (Concordance) load files.
/// Supports Standard, Loadfile-Only, and Production Set output modes.
/// </summary>
internal class DatWriter : LoadFileWriterBase
{
    /// <summary>
    /// The writer mode (e.g. Standard, LoadfileOnly, ProductionSet).
    /// </summary>
    private readonly WriterMode mode;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatWriter"/> class with the specified mode.
    /// </summary>
    /// <param name="mode">The writer mode to use.</param>
    internal DatWriter(WriterMode mode = WriterMode.Standard)
    {
        this.mode = mode;
    }

    /// <summary>
    /// Gets the name of the load file format.
    /// </summary>
    public override string FormatName => this.mode switch
    {
        WriterMode.LoadfileOnly => "DAT (Metadata)",
        WriterMode.ProductionSet => "Production Set DAT",
        _ => "DAT",
    };

    /// <summary>
    /// Gets the standard file extension for the load file, including the leading dot.
    /// </summary>
    public override string FileExtension => ".dat";

    /// <summary>
    /// Writes the load file to the specified stream based on the current writer mode.
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
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles)
    {
        // Defensive guards to prevent IndexOutOfRangeException when delimiters are unset
        var (hasQuote, quote, colDelim) = ResolveStandardDelimiters(request.Delimiters);

        await writer.WriteLineAsync(BuildStandardHeader(request, colDelim, quote, hasQuote));

        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var generator = GetEffectiveProfileGenerator(request, now);

        var buffer = new StringBuilder();
        int rowCount = 0;

        foreach (var fileData in processedFiles)
        {
            var profileValues = generator?.GenerateRow(fileData.WorkItem, fileData);
            foreach (var (_, rowLine) in BuildStandardRowsForFile(fileData, request, colDelim, quote, hasQuote, profileValues))
            {
                buffer.AppendLine(rowLine);
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
    /// Writes the load file using the standard mode, writing to the stream with optional chaos injection.
    /// </summary>
    /// <param name="stream">The output stream.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="processedFiles">The processed files data.</param>
    /// <param name="chaosEngine">The optional chaos engine.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task WriteStandardAsync(
        Stream stream,
        FileGenerationRequest request,
        System.Collections.Generic.IReadOnlyList<FileData> processedFiles,
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
        var (hasQuote, quote, colDelim) = ResolveStandardDelimiters(request.Delimiters);

        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var generator = GetEffectiveProfileGenerator(request, now);

        var rows = new List<(long LineNumber, string RecordId, string Line)>();
        rows.Add((1, "HEADER", BuildStandardHeader(request, colDelim, quote, hasQuote)));

        long currentLineNumber = 2;
        foreach (var fileData in processedFiles)
        {
            var profileValues = generator?.GenerateRow(fileData.WorkItem, fileData);
            foreach (var (recordId, rowLine) in BuildStandardRowsForFile(fileData, request, colDelim, quote, hasQuote, profileValues))
            {
                rows.Add((currentLineNumber++, recordId, rowLine));
            }
        }

        await WriteRowsWithChaosAsync(stream, encoding, eolString, rows, chaosEngine);
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
        char colDelim = !string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? request.Delimiters.ColumnDelimiter[0] : '\u0014';
        char quote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? request.Delimiters.QuoteDelimiter[0] : '\u00fe';
        bool hasQuote = !string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter);

        // Build header
        var header = BuildLoadfileOnlyHeader(colDelim, quote, hasQuote, request.Metadata.ColumnProfile?.FieldNamingConvention);

        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value + 1) : new Random();
#pragma warning restore S2245

        await using var writer = CreateWriter(stream, request);

        // Write header
        string interceptedHeader = ApplyChaosInterception(chaosEngine, 1, header, "HEADER");
        await writer.WriteAsync(interceptedHeader + eolString);

        if (chaosEngine != null)
        {
            var anomaly = chaosEngine.GetEncodingAnomaly(1, 2, encoding);
            if (anomaly != null)
            {
                await writer.FlushAsync();
                await stream.WriteAsync(anomaly);
            }
        }

        for (long i = 1; i <= request.Output.FileCount; i++)
        {
            long lineNumber = i + 1;
            string recordId = request.Bates != null
                ? BatesNumberGenerator.Generate(request.Bates, i - 1)
                : $"DOC{i:D8}";
            var line = BuildLoadfileOnlyRow(i, recordId, colDelim, quote, hasQuote, now, random);

            string interceptedLine = ApplyChaosInterception(chaosEngine, lineNumber, line, recordId);
            await writer.WriteAsync(interceptedLine + eolString);

            if (chaosEngine != null)
            {
                var anomaly = chaosEngine.GetEncodingAnomaly(lineNumber, lineNumber + 1, encoding);
                if (anomaly != null)
                {
                    await writer.FlushAsync();
                    await stream.WriteAsync(anomaly);
                }
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
        var col = string.IsNullOrEmpty(request.Delimiters.ColumnDelimiter) ? "\u0014" : request.Delimiters.ColumnDelimiter;
        var quote = string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? "\u00fe" : request.Delimiters.QuoteDelimiter;
        var eol = GetEolString(request.Delimiters.EndOfLine);
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);

        var namingConvention = request.Metadata.ColumnProfile?.FieldNamingConvention;
        var headers = new List<string> { "DOCID", "BATES_NUMBER", "VOLUME", "NATIVE_PATH", "TEXT_PATH", "IMAGE_PATH", "CUSTODIAN", "DATE_CREATED", "FILE_SIZE", "FILE_TYPE" };
        if (request.Metadata.WithFamilies)
        {
            headers.AddRange(new[] { "BEGATTACH", "ENDATTACH", "PARENTDOCID" });
        }

        var finalHeaders = headers.Select(h => NamingConventionHelper.ApplyConvention(h, namingConvention));
        var headerLine = string.Join(col, finalHeaders.Select(h => $"{quote}{h}{quote}"));

        if (chaosEngine == null)
        {
            await using var writer = CreateWriter(stream, request);
            await writer.WriteAsync(headerLine + eol);

            foreach (var fileData in processedFiles)
            {
                foreach (var (_, rowLine) in BuildProductionSetRowsForFile(fileData, request, col, quote))
                {
                    await writer.WriteAsync(rowLine + eol);
                }
            }

            await writer.FlushAsync();
            return;
        }

        // Chaos path: build rows then delegate to shared WriteRowsWithChaosAsync
        var rows = new List<(long LineNumber, string RecordId, string Line)>();
        rows.Add((1, "HEADER", headerLine));

        long currentLineNumber = 2;
        foreach (var fileData in processedFiles)
        {
            foreach (var (recordId, rowLine) in BuildProductionSetRowsForFile(fileData, request, col, quote))
            {
                rows.Add((currentLineNumber++, recordId, rowLine));
            }
        }

        await WriteRowsWithChaosAsync(stream, encoding, eol, rows, chaosEngine);
    }

    /// <summary>
    /// Gets the parent and child document IDs along with an indication of whether an attachment is present.
    /// </summary>
    private static (string ParentId, string ChildId, bool HasAttachment) GetFamilyIdentifiers(FileData fileData, FileGenerationRequest request)
    {
        bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;
        string parentId = request.Bates != null
            ? BatesNumberGenerator.Generate(request.Bates, fileData.WorkItem.Index - 1)
            : $"DOC{fileData.WorkItem.Index:D8}";
        string childId = hasAttachment ? $"{parentId}_A001" : parentId;
        return (parentId, childId, hasAttachment);
    }

    /// <summary>
    /// Builds standard or production set load file rows (parent and optional child attachments) for a single file.
    /// </summary>
    private static IEnumerable<(string RecordId, string Line)> BuildRowsForFile(
        FileData fileData,
        FileGenerationRequest request,
        Func<RowBuildContext, string> parentRowBuilder,
        Func<RowBuildContext, string> childRowBuilder)
    {
        var (parentId, childId, hasAttachment) = GetFamilyIdentifiers(fileData, request);

        var parentLine = parentRowBuilder(new RowBuildContext { BegAttach = parentId, EndAttach = childId, ParentDocId = string.Empty });
        yield return (parentId, parentLine);

        if (hasAttachment)
        {
            var attach = fileData.Attachment!.Value;
            var childLine = childRowBuilder(new RowBuildContext
            {
                IdOverride = childId,
                IsChild = true,
                BegAttach = parentId,
                EndAttach = childId,
                ParentDocId = parentId,
                FileSizeOverride = attach.content.Length.ToString()
            });

            yield return (childId, childLine);
        }
    }

    /// <summary>
    /// Builds standard load file rows (parent and optional child attachments) for a single file.
    /// </summary>
    /// <param name="fileData">The generated file data.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="colDelim">The column delimiter character.</param>
    /// <param name="quote">The quote character.</param>
    /// <param name="hasQuote">A value indicating whether quote characters should be written around each field.</param>
    /// <param name="profileValues">The custom metadata profile values.</param>
    /// <returns>A collection of generated load file rows containing record ID and row string.</returns>
    private static IEnumerable<(string RecordId, string Line)> BuildStandardRowsForFile(
        FileData fileData,
        FileGenerationRequest request,
        char colDelim,
        char quote,
        bool hasQuote,
        System.Collections.Generic.Dictionary<string, string>? profileValues)
    {
        return BuildRowsForFile(
            fileData,
            request,
            context => BuildStandardRow(fileData, request, colDelim, quote, hasQuote, profileValues, context),
            context =>
            {
                var attach = fileData.Attachment!.Value;
                var attachmentPath = $"{fileData.WorkItem.FolderName}/{fileData.WorkItem.Index}_{attach.filename}";
                var newContext = new RowBuildContext
                {
                    IdOverride = context.IdOverride,
                    FilePathOverride = attachmentPath,
                    FileSizeOverride = context.FileSizeOverride,
                    IsChild = true,
                    BegAttach = context.BegAttach,
                    EndAttach = context.EndAttach,
                    ParentDocId = context.ParentDocId
                };
                return BuildStandardRow(fileData, request, colDelim, quote, hasQuote, profileValues, newContext);
            });
    }

    /// <summary>
    /// Builds production set load file rows (parent and optional child attachments) for a single file.
    /// </summary>
    /// <param name="fileData">The generated file data.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="col">The column delimiter string.</param>
    /// <param name="quote">The quote string.</param>
    /// <returns>A collection of generated load file rows containing record ID and row string.</returns>
    private static IEnumerable<(string RecordId, string Line)> BuildProductionSetRowsForFile(
        FileData fileData,
        FileGenerationRequest request,
        string col,
        string quote)
    {
        return BuildRowsForFile(
            fileData,
            request,
            context => BuildProductionSetRow(fileData, request, col, quote, context),
            context =>
            {
                var attach = fileData.Attachment!.Value;
                var childExt = Path.GetExtension(attach.filename);
                var childBates = context.IdOverride!;
                var childNativePath = Path.Combine("NATIVES", fileData.WorkItem.FolderName, $"{childBates}{childExt}").Replace(Path.DirectorySeparatorChar, '\\');
                var childTextPath = Path.Combine("TEXT", fileData.WorkItem.FolderName, $"{childBates}.txt").Replace(Path.DirectorySeparatorChar, '\\');
                var childImagePath = Path.Combine("IMAGES", fileData.WorkItem.FolderName, $"{childBates}.tif").Replace(Path.DirectorySeparatorChar, '\\');

                var newContext = new RowBuildContext
                {
                    IdOverride = childBates,
                    NativePathOverride = childNativePath,
                    TextPathOverride = childTextPath,
                    ImagePathOverride = childImagePath,
                    FileSizeOverride = context.FileSizeOverride,
                    IsChild = true,
                    BegAttach = context.BegAttach,
                    EndAttach = context.EndAttach,
                    ParentDocId = context.ParentDocId
                };
                return BuildProductionSetRow(fileData, request, col, quote, newContext);
            });
    }

    /// <summary>
    /// Builds a single DAT row for the production set output mode.
    /// </summary>
    /// <param name="fileData">The generated file data.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="col">The column delimiter.</param>
    /// <param name="quote">The quote delimiter.</param>
    /// <param name="context">The row context containing optional overrides and family boundaries.</param>
    /// <returns>A formatted DAT row string.</returns>
    private static string BuildProductionSetRow(
        FileData fileData,
        FileGenerationRequest request,
        string col,
        string quote,
        RowBuildContext context)
    {
        var workItem = fileData.WorkItem;
        var batesNumber = context.IdOverride ?? BatesNumberGenerator.Generate(request.Bates!, workItem.Index - 1);
        var imagePath = context.ImagePathOverride ?? workItem.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
            .Replace(Path.GetExtension(workItem.FilePathInZip), ".tif");

        var nativePath = context.NativePathOverride ?? workItem.FilePathInZip.Replace(Path.DirectorySeparatorChar, '\\');
        var textPath = context.TextPathOverride ?? nativePath.Replace($".{request.Output.FileType}", ".txt");
        var imagesPath = imagePath.Replace(Path.DirectorySeparatorChar, '\\');

#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(unchecked((int)(request.Metadata.Seed.Value + workItem.Index))) : Random.Shared;
#pragma warning restore S2245
        var now = request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;
        var maxCustodians = Math.Max(2, request.Metadata.CustodianCountOverride ?? 10);
        var custodianProd = $"Custodian {random.Next(1, maxCustodians + 1)}";
        var dateCreated = now.AddDays(-random.Next(1, 730)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var fileSize = context.FileSizeOverride ?? fileData.DataLength.ToString();
        var fileType = context.IsChild ? Path.GetExtension(fileData.Attachment!.Value.filename).TrimStart('.').ToUpperInvariant() : request.Output.FileType.ToUpperInvariant();

        var fields = new List<string>
        {
            batesNumber,
            batesNumber,
            workItem.FolderName,
            nativePath,
            textPath,
            imagesPath,
            custodianProd,
            dateCreated,
            fileSize,
            fileType,
        };

        if (request.Metadata.WithFamilies)
        {
            fields.AddRange(new[]
            {
                context.BegAttach ?? string.Empty,
                context.EndAttach ?? string.Empty,
                context.ParentDocId ?? string.Empty,
            });
        }

        return string.Join(col, fields.Select(f => $"{quote}{EscapeDatField(f, quote[0], request.Delimiters.NewlineDelimiter)}{quote}"));
    }

    /// <summary>
    /// Gets the effective profile generator to use for populating metadata fields.
    /// </summary>
    /// <param name="request">The file generation request.</param>
    /// <param name="now">The effective date/time reference.</param>
    /// <returns>A data generator instance or null if no profile should be used.</returns>
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

    /// <summary>
    /// Builds the header line for a standard DAT load file.
    /// </summary>
    /// <param name="request">The file generation request.</param>
    /// <param name="colDelim">The column delimiter character.</param>
    /// <param name="quote">The quote character.</param>
    /// <param name="hasQuote">A value indicating whether quote characters should be written around each field.</param>
    /// <returns>The formatted header string.</returns>
    private static string BuildStandardHeader(FileGenerationRequest request, char colDelim, char quote, bool hasQuote)
    {
        var namingConvention = request.Metadata.ColumnProfile?.FieldNamingConvention;
        var sb = new StringBuilder();
        AppendField(sb, NamingConventionHelper.ApplyConvention("Control Number", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("File Path", namingConvention), quote, hasQuote);

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("Custodian", namingConvention), quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("Date Sent", namingConvention), quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("Author", namingConvention), quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("File Size", namingConvention), quote, hasQuote);
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("To", namingConvention), quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("From", namingConvention), quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("Subject", namingConvention), quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("Sent Date", namingConvention), quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("Attachment", namingConvention), quote, hasQuote);
        }

        if (request.Bates != null)
        {
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("Bates Number", namingConvention), quote, hasQuote);
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("Page Count", namingConvention), quote, hasQuote);
        }

        if (request.Output.WithText)
        {
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("Extracted Text", namingConvention), quote, hasQuote);
        }

        if (request.Metadata.WithFamilies)
        {
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("BEGATTACH", namingConvention), quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("ENDATTACH", namingConvention), quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, NamingConventionHelper.ApplyConvention("PARENTDOCID", namingConvention), quote, hasQuote);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds a single DAT row for the standard output mode.
    /// </summary>
    /// <param name="fileData">The generated file data.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="colDelim">The column delimiter character.</param>
    /// <param name="quote">The quote character.</param>
    /// <param name="hasQuote">A value indicating whether quote characters should be written around each field.</param>
    /// <param name="profileValues">The custom metadata profile values.</param>
    /// <param name="context">The row context containing optional overrides and family boundaries.</param>
    /// <returns>A formatted DAT row string.</returns>
    private static string BuildStandardRow(
        FileData fileData,
        FileGenerationRequest request,
        char colDelim,
        char quote,
        bool hasQuote,
        System.Collections.Generic.Dictionary<string, string>? profileValues,
        RowBuildContext context)
    {
        var workItem = fileData.WorkItem;
        var docId = context.IdOverride ?? EscapeDatField($"DOC{workItem.Index:D8}", quote, request.Delimiters.NewlineDelimiter);
        var filePath = context.FilePathOverride ?? EscapeDatField(workItem.FilePathInZip, quote, request.Delimiters.NewlineDelimiter);

        var sb = new StringBuilder();
        AppendField(sb, docId, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, filePath, quote, hasQuote);

        AppendMetadataColumns(sb, fileData, request, profileValues, context);
        AppendEmailColumns(sb, fileData, request, profileValues, context);

        if (request.Bates != null)
        {
            var batesVal = context.IdOverride ?? BatesNumberGenerator.Generate(request.Bates, workItem.Index - 1);
            sb.Append(colDelim);
            AppendField(sb, batesVal, quote, hasQuote);
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            var pageCount = context.IsChild ? 1 : fileData.PageCount;
            sb.Append(colDelim);
            AppendField(sb, pageCount.ToString(), quote, hasQuote);
        }

        AppendTextColumn(sb, fileData, request, context);

        if (request.Metadata.WithFamilies)
        {
            sb.Append(colDelim);
            AppendField(sb, context.BegAttach ?? string.Empty, quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, context.EndAttach ?? string.Empty, quote, hasQuote);
            sb.Append(colDelim);
            AppendField(sb, context.ParentDocId ?? string.Empty, quote, hasQuote);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Appends the general metadata columns to the standard row.
    /// </summary>
    private static void AppendMetadataColumns(
        StringBuilder sb,
        FileData fileData,
        FileGenerationRequest request,
        System.Collections.Generic.Dictionary<string, string>? profileValues,
        RowBuildContext context)
    {
        if (!request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            return;
        }

        var (hasQuote, quote, colDelim) = ResolveStandardDelimiters(request.Delimiters);

        var custodian = EscapeDatField(profileValues?.GetValueOrDefault("CUSTODIAN") ?? string.Empty, quote, request.Delimiters.NewlineDelimiter);
        var dateSent = context.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("DATESENT") ?? string.Empty);
        var author = context.IsChild ? string.Empty : EscapeDatField(profileValues?.GetValueOrDefault("AUTHOR") ?? string.Empty, quote, request.Delimiters.NewlineDelimiter);
        var fileSize = context.FileSizeOverride ?? (profileValues?.GetValueOrDefault("FILESIZE") ?? fileData.DataLength.ToString());

        sb.Append(colDelim);
        AppendField(sb, custodian, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, dateSent, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, author, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, fileSize, quote, hasQuote);
    }

    /// <summary>
    /// Appends the EML email-specific metadata columns to the standard row.
    /// </summary>
    private static void AppendEmailColumns(
        StringBuilder sb,
        FileData fileData,
        FileGenerationRequest request,
        System.Collections.Generic.Dictionary<string, string>? profileValues,
        RowBuildContext context)
    {
        if (!request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            return;
        }

        var (hasQuote, quote, colDelim) = ResolveStandardDelimiters(request.Delimiters);

        var workItem = fileData.WorkItem;
        var to = context.IsChild ? string.Empty : EscapeDatField(profileValues?.GetValueOrDefault("EMAILTO") ?? $"recipient{workItem.Index}@example.com", quote, request.Delimiters.NewlineDelimiter);
        var from = context.IsChild ? string.Empty : EscapeDatField(profileValues?.GetValueOrDefault("EMAILFROM") ?? $"sender{workItem.Index}@example.com", quote, request.Delimiters.NewlineDelimiter);
        var subject = context.IsChild ? string.Empty : EscapeDatField(profileValues?.GetValueOrDefault("EMAILSUBJECT") ?? $"Email Subject {workItem.Index}", quote, request.Delimiters.NewlineDelimiter);
        var sentDate = context.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("EMAILSENTDATE") ?? string.Empty);
        var attachment = context.IsChild ? string.Empty : EscapeDatField(profileValues?.GetValueOrDefault("EMAILATTACHMENT") ?? string.Empty, quote, request.Delimiters.NewlineDelimiter);

        sb.Append(colDelim);
        AppendField(sb, to, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, from, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, subject, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, sentDate, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, attachment, quote, hasQuote);
    }

    /// <summary>
    /// Appends the extracted text path column to the standard row if enabled.
    /// </summary>
    private static void AppendTextColumn(
        StringBuilder sb,
        FileData fileData,
        FileGenerationRequest request,
        RowBuildContext context)
    {
        if (!request.Output.WithText)
        {
            return;
        }

        var (hasQuote, quote, colDelim) = ResolveStandardDelimiters(request.Delimiters);

        var workItem = fileData.WorkItem;
        string textPath;
        if (context.IsChild)
        {
            var attachmentTextFileName = $"{Path.GetFileNameWithoutExtension(fileData.Attachment!.Value.filename)}.txt";
            textPath = $"{workItem.FolderName}/{workItem.Index}_{attachmentTextFileName}";
        }
        else
        {
            var sourceSuffix = $".{request.Output.FileType}";
            textPath = workItem.FilePathInZip.EndsWith(sourceSuffix, StringComparison.OrdinalIgnoreCase)
                ? workItem.FilePathInZip[..^sourceSuffix.Length] + ".txt"
                : workItem.FilePathInZip;
        }

        sb.Append(colDelim);
        AppendField(sb, textPath, quote, hasQuote);
    }

    /// <summary>
    /// Resolves the standard column delimiter, quote character, and hasQuote flag from the configured
    /// delimiter settings, applying fallback sentinel values when a delimiter is unset.
    /// </summary>
    /// <param name="delimiters">The delimiter configuration.</param>
    /// <returns>A tuple of (hasQuote, quoteChar, columnDelimChar).</returns>
    private static (bool HasQuote, char Quote, char ColDelim) ResolveStandardDelimiters(Config.DelimiterConfig delimiters)
    {
        bool hasQuote = !string.IsNullOrEmpty(delimiters.QuoteDelimiter);
        char quote = hasQuote ? delimiters.QuoteDelimiter[0] : '\u00fe';
        char colDelim = !string.IsNullOrEmpty(delimiters.ColumnDelimiter) ? delimiters.ColumnDelimiter[0] : '\u0014';
        return (hasQuote, quote, colDelim);
    }

    /// <summary>
    /// Builds the header line for a loadfile-only DAT load file.
    /// </summary>
    /// <param name="colDelim">The column delimiter character.</param>
    /// <param name="quote">The quote character.</param>
    /// <param name="hasQuote">A value indicating whether quotes should be applied.</param>
    /// <param name="namingConvention">The naming convention for fields.</param>
    /// <returns>The formatted header string.</returns>
    private static string BuildLoadfileOnlyHeader(
        char colDelim,
        char quote,
        bool hasQuote,
        string? namingConvention)
    {
        var sb = new StringBuilder();
        AppendField(sb, NamingConventionHelper.ApplyConvention("Control Number", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("File Path", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("Custodian", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("Date Sent", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("Author", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("File Size", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("EmailSubject", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("EmailFrom", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("EmailTo", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("EmailSentDate", namingConvention), quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, NamingConventionHelper.ApplyConvention("ExtractedText", namingConvention), quote, hasQuote);

        return sb.ToString();
    }

    /// <summary>
    /// Builds a row line for a loadfile-only DAT load file.
    /// </summary>
    /// <param name="index">The 1-based index of the document.</param>
    /// <param name="recordId">The record ID of the document.</param>
    /// <param name="colDelim">The column delimiter character.</param>
    /// <param name="quote">The quote character.</param>
    /// <param name="hasQuote">A value indicating whether quotes should be applied.</param>
    /// <param name="now">The current timestamp reference.</param>
    /// <param name="random">The random number generator.</param>
    /// <returns>The formatted row string.</returns>
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
        var dateSent = now.AddDays(-random.Next(1, 365)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var author = $"Author {random.Next(1, 100):D3}";
        var fileSize = random.Next(1024, 10485760).ToString();
        var subjLine = $"Email Subject {index}";
        var senderAddr = $"sender{index}@example.com";
        var recipientAddr = $"recipient{index}@example.com";
        var sentTime = now.AddDays(-random.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
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
        AppendField(sb, subjLine, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, senderAddr, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, recipientAddr, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, sentTime, quote, hasQuote);
        sb.Append(colDelim);
        AppendField(sb, extractedText, quote, hasQuote);

        return sb.ToString();
    }

    /// <summary>
    /// Context object containing optional overrides and family boundary information for building a load file row.
    /// </summary>
    private sealed class RowBuildContext
    {
        /// <summary>
        /// Gets the override value for the document control number or Bates number.
        /// </summary>
        public string? IdOverride { get; init; }

        /// <summary>
        /// Gets the override value for the relative file path.
        /// </summary>
        public string? FilePathOverride { get; init; }

        /// <summary>
        /// Gets the override value for the native path.
        /// </summary>
        public string? NativePathOverride { get; init; }

        /// <summary>
        /// Gets the override value for the extracted text path.
        /// </summary>
        public string? TextPathOverride { get; init; }

        /// <summary>
        /// Gets the override value for the image path.
        /// </summary>
        public string? ImagePathOverride { get; init; }

        /// <summary>
        /// Gets the override value for the file size in bytes.
        /// </summary>
        public string? FileSizeOverride { get; init; }

        /// <summary>
        /// Gets a value indicating whether the row represents a child attachment document.
        /// </summary>
        public bool IsChild { get; init; }

        /// <summary>
        /// Gets the override value for the BEGATTACH family field.
        /// </summary>
        public string? BegAttach { get; init; }

        /// <summary>
        /// Gets the override value for the ENDATTACH family field.
        /// </summary>
        public string? EndAttach { get; init; }

        /// <summary>
        /// Gets the override value for the PARENTDOCID family field.
        /// </summary>
        public string? ParentDocId { get; init; }
    }
}
