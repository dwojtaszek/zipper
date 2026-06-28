using Zipper.Profiles;
using Zipper.Utils;

namespace Zipper.LoadFiles;

/// <summary>
/// Column authority for the DAT (Concordance) format across all three writer modes
/// (Standard, Loadfile-Only, Production Set) plus the column-profile path. Produces the
/// ordered header columns and raw record values; a <see cref="DatSerializer"/> renders each
/// record to a line and the <see cref="LoadFileEmitter"/> owns I/O, EOL, and chaos.
/// </summary>
/// <remarks>
/// Values are emitted raw (unescaped). The serializer applies quote-doubling and newline
/// sanitisation once per field; the historical writers escaped some fields and not others,
/// but those un-escaped fields never contained the quote or newline characters, so uniform
/// escaping is byte-identical.
/// </remarks>
internal sealed class DatComposer : ILoadFileComposer
{
    private readonly FileGenerationRequest request;
    private readonly WriterMode mode;
    private readonly string? namingConvention;
    private readonly List<string> headerColumns;

    // Profile path (loadfile-only with an explicit ColumnProfile) only.
    private readonly DataGenerator? profileGenerator;
    private readonly List<string>? profileColumnNames;
    private readonly BatesSequence? batesSequence;

    public DatComposer(FileGenerationRequest request, WriterMode mode)
    {
        this.request = request;
        this.mode = mode;
        this.namingConvention = request.Metadata.ColumnProfile?.FieldNamingConvention;
        this.batesSequence = request.Bates != null ? BatesSequence.FromConfig(request.Bates) : null;

        if (mode != WriterMode.ProductionSet && request.Metadata.ColumnProfile is not null)
        {
            var profile = request.Metadata.ColumnProfile;
            this.profileGenerator = new DataGenerator(
                profile,
                request.Metadata.Seed,
                custodianCountOverride: request.Metadata.CustodianCountOverride,
                dateFormatOverride: request.Metadata.DateFormatOverride,
                emptyPercentageOverride: request.Metadata.EmptyPercentageOverride);
            this.profileColumnNames = this.profileGenerator.GetColumnNames().ToList();
            this.headerColumns = this.profileColumnNames.Select(this.Apply).ToList();
        }
        else
        {
            this.headerColumns = this.BuildHeaderColumns();
        }
    }

    public IReadOnlyList<string> HeaderColumns => this.headerColumns;

    public IEnumerable<LoadFileRecord> Compose(IReadOnlyList<FileData> processedFiles)
        => this.mode switch
        {
            WriterMode.LoadfileOnly => this.profileGenerator is not null
                ? this.ComposeProfile()
                : this.ComposeLoadfileOnly(),
            WriterMode.ProductionSet => this.ComposeProduction(processedFiles),
            _ => this.ComposeStandard(processedFiles),
        };

    private string Apply(string name) => NamingConventionHelper.ApplyConvention(name, this.namingConvention);

    private DateTime EffectiveNow() => this.request.Metadata.Seed.HasValue
        ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        : DateTime.UtcNow;

    private LoadFileRecord MakeRecord(string recordId, List<string> orderedValues)
        => LoadFileRecordBuilder.Build(this.headerColumns, orderedValues, recordId);

