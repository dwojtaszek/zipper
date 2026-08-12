using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.LoadFiles;

/// <summary>
/// Standard mode and profile-path composer for the DAT format. Builds Standard mode header
/// columns (flag-driven) and generates row values for both the Standard path (real files)
/// and the profile path (synthetic data via <see cref="DataGenerator"/>).
/// </summary>
internal sealed class DatStandardComposer
{
    private readonly FileGenerationRequest request;
    private readonly BatesSequence? batesSequence;
    private readonly IReadOnlyList<string> headerColumns;
    private readonly DataGenerator? profileGenerator;
    private readonly List<string>? profileColumnNames;
    private readonly bool hasTextPathColumn;

    public DatStandardComposer(
        FileGenerationRequest request,
        BatesSequence? batesSequence,
        IReadOnlyList<string> headerColumns,
        DataGenerator? profileGenerator = null,
        List<string>? profileColumnNames = null)
    {
        this.request = request;
        this.batesSequence = batesSequence;
        this.headerColumns = headerColumns;
        this.profileGenerator = profileGenerator;
        this.profileColumnNames = profileColumnNames;
        this.hasTextPathColumn = profileColumnNames?.Any(
            n => StandardRowValueGenerators.ByName.TryGetValue(n, out var gen) && gen is TextPathGenerator) ?? false;
    }

    /// <summary>
    /// Builds the header columns for Standard mode (non-profile). Columns depend on request flags:
    /// metadata, EML, collection metadata, Bates, TIFF page count, text, and families.
    /// </summary>
    public static List<string> BuildHeaders(FileGenerationRequest request, string? namingConvention)
    {
        var cols = new List<string> { "Control Number", "File Path" };
        if (request.Output.IsMixedFileTypes)
        {
            cols.Add("File Type");
        }

        if (request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            cols.AddRange(new[] { "Custodian", "Date Sent", "Author", "File Size" });
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            cols.AddRange(new[] { "To", "From", "CC", "Subject", "Sent Date", "Attachment" });
        }

        if (request.Metadata.ShouldIncludeCollectionMetadataColumns())
        {
            cols.AddRange(new[] { "Data Source", "Collection Date", "De-Nisted", "Dedupe Group ID", "Processing Status" });
        }

        if (request.Bates != null)
        {
            cols.Add("Bates Number");
        }

        if (request.Tiff.ShouldIncludePageCount(request.Output))
        {
            cols.Add("Page Count");
        }

        if (request.Output.WithText)
        {
            cols.Add("Extracted Text");
        }

        if (request.Metadata.WithFamilies)
        {
            cols.AddRange(new[] { "BEGATTACH", "ENDATTACH", "PARENTDOCID" });
        }

        return cols.Select(c => DatComposerShared.ApplyConvention(c, namingConvention)).ToList();
    }

