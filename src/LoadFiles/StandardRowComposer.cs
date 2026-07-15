namespace Zipper.LoadFiles;

/// <summary>
/// Shared column authority for the single-record-per-file delimited formats (CSV and
/// Concordance). The column pipeline, request-driven conditionals, synthetic value generation
/// (fixed random draw order), and record assembly live here; subclasses supply only the
/// per-format header names and whether the attachment-boundary columns are present.
/// </summary>
internal abstract class StandardRowComposer : ILoadFileComposer
{
    protected readonly FileGenerationRequest request;
    private readonly List<string> orderedKeys;
    private readonly List<string> headerColumns;
    private readonly BatesSequence? batesSequence;

    protected StandardRowComposer(FileGenerationRequest request)
    {
        this.request = request;
        this.batesSequence = request.Bates != null ? BatesSequence.FromConfig(request.Bates) : null;
        this.orderedKeys = this.BuildOrderedKeys();
        this.headerColumns = this.orderedKeys.Select(this.HeaderName).ToList();
    }

    /// <summary>Gets the hash column keys to emit based on request configuration.</summary>
    protected static IReadOnlyList<string> GetHashColumnKeys(FileGenerationRequest request)
    {
        if (!request.Hash.IsEnabled)
        {
            return Array.Empty<string>();
        }

        var keys = new List<string>(request.Hash.Algorithms.Count);
        if (request.Hash.Algorithms.Contains(Config.HashAlgorithm.MD5))
            keys.Add("MD5HASH");
        if (request.Hash.Algorithms.Contains(Config.HashAlgorithm.SHA1))
            keys.Add("SHA1HASH");
        if (request.Hash.Algorithms.Contains(Config.HashAlgorithm.SHA256))
            keys.Add("SHA256HASH");
        return keys;
    }

    public IReadOnlyList<string> HeaderColumns => this.headerColumns;

    /// <summary>Gets a value indicating whether leading BEGATTY/ENDATTY columns are emitted.</summary>
    protected abstract bool IncludeAttachmentBoundaryColumns { get; }

    /// <summary>Maps a canonical column key to this format's header name.</summary>
    protected abstract string HeaderName(string columnKey);

    public IEnumerable<LoadFileRecord> Compose(IReadOnlyList<FileData> processedFiles)
    {
#pragma warning disable S2245
        var random = this.request.Metadata.Seed.HasValue ? new Random(this.request.Metadata.Seed.Value) : Random.Shared;
#pragma warning restore S2245
        var now = this.request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

        bool includeMeta = this.request.Metadata.ShouldIncludeMetadataColumns(this.request.Output);
        bool includeEml = this.request.Metadata.ShouldIncludeEmlColumns(this.request.Output);
        bool includeCollectionMeta = this.request.Metadata.ShouldIncludeCollectionMetadataColumns();

        foreach (var fileData in processedFiles)
        {
            var wi = fileData.WorkItem;

            // Draw order must stay metadata-then-email to match historical output.
            var meta = includeMeta ? SyntheticRowValues.Metadata(wi, fileData, random, now) : default;
            var eml = includeEml ? SyntheticRowValues.Eml(wi, fileData, random, now) : default;
            var colMeta = includeCollectionMeta ? SyntheticRowValues.CollectionMetadata(wi, random, now) : default;

            bool hasAttachment = this.request.Metadata.WithFamilies && this.request.Output.IsEml && fileData.Attachment.HasValue;
            string parentId = this.batesSequence is not null
                ? this.batesSequence.Format(wi.Index - 1).ToString()
                : $"DOC{wi.Index:D8}";
            string childId = hasAttachment ? $"{parentId}_A001" : parentId;

            var parentCtx = new RowCtx
            {
                IdOverride = parentId,
                BegAttach = parentId,
                EndAttach = childId,
                ParentDocId = string.Empty
            };

            var values = this.orderedKeys.Select(k => this.Resolve(k, wi, fileData, meta, eml, colMeta, parentCtx)).ToList();
            yield return LoadFileRecordBuilder.Build(this.headerColumns, values, parentId);

            if (hasAttachment)
            {
                var attach = fileData.Attachment!.Value;
                var sanitizedFilename = FamilyPlan.SanitizeAttachmentFilename(attach.filename);
                var attachmentPath = $"{wi.FolderName}/{wi.Index}_{sanitizedFilename}";
                var childCtx = new RowCtx
                {
                    IdOverride = childId,
                    ControlOverride = $"DOC{wi.Index:D8}_A001",
                    FilePathOverride = attachmentPath,
                    FileSizeOverride = attach.content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    IsChild = true,
                    BegAttach = parentId,
                    EndAttach = childId,
                    ParentDocId = parentId,
                };
                var childValues = this.orderedKeys.Select(k => this.Resolve(k, wi, fileData, meta, eml, colMeta, childCtx)).ToList();
                yield return LoadFileRecordBuilder.Build(this.headerColumns, childValues, childId);
            }
        }
    }