    private List<string> BuildHeaderColumns()
    {
        if (this.mode == WriterMode.ProductionSet)
        {
            var headers = new List<string> { "DOCID", "BATES_NUMBER", "VOLUME", "NATIVE_PATH", "TEXT_PATH", "IMAGE_PATH", "CUSTODIAN", "DATE_CREATED", "FILE_SIZE", "FILE_TYPE" };
            if (this.request.Metadata.WithFamilies)
            {
                headers.AddRange(new[] { "BEGATTACH", "ENDATTACH", "PARENTDOCID" });
            }

            return headers.Select(this.Apply).ToList();
        }

        if (this.mode == WriterMode.LoadfileOnly)
        {
            return new[]
            {
                "Control Number", "File Path", "Custodian", "Date Sent", "Author", "File Size",
                "EmailSubject", "EmailFrom", "EmailTo", "EmailSentDate", "ExtractedText",
            }.Select(this.Apply).ToList();
        }

        // Standard mode: columns depend on request flags.
        var cols = new List<string> { "Control Number", "File Path" };
        if (this.request.Metadata.ShouldIncludeMetadataColumns(this.request.Output))
        {
            cols.AddRange(new[] { "Custodian", "Date Sent", "Author", "File Size" });
        }

        if (this.request.Metadata.ShouldIncludeEmlColumns(this.request.Output))
        {
            cols.AddRange(new[] { "To", "From", "Subject", "Sent Date", "Attachment" });
        }

        if (this.request.Bates != null)
        {
            cols.Add("Bates Number");
        }

        if (this.request.Tiff.ShouldIncludePageCount(this.request.Output))
        {
            cols.Add("Page Count");
        }

        if (this.request.Output.WithText)
        {
            cols.Add("Extracted Text");
        }

        if (this.request.Metadata.WithFamilies)
        {
            cols.AddRange(new[] { "BEGATTACH", "ENDATTACH", "PARENTDOCID" });
        }

        return cols.Select(this.Apply).ToList();
    }

    private IEnumerable<LoadFileRecord> ComposeStandard(IReadOnlyList<FileData> processedFiles)
    {
        var generator = this.profileGenerator ?? GetEffectiveProfileGenerator(this.request, this.EffectiveNow());

        foreach (var fileData in processedFiles)
        {
            var profileValues = generator?.GenerateRow(fileData.WorkItem, fileData);
            var (parentId, childId, hasAttachment) = this.GetFamilyIdentifiers(fileData, this.request);

            yield return this.MakeRecord(
                parentId,
                this.StandardRowValues(fileData, profileValues, new RowCtx { BegAttach = parentId, EndAttach = childId, ParentDocId = string.Empty }));

            if (hasAttachment)
            {
                var attach = fileData.Attachment!.Value;
                var attachmentPath = $"{fileData.WorkItem.FolderName}/{fileData.WorkItem.Index}_{attach.filename}";
                yield return this.MakeRecord(
                    childId,
                    this.StandardRowValues(fileData, profileValues, new RowCtx
                    {
                        IdOverride = childId,
                        FilePathOverride = attachmentPath,
                        FileSizeOverride = attach.content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        IsChild = true,
                        BegAttach = parentId,
                        EndAttach = childId,
                        ParentDocId = parentId,
                    }));
            }
        }
    }