    /// <summary>
    /// Composes rows for Standard mode using real processed files.
    /// </summary>
    public IEnumerable<LoadFileRecord> ComposeStandard(IReadOnlyList<FileData> processedFiles)
    {
        var generator = this.profileGenerator ?? DatComposerShared.GetEffectiveProfileGenerator(
            this.request, DatComposerShared.EffectiveNow(this.request));

        foreach (var fileData in processedFiles)
        {
            var profileValues = generator?.GenerateRow(fileData.WorkItem, fileData);
            if (this.request.Metadata.ColumnProfile is not null)
            {
                // Source Metadata surfaces only through an explicit Column Profile (REQ-203);
                // built-in fallback profiles (LegacyEml/LegacyWithMetadata) never merge.
                DatComposerShared.MergeSourceMetadata(profileValues, fileData.WorkItem.SourceMetadata);
            }
            var (parentId, childId, hasAttachment) = DatComposerShared.GetFamilyIdentifiers(
                fileData, this.request, this.batesSequence);

            yield return DatComposerShared.MakeRecord(
                this.headerColumns,
                parentId,
                this.StandardRowValues(fileData, profileValues, new DatRowContext { IdOverride = parentId, BegAttach = parentId, EndAttach = childId, ParentDocId = string.Empty }));

            if (hasAttachment)
            {
                var attach = fileData.Attachment!.Value;
                var sanitizedFilename = FamilyPlan.SanitizeAttachmentFilename(attach.filename);
                var attachmentPath = $"{fileData.WorkItem.FolderPrefix}{fileData.WorkItem.Index}_{sanitizedFilename}";
                yield return DatComposerShared.MakeRecord(
                    this.headerColumns,
                    childId,
                    this.StandardRowValues(fileData, profileValues, new DatRowContext
                    {
                        IdOverride = childId,
                        ControlOverride = this.batesSequence is null ? childId : $"DOC{fileData.WorkItem.Index:D8}_A001",
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

    /// <summary>
    /// Composes rows for the profile path (Loadfile-Only mode with an explicit ColumnProfile).
    /// Generates synthetic file data and renders rows using <see cref="StandardRowValues"/>.
    /// </summary>
    public IEnumerable<LoadFileRecord> ComposeProfile()
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
            yield return DatComposerShared.MakeRecord(
                this.headerColumns,
                parentId,
                this.StandardRowValues(fileData, parentValues, new DatRowContext { IdOverride = parentId, BegAttach = parentId, EndAttach = childId, ParentDocId = string.Empty }));

            if (hasAttachment)
            {
                var childExt = ".pdf";
                var childPath = $"NATIVES/{folderNum:D3}/{childId}{childExt}";
                yield return DatComposerShared.MakeRecord(
                    this.headerColumns,
                    childId,
                    this.StandardRowValues(fileData, parentValues, new DatRowContext
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

    private List<string> StandardRowValues(FileData fileData, Dictionary<string, string>? profileValues, DatRowContext ctx)
    {
        var wi = fileData.WorkItem;

        if (this.profileColumnNames is not null && profileValues is not null)
        {
            string batesIdentity = ctx.IdOverride ?? (this.batesSequence is not null ? this.batesSequence.Format(wi.Index - 1).ToString() : $"DOC{wi.Index:D8}");
            string controlIdentity = (!ctx.IsChild && wi.ControlNumberOverride is not null) ? wi.ControlNumberOverride : batesIdentity;
            var fileSize = ctx.FileSizeOverride ?? (ctx.IsChild ? null : fileData.DataLength.ToString(System.Globalization.CultureInfo.InvariantCulture));

            var standard = new StandardRowResolution
            {
                ControlIdentity = controlIdentity,
                BatesIdentity = batesIdentity,
                NativePath = ctx.FilePathOverride ?? wi.FilePathInZip,
                FileSize = fileSize,
                TextPath = this.request.Output.WithText && this.hasTextPathColumn ? this.StandardTextPath(fileData, ctx) : string.Empty,
                PageCount = fileData.PageCount,
                IsChild = ctx.IsChild,
                WithFamilies = this.request.Metadata.WithFamilies,
                WithText = this.request.Output.WithText,
                BegAttach = ctx.BegAttach,
                EndAttach = ctx.EndAttach,
                ParentDocId = ctx.ParentDocId,
            };

#pragma warning disable S2245
            var baseCtx = new ColumnGenerationContext
            {
                NativeFileIndex = wi.Index,
                FolderNumber = wi.FolderNumber,
                DocumentIndex = 0,
                Seeded = Random.Shared,
                Now = DatComposerShared.EffectiveNow(this.request),
                FileData = fileData,
            };
#pragma warning restore S2245

            var result = new List<string>(this.profileColumnNames.Count);
            for (int i = 0; i < this.profileColumnNames.Count; i++)
            {
                var n = this.profileColumnNames[i];
                if (StandardRowValueGenerators.ByName.TryGetValue(n, out var generator))
                {
                    var generationCtx = baseCtx with
                    {
                        StandardRow = standard,
                        ProfileValue = profileValues.TryGetValue(n, out var profileValue) ? profileValue : null,
                    };
                    result.Add(generator.Generate(generationCtx));
                }
                else
                {
                    result.Add(DatComposerShared.ResolveHashColumn(n.ToUpperInvariant(), fileData) ?? (profileValues.TryGetValue(n, out var x) ? x : string.Empty));
                }
            }

            return result;
        }

        var recordType = wi.EffectiveFileType(this.request);
        var recordIsEml = string.Equals(recordType, "eml", StringComparison.Ordinal);
        var recordIsTiff = string.Equals(recordType, "tiff", StringComparison.Ordinal);

        var v = new List<string>(this.headerColumns.Count)
        {
            ctx.ControlOverride ?? wi.ControlNumberOverride ?? $"DOC{wi.Index:D8}",
            ctx.FilePathOverride ?? wi.FilePathInZip,
        };

        if (this.request.Output.IsMixedFileTypes)
        {
            v.Add(ctx.IsChild && fileData.Attachment.HasValue
                ? System.IO.Path.GetExtension(fileData.Attachment.Value.filename).TrimStart('.').ToUpperInvariant()
                : recordType.ToUpperInvariant());
        }

        if (this.request.Metadata.ShouldIncludeMetadataColumns(this.request.Output))
        {
            v.Add(profileValues?.GetValueOrDefault("CUSTODIAN") ?? string.Empty);
            v.Add(ctx.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("DATESENT") ?? string.Empty));
            v.Add(ctx.IsChild ? string.Empty : (profileValues?.GetValueOrDefault("AUTHOR") ?? string.Empty));
            v.Add(ctx.FileSizeOverride ?? (profileValues?.GetValueOrDefault("FILESIZE") ?? fileData.DataLength.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (this.request.Metadata.ShouldIncludeEmlColumns(this.request.Output))
        {
            // In a File Type mix, Email Metadata appears only on Email records.
            var emlValues = !ctx.IsChild && recordIsEml;
            var emailCc = string.Empty;
            if (emlValues)
            {
                emailCc = profileValues?.GetValueOrDefault("EMAILCC") ?? (fileData.Email?.Cc ?? (fileData.Email != null ? string.Empty : $"cc{wi.Index}@example.com"));
            }

            v.Add(emlValues ? (profileValues?.GetValueOrDefault("EMAILTO") ?? $"recipient{wi.Index}@example.com") : string.Empty);
            v.Add(emlValues ? (profileValues?.GetValueOrDefault("EMAILFROM") ?? $"sender{wi.Index}@example.com") : string.Empty);
            v.Add(emailCc);
            v.Add(emlValues ? (profileValues?.GetValueOrDefault("EMAILSUBJECT") ?? $"Email Subject {wi.Index}") : string.Empty);
            v.Add(emlValues ? (profileValues?.GetValueOrDefault("EMAILSENTDATE") ?? string.Empty) : string.Empty);
            v.Add(emlValues ? (profileValues?.GetValueOrDefault("EMAILATTACHMENT") ?? string.Empty) : string.Empty);
        }

        if (this.request.Metadata.ShouldIncludeCollectionMetadataColumns())
        {
#pragma warning disable S2245
            var colRandom = this.request.Metadata.Seed.HasValue ? new Random(unchecked((int)(this.request.Metadata.Seed.Value + wi.Index))) : Random.Shared;
#pragma warning restore S2245
            var now = DatComposerShared.EffectiveNow(this.request);
            v.Add(profileValues?.GetValueOrDefault("DATA_SOURCE") ?? CollectionMetadataValues.DataSources[colRandom.Next(CollectionMetadataValues.DataSources.Length)]);
            v.Add(profileValues?.GetValueOrDefault("COLLECTION_DATE") ?? now.AddDays(-colRandom.Next(1, 30)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            v.Add(profileValues?.GetValueOrDefault("DENISTED") ?? (colRandom.Next(100) < 85 ? "YES" : "NO"));
            v.Add(profileValues?.GetValueOrDefault("DEDUPE_GROUP_ID") ?? $"GRP{colRandom.Next(1, 1000):D6}");
            v.Add(profileValues?.GetValueOrDefault("PROCESSING_STATUS") ?? CollectionMetadataValues.ProcessingStatuses[colRandom.Next(CollectionMetadataValues.ProcessingStatuses.Length)]);
        }

        if (this.request.Bates != null)
        {
            v.Add(ctx.IdOverride ?? this.batesSequence!.Format(wi.Index - 1).ToString());
        }

        if (this.request.Tiff.ShouldIncludePageCount(this.request.Output))
        {
            // In a File Type mix, Page Count appears only on TIFF records.
            var pageCount = string.Empty;
            if (ctx.IsChild)
            {
                pageCount = "1";
            }
            else if (recordIsTiff)
            {
                pageCount = fileData.PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            v.Add(pageCount);
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

    private string StandardTextPath(FileData fileData, DatRowContext ctx)
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
            return $"{wi.FolderPrefix}{wi.Index}_{attachmentTextFileName}";
        }

        return TextPathHelper.GetTextPath(wi.FilePathInZip);
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
}
