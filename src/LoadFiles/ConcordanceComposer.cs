namespace Zipper.LoadFiles;

/// <summary>
/// Column authority for the Concordance DAT format (standard generation only). Columns use the
/// fixed Concordance field names; BEGATTY/ENDATTY are present but left empty (parent/child
/// attachment ranges are not tracked). One record per file; values are raw and the serializer
/// applies þ-wrapping and quote-doubling.
/// </summary>
internal sealed class ConcordanceComposer : ILoadFileComposer
{
    private readonly FileGenerationRequest request;
    private readonly List<string> headerColumns;

    public ConcordanceComposer(FileGenerationRequest request)
    {
        this.request = request;
        this.headerColumns = this.BuildHeaderColumns();
    }

    public IReadOnlyList<string> HeaderColumns => this.headerColumns;

    public IEnumerable<LoadFileRecord> Compose(IReadOnlyList<FileData> processedFiles)
    {
#pragma warning disable S2245
        var random = this.request.Metadata.Seed.HasValue ? new Random(this.request.Metadata.Seed.Value) : Random.Shared;
#pragma warning restore S2245
        var now = this.request.Metadata.Seed.HasValue ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) : DateTime.UtcNow;

        foreach (var fileData in processedFiles)
        {
            var wi = fileData.WorkItem;
            var v = new List<string>(this.headerColumns.Count)
            {
                string.Empty, // BEGATTY
                string.Empty, // ENDATTY
                $"DOC{wi.Index:D8}",
                wi.FilePathInZip,
            };

            if (this.request.Metadata.ShouldIncludeMetadataColumns(this.request.Output))
            {
                var (custodian, dateSent, author, fileSize) = SyntheticRowValues.Metadata(wi, fileData, random, now);
                v.Add(custodian);
                v.Add(dateSent);
                v.Add(author);
                v.Add(fileSize);
            }

            if (this.request.Metadata.ShouldIncludeEmlColumns(this.request.Output))
            {
                var (to, from, subject, sentDate, attachment) = SyntheticRowValues.Eml(wi, fileData, random, now);
                v.Add(to);
                v.Add(from);
                v.Add(subject);
                v.Add(sentDate);
                v.Add(attachment);
            }

            if (this.request.Bates != null)
            {
                v.Add(BatesNumberGenerator.Generate(this.request.Bates, wi.Index - 1));
            }

            if (this.request.Tiff.ShouldIncludePageCount(this.request.Output))
            {
                v.Add(fileData.PageCount.ToString());
            }

            if (this.request.Output.WithText)
            {
                v.Add(wi.FilePathInZip.Replace($".{this.request.Output.FileType}", ".txt"));
            }

            yield return this.MakeRecord($"DOC{wi.Index:D8}", v);
        }
    }

    private List<string> BuildHeaderColumns()
    {
        var cols = new List<string> { "BEGATTY", "ENDATTY", "CONTROLNUMBER", "PATH" };
        if (this.request.Metadata.ShouldIncludeMetadataColumns(this.request.Output))
        {
            cols.AddRange(new[] { "CUSTODIAN", "DATESENT", "AUTHOR", "FILESIZE" });
        }

        if (this.request.Metadata.ShouldIncludeEmlColumns(this.request.Output))
        {
            cols.AddRange(new[] { "TO", "FROM", "SUBJECT", "SENTDATE", "ATTACHMENT" });
        }

        if (this.request.Bates != null)
        {
            cols.Add("BATES");
        }

        if (this.request.Tiff.ShouldIncludePageCount(this.request.Output))
        {
            cols.Add("PAGECOUNT");
        }

        if (this.request.Output.WithText)
        {
            cols.Add("TEXT_PATH");
        }

        return cols;
    }

    private LoadFileRecord MakeRecord(string recordId, List<string> orderedValues)
    {
        if (orderedValues.Count != this.headerColumns.Count)
        {
            throw new InvalidOperationException(
                $"ConcordanceComposer value count {orderedValues.Count} does not match header column count {this.headerColumns.Count}.");
        }

        var values = new Dictionary<string, string>(this.headerColumns.Count);
        for (int i = 0; i < this.headerColumns.Count; i++)
        {
            values[this.headerColumns[i]] = orderedValues[i];
        }

        return new LoadFileRecord { Columns = this.headerColumns, Values = values, RecordId = recordId };
    }
}