    private List<string> StandardRowValues(FileData fileData, Dictionary<string, string>? profileValues, RowCtx ctx)
    {
        var wi = fileData.WorkItem;

        if (this.profileColumnNames is not null && profileValues is not null)
        {
            string id = ctx.IdOverride ?? (this.batesSequence is not null ? this.batesSequence.Format(wi.Index - 1).ToString() : $"DOC{wi.Index:D8}");
            var fileSize = ctx.FileSizeOverride ?? (ctx.IsChild ? null : fileData.DataLength.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var result = new List<string>(this.profileColumnNames.Count);
            for (int i = 0; i < this.profileColumnNames.Count; i++)
            {
                var n = this.profileColumnNames[i];
                var upper = n.ToUpperInvariant();

                string val;
                switch (upper)
                {
                    case "DOCID":
                    case "CONTROLNUMBER":
                    case "BEGBATES":
                    case "ENDBATES":
                    case "CONTROL_NUMBER":
                    case "CONTROL NUMBER":
                        val = id;
                        break;
                    case "FILEPATH":
                    case "FILE_PATH":
                    case "FILE PATH":
                    case "NATIVEPATH":
                    case "NATIVE_PATH":
                    case "NATIVE PATH":
                        val = ctx.FilePathOverride ?? wi.FilePathInZip;
                        break;
                    case "FILESIZE":
                    case "FILE_SIZE":
                    case "FILE SIZE":
                        val = fileSize ?? (profileValues.TryGetValue(n, out var fs) ? fs : string.Empty);
                        break;
                    case "BEGATTACH":
                    case "BEG_ATTACH":
                    case "BEG ATTACH":
                        val = this.request.Metadata.WithFamilies ? ctx.BegAttach : (profileValues.TryGetValue(n, out var ba) ? ba : string.Empty);
                        break;
                    case "ENDATTACH":
                    case "END_ATTACH":
                    case "END ATTACH":
                        val = this.request.Metadata.WithFamilies ? ctx.EndAttach : (profileValues.TryGetValue(n, out var ea) ? ea : string.Empty);
                        break;
                    case "PARENTDOCID":
                    case "PARENT_DOC_ID":
                    case "PARENT DOC ID":
                        val = this.request.Metadata.WithFamilies ? ctx.ParentDocId : (profileValues.TryGetValue(n, out var pd) ? pd : string.Empty);
                        break;
                    case "DATESENT":
                    case "DATE_SENT":
                    case "DATE SENT":
                    case "AUTHOR":
                    case "EMAILTO":
                    case "EMAIL_TO":
                    case "EMAIL TO":
                    case "EMAILFROM":
                    case "EMAIL_FROM":
                    case "EMAIL FROM":
                    case "EMAILSUBJECT":
                    case "EMAIL_SUBJECT":
                    case "EMAIL SUBJECT":
                    case "EMAILSENTDATE":
                    case "EMAIL_SENT_DATE":
                    case "EMAIL SENT DATE":
                    case "EMAILATTACHMENT":
                    case "EMAIL_ATTACHMENT":
                    case "EMAIL ATTACHMENT":
                        val = ctx.IsChild ? string.Empty : (profileValues.TryGetValue(n, out var ce) ? ce : string.Empty);
                        break;
                    case "PAGECOUNT":
                    case "PAGE_COUNT":
                    case "PAGE COUNT":
                        val = ctx.IsChild ? "1" : fileData.PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case "TEXTPATH":
                    case "TEXT_PATH":
                    case "TEXT PATH":
                        val = this.request.Output.WithText ? this.StandardTextPath(fileData, ctx) : string.Empty;
                        break;
                    default:
                        val = profileValues.TryGetValue(n, out var x) ? x : string.Empty;
                        break;
                }
                result.Add(val);
            }
            return result;
        }

        var v = new List<string>(this.headerColumns.Count)
        {
            ctx.IdOverride ?? $"DOC{wi.Index:D8}",
            ctx.FilePathOverride ?? wi.FilePathInZip,
        };

        if (this.request.Metadata.ShouldIncludeMetadataColumns(this.request.Output))
        {
            v.Add(profileValues?.GetValueOrDefault("CUSTODIAN") ?? string.Empty);
            v.Add(ctx.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("DATESENT") ?? string.Empty));
            v.Add(ctx.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("AUTHOR") ?? string.Empty));
            v.Add(ctx.FileSizeOverride ?? (profileValues?.GetValueOrDefault("FILESIZE") ?? fileData.DataLength.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (this.request.Metadata.ShouldIncludeEmlColumns(this.request.Output))
        {
            v.Add(ctx.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("EMAILTO") ?? $"recipient{wi.Index}@example.com"));
            v.Add(ctx.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("EMAILFROM") ?? $"sender{wi.Index}@example.com"));
            v.Add(ctx.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("EMAILSUBJECT") ?? $"Email Subject {wi.Index}"));
            v.Add(ctx.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("EMAILSENTDATE") ?? string.Empty));
            v.Add(ctx.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("EMAILATTACHMENT") ?? string.Empty));
        }

        if (this.request.Bates != null)
        {
            v.Add(ctx.IdOverride ?? this.batesSequence!.Format(wi.Index - 1).ToString());
        }

        if (this.request.Tiff.ShouldIncludePageCount(this.request.Output))
        {
            v.Add((ctx.IsChild ? 1 : fileData.PageCount).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (this.request.Output.WithText)
        {
            v.Add(this.StandardTextPath(fileData, ctx));
        }

        if (this.request.Metadata.WithFamilies)
        {
            v.Add(ctx.BegAttach);
            v.Add(ctx.EndAttach);
            v.Add(ctx.ParentDocId);
        }

        return v;
    }

    private string StandardTextPath(FileData fileData, RowCtx ctx)
    {
        var wi = fileData.WorkItem;
        if (ctx.IsChild)
        {
            var attachmentTextFileName = $"{Path.GetFileNameWithoutExtension(fileData.Attachment!.Value.filename)}.txt";
            return $"{wi.FolderName}/{wi.Index}_{attachmentTextFileName}";
        }

        var sourceSuffix = $".{this.request.Output.FileType}";
        return wi.FilePathInZip.EndsWith(sourceSuffix, StringComparison.OrdinalIgnoreCase)
            ? wi.FilePathInZip[..^sourceSuffix.Length] + ".txt"
            : wi.FilePathInZip;
    }

    private IEnumerable<LoadFileRecord> ComposeProduction(IReadOnlyList<FileData> processedFiles)
    {
        foreach (var fileData in processedFiles)
        {
            var (parentId, childId, hasAttachment) = this.GetFamilyIdentifiers(fileData, this.request);

            yield return this.MakeRecord(
                parentId,
                this.ProductionRowValues(fileData, new RowCtx { BegAttach = parentId, EndAttach = childId, ParentDocId = string.Empty }));

            if (hasAttachment)
            {
                var attach = fileData.Attachment!.Value;
                var childExt = Path.GetExtension(attach.filename);
                var childBates = childId;
                var childNativePath = Path.Combine("NATIVES", fileData.WorkItem.FolderName, $"{childBates}{childExt}").Replace(Path.DirectorySeparatorChar, '\\');
                var childTextPath = Path.Combine("TEXT", fileData.WorkItem.FolderName, $"{childBates}.txt").Replace(Path.DirectorySeparatorChar, '\\');
                var childImagePath = Path.Combine("IMAGES", fileData.WorkItem.FolderName, $"{childBates}.tif").Replace(Path.DirectorySeparatorChar, '\\');

                yield return this.MakeRecord(
                    childId,
                    this.ProductionRowValues(fileData, new RowCtx
                    {
                        IdOverride = childBates,
                        NativePathOverride = childNativePath,
                        TextPathOverride = childTextPath,
                        ImagePathOverride = childImagePath,
                        FileSizeOverride = attach.content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        IsChild = true,
                        BegAttach = parentId,
                        EndAttach = childId,
                        ParentDocId = parentId,
                    }));
            }
        }
    }

    private List<string> ProductionRowValues(FileData fileData, RowCtx ctx)
    {
        var wi = fileData.WorkItem;
        var batesNumber = ctx.IdOverride ?? this.batesSequence!.Format(wi.Index - 1).ToString();
        var imagePath = ctx.ImagePathOverride ?? wi.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
            .Replace(Path.GetExtension(wi.FilePathInZip), ".tif", StringComparison.Ordinal);
        // FilePathInZip always uses forward slashes (ZIP spec); replace '/' directly so the
        // backslash normalization also works on Windows (where DirectorySeparatorChar is '\').
        var nativePath = ctx.NativePathOverride ?? wi.FilePathInZip.Replace('/', '\\');
        var textPath = ctx.TextPathOverride ?? nativePath.Replace($".{this.request.Output.FileType}", ".txt", StringComparison.Ordinal);
        var imagesPath = imagePath.Replace('/', '\\');

#pragma warning disable S2245
        var random = this.request.Metadata.Seed.HasValue ? new Random(unchecked((int)(this.request.Metadata.Seed.Value + wi.Index))) : Random.Shared;
#pragma warning restore S2245
        var now = this.EffectiveNow();
        var maxCustodians = Math.Max(2, this.request.Metadata.CustodianCountOverride ?? 10);
        var custodianProd = $"Custodian {random.Next(1, maxCustodians + 1)}";
        var dateCreated = now.AddDays(-random.Next(1, 730)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var fileSize = ctx.FileSizeOverride ?? fileData.DataLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var fileType = ctx.IsChild ? Path.GetExtension(fileData.Attachment!.Value.filename).TrimStart('.').ToUpperInvariant() : this.request.Output.FileType.ToUpperInvariant();

        var v = new List<string>(this.headerColumns.Count)
        {
            batesNumber,
            batesNumber,
            wi.FolderName,
            nativePath,
            textPath,
            imagesPath,
            custodianProd,
            dateCreated,
            fileSize,
            fileType,
        };

        if (this.request.Metadata.WithFamilies)
        {
            v.Add(ctx.BegAttach);
            v.Add(ctx.EndAttach);
            v.Add(ctx.ParentDocId);
        }

        return v;
    }

    private IEnumerable<LoadFileRecord> ComposeLoadfileOnly()
    {
        var now = this.EffectiveNow();
#pragma warning disable S2245
        var random = this.request.Metadata.Seed.HasValue ? new Random(this.request.Metadata.Seed.Value + 1) : new Random();
#pragma warning restore S2245

        for (long i = 1; i <= this.request.Output.FileCount; i++)
        {
            var recordId = this.batesSequence is not null
                ? this.batesSequence.Next().ToString()
                : $"DOC{i:D8}";

            // Draw order must match the legacy writer: dateSent, author, fileSize, sentTime.
            var custodian = $"Custodian {(i % 10) + 1}";
            var dateSent = now.AddDays(-random.Next(1, 365)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var author = $"Author {random.Next(1, 100):D3}";
            var fileSize = random.Next(1024, 10485760).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var subjLine = $"Email Subject {i}";
            var senderAddr = $"sender{i}@example.com";
            var recipientAddr = $"recipient{i}@example.com";
            var sentTime = now.AddDays(-random.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            var filePath = $"NATIVES\\{(i % 50) + 1:D3}\\{recordId}.pdf";
            var extractedText = $"Sample extracted text content for document {recordId}.";

            yield return this.MakeRecord(recordId, new List<string>
            {
                recordId, filePath, custodian, dateSent, author, fileSize,
                subjLine, senderAddr, recipientAddr, sentTime, extractedText,
            });
        }
    }

    private IEnumerable<LoadFileRecord> ComposeProfile()
    {
        var generator = this.profileGenerator!;
        var columnNames = this.profileColumnNames!;

#pragma warning disable S2245
        var rowRandom = this.request.Metadata.Seed.HasValue ? new Random(this.request.Metadata.Seed.Value + 17) : new Random();
#pragma warning restore S2245

        var fileTypeLower = this.request.Output.FileTypeLower;

        for (long i = 1; i <= this.request.Output.FileCount; i++)
        {
            var folderNum = (int)((i - 1) % 50) + 1;
            var workItem = new FileWorkItem
            {
                Index = i,
                FolderNumber = folderNum,
                FilePathInZip = $"NATIVES/{folderNum:D3}/DOC{i:D8}.{fileTypeLower}",
            };

            var fileData = new FileData
            {
                WorkItem = workItem,
                DataLength = rowRandom.Next(1024, 10_485_760),
                PageCount = rowRandom.Next(1, 11),
            };

            var values = generator.GenerateRow(workItem, fileData);
            var ordered = columnNames.Select(n => values.TryGetValue(n, out var x) ? x : string.Empty).ToList();
            yield return this.MakeRecord($"DOC{i:D8}", ordered);
        }
    }

    private (string ParentId, string ChildId, bool HasAttachment) GetFamilyIdentifiers(FileData fileData, FileGenerationRequest request)
    {
        bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;
        string parentId = this.batesSequence is not null
            ? this.batesSequence.Format(fileData.WorkItem.Index - 1).ToString()
            : $"DOC{fileData.WorkItem.Index:D8}";
        string childId = hasAttachment ? $"{parentId}_A001" : parentId;
        return (parentId, childId, hasAttachment);
    }

    private static DataGenerator? GetEffectiveProfileGenerator(FileGenerationRequest request, DateTime now)
    {
        var profile = request.Metadata.ColumnProfile;
        if (profile is null && request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            profile = request.Output.IsEml
                ? BuiltInProfiles.LegacyEml
                : BuiltInProfiles.LegacyWithMetadata;
        }

        return profile is not null ? new DataGenerator(profile, request.Metadata.Seed, now) : null;
    }

    private sealed record RowCtx
    {
        public string? IdOverride { get; init; }

        public string? FilePathOverride { get; init; }

        public string? FileSizeOverride { get; init; }

        public string? NativePathOverride { get; init; }

        public string? TextPathOverride { get; init; }

        public string? ImagePathOverride { get; init; }

        public bool IsChild { get; init; }

        public string BegAttach { get; init; } = string.Empty;

        public string EndAttach { get; init; } = string.Empty;

        public string ParentDocId { get; init; } = string.Empty;
    }
}
