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
            var lfCols = new List<string>
            {
                "Control Number", "File Path", "Custodian", "Date Sent", "Author", "File Size",
                "EmailSubject", "EmailFrom", "EmailTo", "EmailSentDate", "ExtractedText",
            };
            if (this.request.Metadata.WithFamilies)
            {
                lfCols.AddRange(new[] { "BEGATTACH", "ENDATTACH", "PARENTDOCID" });
            }
            return lfCols.Select(this.Apply).ToList();
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
                this.StandardRowValues(fileData, profileValues, new RowCtx { IdOverride = parentId, BegAttach = parentId, EndAttach = childId, ParentDocId = string.Empty }));

            if (hasAttachment)
            {
                var attach = fileData.Attachment!.Value;
                var sanitizedFilename = FamilyPlan.SanitizeAttachmentFilename(attach.filename);
                var attachmentPath = $"{fileData.WorkItem.FolderName}/{fileData.WorkItem.Index}_{sanitizedFilename}";
                yield return this.MakeRecord(
                    childId,
                    this.StandardRowValues(fileData, profileValues, new RowCtx
                    {
                        IdOverride = childId,
                        ControlOverride = $"DOC{fileData.WorkItem.Index:D8}_A001",
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
                        val = this.request.Output.WithText ? this.StandardTextPath(fileData, ctx) : (profileValues.TryGetValue(n, out var tp) ? tp : string.Empty);
                        break;
                    default:
                        val = ResolveHashColumn(upper, fileData) ?? (profileValues.TryGetValue(n, out var x) ? x : string.Empty);
                        break;
                }
                result.Add(val);
            }
            return result;
        }

        var v = new List<string>(this.headerColumns.Count)
        {
            ctx.ControlOverride ?? $"DOC{wi.Index:D8}",
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
            string filename;
            if (fileData.Attachment.HasValue)
            {
                filename = fileData.Attachment.Value.filename;
            }
            else if (ctx.FilePathOverride is not null)
            {
                filename = Path.GetFileName(ctx.FilePathOverride);
            }
            else
            {
                filename = $"{ctx.IdOverride ?? $"{wi.Index}_A001"}.pdf";
            }
            var attachmentTextFileName = $"{Path.GetFileNameWithoutExtension(FamilyPlan.SanitizeAttachmentFilename(filename))}.txt";
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
                this.ProductionRowValues(fileData, new RowCtx { IdOverride = parentId, BegAttach = parentId, EndAttach = childId, ParentDocId = string.Empty }));

            if (hasAttachment)
            {
                var attach = fileData.Attachment!.Value;
                var childExt = Path.GetExtension(FamilyPlan.SanitizeAttachmentFilename(attach.filename));
                var childBates = childId;
                var childNativePath = Path.Combine("NATIVES", fileData.WorkItem.FolderName, $"{childBates}{childExt}").Replace('/', '\\');
                var childTextPath = Path.Combine("TEXT", fileData.WorkItem.FolderName, $"{childBates}.txt").Replace('/', '\\');
                var childImagePath = Path.Combine("IMAGES", fileData.WorkItem.FolderName, $"{childBates}.tif").Replace('/', '\\');

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
        var textPath = ctx.TextPathOverride ?? (nativePath.StartsWith("NATIVES\\", StringComparison.OrdinalIgnoreCase) ? "TEXT\\" + nativePath.Substring(8) : nativePath).Replace($".{this.request.Output.FileType}", ".txt", StringComparison.Ordinal);
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
            var parentId = this.batesSequence is not null
                ? this.batesSequence.Next().ToString()
                : $"DOC{i:D8}";

            bool hasAttachment = FamilyPlan.HasAttachment(this.request, i);
            string childId = hasAttachment ? $"{parentId}_A001" : parentId;

            var custodian = $"Custodian {(i % 10) + 1}";
            var dateSent = now.AddDays(-random.Next(1, 365)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            var author = $"Author {random.Next(1, 100):D3}";
            var fileSize = random.Next(1024, 10485760).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var subjLine = $"Email Subject {i}";
            var senderAddr = $"sender{i}@example.com";
            var recipientAddr = $"recipient{i}@example.com";
            var sentTime = now.AddDays(-random.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            var filePath = $"NATIVES\\{(i % 50) + 1:D3}\\{parentId}.pdf";
            var extractedText = $"Sample extracted text content for document {parentId}.";

            var parentRecordValues = new List<string>
            {
                parentId, filePath, custodian, dateSent, author, fileSize,
                subjLine, senderAddr, recipientAddr, sentTime, extractedText,
            };

            if (this.request.Metadata.WithFamilies)
            {
                parentRecordValues.AddRange(new[] { parentId, childId, string.Empty });
            }

            yield return this.MakeRecord(parentId, parentRecordValues);

            if (hasAttachment)
            {
                var childFileSize = random.Next(1024, 10485760).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var childPath = $"NATIVES\\{(i % 50) + 1:D3}\\{childId}.pdf";
                var childExtractedText = $"Sample extracted text content for document {childId}.";

                var childRecordValues = new List<string>
                {
                    childId, childPath, custodian, string.Empty, string.Empty, childFileSize,
                    string.Empty, string.Empty, string.Empty, string.Empty, childExtractedText,
                };

                if (this.request.Metadata.WithFamilies)
                {
                    childRecordValues.AddRange(new[] { parentId, childId, parentId });
                }

                yield return this.MakeRecord(childId, childRecordValues);
            }
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
        var hashConfig = this.request.Hash;

        for (long i = 1; i <= this.request.Output.FileCount; i++)
        {
            var folderNum = (int)((i - 1) % 50) + 1;
            var workItem = new FileWorkItem
            {
                Index = i,
                FolderNumber = folderNum,
                FolderName = $"{folderNum:D3}",
                FileName = $"DOC{i:D8}.{fileTypeLower}",
                FilePathInZip = $"NATIVES/{folderNum:D3}/DOC{i:D8}.{fileTypeLower}",
            };

            var fileData = new FileData
            {
                WorkItem = workItem,
                DataLength = rowRandom.Next(1024, 10_485_760),
                PageCount = rowRandom.Next(1, 11),
                Hashes = hashConfig.Mode == Config.HashMode.Simulated ? GenerateSimulatedHashes(workItem) : null,
            };

            bool hasAttachment = FamilyPlan.HasAttachment(this.request, i);
            string parentId = this.batesSequence is not null
                ? this.batesSequence.Format(i - 1).ToString()
                : $"DOC{i:D8}";
            string childId = hasAttachment ? $"{parentId}_A001" : parentId;

            var parentValues = generator.GenerateRow(workItem, fileData);
            yield return this.MakeRecord(
                parentId,
                this.StandardRowValues(fileData, parentValues, new RowCtx { IdOverride = parentId, BegAttach = parentId, EndAttach = childId, ParentDocId = string.Empty }));

            if (hasAttachment)
            {
                var childExt = ".pdf";
                var childPath = $"NATIVES/{folderNum:D3}/{childId}{childExt}";
                yield return this.MakeRecord(
                    childId,
                    this.StandardRowValues(fileData, parentValues, new RowCtx
                    {
                        IdOverride = childId,
                        ControlOverride = $"DOC{i:D8}_A001",
                        FilePathOverride = childPath,
                        FileSizeOverride = rowRandom.Next(1024, 10_485_760).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        IsChild = true,
                        BegAttach = parentId,
                        EndAttach = childId,
                        ParentDocId = parentId,
                    }));
            }
        }
    }

    private IReadOnlyDictionary<Config.HashAlgorithm, string>? GenerateSimulatedHashes(FileWorkItem workItem)
    {
        var hashConfig = this.request.Hash;
        if (!hashConfig.IsEnabled)
        {
            return null;
        }

        var dict = new Dictionary<Config.HashAlgorithm, string>(hashConfig.Algorithms.Count);
        var rng = Config.HashUtility.CreateSeededRandom(this.request, workItem.Index);
        foreach (var algo in hashConfig.Algorithms)
            dict[algo] = Config.HashUtility.GenerateSimulatedHash(algo, rng);

        return dict;
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

    private static string? ResolveHashColumn(string upperColumnName, FileData fileData)
    {
        if (fileData.Hashes is null)
        {
            return null;
        }

        var algo = upperColumnName switch
        {
            "MD5HASH" or "MD5_HASH" or "MD5 HASH" => Config.HashAlgorithm.MD5,
            "SHA1HASH" or "SHA1_HASH" or "SHA1 HASH" => Config.HashAlgorithm.SHA1,
            "SHA256HASH" or "SHA256_HASH" or "SHA256 HASH" => Config.HashAlgorithm.SHA256,
            _ => (Config.HashAlgorithm?)null,
        };

        if (algo.HasValue && fileData.Hashes.TryGetValue(algo.Value, out var hashValue))
        {
            return hashValue;
        }

        return null;
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

        public string? ControlOverride { get; init; }

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
