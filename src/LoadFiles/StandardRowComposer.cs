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
        this.orderedKeys = this.BuildOrderedKeys();
        this.headerColumns = this.orderedKeys.Select(this.HeaderName).ToList();
        this.batesSequence = request.Bates != null ? BatesSequence.FromConfig(request.Bates) : null;
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

        foreach (var fileData in processedFiles)
        {
            var wi = fileData.WorkItem;

            // Draw order must stay metadata-then-email to match historical output.
            var meta = includeMeta ? SyntheticRowValues.Metadata(wi, fileData, random, now) : default;
            var eml = includeEml ? SyntheticRowValues.Eml(wi, fileData, random, now) : default;

            var values = this.orderedKeys.Select(k => this.Resolve(k, wi, fileData, meta, eml)).ToList();
            yield return LoadFileRecordBuilder.Build(this.headerColumns, values, $"DOC{wi.Index:D8}");
        }
    }

    private List<string> BuildOrderedKeys()
    {
        var keys = new List<string>();
        if (this.IncludeAttachmentBoundaryColumns)
        {
            keys.Add("BEGATTY");
            keys.Add("ENDATTY");
        }

        keys.Add("CONTROL");
        keys.Add("PATH");

        if (this.request.Metadata.ShouldIncludeMetadataColumns(this.request.Output))
        {
            keys.AddRange(new[] { "CUSTODIAN", "DATESENT", "AUTHOR", "FILESIZE" });
        }

        if (this.request.Metadata.ShouldIncludeEmlColumns(this.request.Output))
        {
            keys.AddRange(new[] { "TO", "FROM", "SUBJECT", "SENTDATE", "ATTACHMENT" });
        }

        if (this.request.Bates != null)
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

        return keys;
    }

    private string Resolve(
        string key,
        FileWorkItem wi,
        FileData fileData,
        (string Custodian, string DateSent, string Author, string FileSize) meta,
        (string To, string From, string Subject, string SentDate, string Attachment) eml)
        => key switch
        {
            "BEGATTY" => string.Empty,
            "ENDATTY" => string.Empty,
            "CONTROL" => $"DOC{wi.Index:D8}",
            "PATH" => wi.FilePathInZip,
            "CUSTODIAN" => meta.Custodian,
            "DATESENT" => meta.DateSent,
            "AUTHOR" => meta.Author,
            "FILESIZE" => meta.FileSize,
            "TO" => eml.To,
            "FROM" => eml.From,
            "SUBJECT" => eml.Subject,
            "SENTDATE" => eml.SentDate,
            "ATTACHMENT" => eml.Attachment,
            "BATES" => this.batesSequence!.Format(wi.Index - 1).ToString(),
            "PAGECOUNT" => fileData.PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            // Whole-string Replace (not extension-only) preserves byte-for-byte parity with the
            // legacy writers; FilePathInZip folder segments never contain ".{FileType}" in practice.
            "TEXT" => wi.FilePathInZip.Replace($".{this.request.Output.FileType}", ".txt", StringComparison.Ordinal),
            _ => string.Empty,
        };
}