    private List<string> BuildOrderedKeys()
    {
        var keys = new List<string>();
        if (this.IncludeAttachmentBoundaryColumns)
        {
            keys.AddRange(new[] { "BEGATTY", "ENDATTY" });
        }

        keys.AddRange(new[] { "CONTROL", "PATH" });

        if (this.request.Metadata.ShouldIncludeMetadataColumns(this.request.Output))
        {
            keys.AddRange(new[] { "CUSTODIAN", "DATESENT", "AUTHOR", "FILESIZE" });
        }

        if (this.request.Metadata.ShouldIncludeEmlColumns(this.request.Output))
        {
            keys.AddRange(new[] { "TO", "FROM", "SUBJECT", "SENTDATE", "ATTACHMENT" });
        }

        if (this.request.Metadata.ShouldIncludeCollectionMetadataColumns())
        {
            keys.AddRange(new[] { "DATA_SOURCE", "COLLECTION_DATE", "DENISTED", "DEDUPE_GROUP_ID", "PROCESSING_STATUS" });
        }

        if (this.batesSequence is not null)
        {
            keys.Add("BATES");
        }

        if (this.request.Tiff.ShouldIncludePageCount(this.request.Output))
        {
            keys.Add("PAGECOUNT");
        }

        if (this.request.Output.WithText)
        {
            keys.Add("TEXT");
        }

        if (this.request.Metadata.WithFamilies)
        {
            keys.AddRange(new[] { "BEGATTACH", "ENDATTACH", "PARENTDOCID" });
        }

        if (this.request.Hash.IsEnabled)
        {
            keys.AddRange(GetHashColumnKeys(this.request));
        }

        return keys;
    }

    private string Resolve(
        string key,
        FileWorkItem wi,
        FileData fileData,
        (string Custodian, string DateSent, string Author, string FileSize) meta,
        (string To, string From, string Subject, string SentDate, string Attachment) eml,
        (string DataSource, string CollectionDate, string DeNisted, string DedupeGroupId, string ProcessingStatus) colMeta,
        RowCtx ctx)
        => key switch
        {
            "BEGATTY" => this.request.Metadata.WithFamilies ? ctx.BegAttach : string.Empty,
            "ENDATTY" => this.request.Metadata.WithFamilies ? ctx.EndAttach : string.Empty,
            "CONTROL" => ctx.ControlOverride ?? $"DOC{wi.Index:D8}",
            "PATH" => ctx.FilePathOverride ?? wi.FilePathInZip,
            "CUSTODIAN" => meta.Custodian,
            "DATESENT" => ctx.IsChild ? string.Empty : meta.DateSent,
            "AUTHOR" => ctx.IsChild ? string.Empty : meta.Author,
            "FILESIZE" => ctx.FileSizeOverride ?? meta.FileSize,
            "TO" => ctx.IsChild ? string.Empty : eml.To,
            "FROM" => ctx.IsChild ? string.Empty : eml.From,
            "SUBJECT" => ctx.IsChild ? string.Empty : eml.Subject,
            "SENTDATE" => ctx.IsChild ? string.Empty : eml.SentDate,
            "ATTACHMENT" => ctx.IsChild ? string.Empty : eml.Attachment,
            "DATA_SOURCE" => colMeta.DataSource,
            "COLLECTION_DATE" => colMeta.CollectionDate,
            "DENISTED" => colMeta.DeNisted,
            "DEDUPE_GROUP_ID" => colMeta.DedupeGroupId,
            "PROCESSING_STATUS" => colMeta.ProcessingStatus,
            "BATES" => ctx.IdOverride ?? this.batesSequence!.Format(wi.Index - 1).ToString(),
            "PAGECOUNT" => (ctx.IsChild ? 1 : fileData.PageCount).ToString(System.Globalization.CultureInfo.InvariantCulture),
            // Whole-string Replace (not extension-only) preserves byte-for-byte parity with the
            // legacy writers; FilePathInZip folder segments never contain ".{FileType}" in practice.
            "TEXT" => ctx.IsChild
                ? $"{wi.FolderName}/{wi.Index}_{System.IO.Path.GetFileNameWithoutExtension(FamilyPlan.SanitizeAttachmentFilename(fileData.Attachment!.Value.filename))}.txt"
                : wi.FilePathInZip.Replace($".{this.request.Output.FileType}", ".txt", StringComparison.Ordinal),
            "BEGATTACH" => ctx.BegAttach,
            "ENDATTACH" => ctx.EndAttach,
            "PARENTDOCID" => ctx.ParentDocId,
            "MD5HASH" => ResolveHashFromFileData(fileData, Config.HashAlgorithm.MD5),
            "SHA1HASH" => ResolveHashFromFileData(fileData, Config.HashAlgorithm.SHA1),
            "SHA256HASH" => ResolveHashFromFileData(fileData, Config.HashAlgorithm.SHA256),
            _ => string.Empty,
        };

    private static string ResolveHashFromFileData(FileData fileData, Config.HashAlgorithm algorithm)
        => fileData.Hashes is not null && fileData.Hashes.TryGetValue(algorithm, out var hash)
            ? hash
            : string.Empty;

    protected sealed record RowCtx
    {
        public string? IdOverride { get; init; }

        public string? ControlOverride { get; init; }

        public string? FilePathOverride { get; init; }

        public string? FileSizeOverride { get; init; }

        public bool IsChild { get; init; }

        public string BegAttach { get; init; } = string.Empty;

        public string EndAttach { get; init; } = string.Empty;

        public string ParentDocId { get; init; } = string.Empty;
    }
}

