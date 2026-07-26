namespace Zipper.LoadFiles;

/// <summary>
/// Loadfile-Only mode composer for the DAT format. Generates synthetic load file records
/// without real source files, using deterministic random data when a seed is configured.
/// </summary>
internal sealed class DatLoadfileOnlyComposer
{
    private readonly FileGenerationRequest request;
    private readonly BatesSequence? batesSequence;
    private readonly IReadOnlyList<string> headerColumns;

    public DatLoadfileOnlyComposer(
        FileGenerationRequest request,
        BatesSequence? batesSequence,
        IReadOnlyList<string> headerColumns)
    {
        this.request = request;
        this.batesSequence = batesSequence;
        this.headerColumns = headerColumns;
    }

    /// <summary>
    /// Builds the header columns for Loadfile-Only mode (no profile path).
    /// </summary>
    public static List<string> BuildHeaders(FileGenerationRequest request, string? namingConvention)
    {
        var lfCols = new List<string>
        {
            "Control Number", "File Path", "Custodian", "Date Sent", "Author", "File Size",
            "EmailSubject", "EmailFrom", "EmailTo", "EmailCC", "EmailSentDate", "ExtractedText",
        };
        if (request.Metadata.WithFamilies)
        {
            lfCols.AddRange(new[] { "BEGATTACH", "ENDATTACH", "PARENTDOCID" });
        }
        return lfCols.Select(c => DatComposerShared.ApplyConvention(c, namingConvention)).ToList();
    }

    public IEnumerable<LoadFileRecord> Compose()
    {
        var now = DatComposerShared.EffectiveNow(this.request);
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
            var ccAddr = $"cc{i}@example.com";
            var sentTime = now.AddDays(-random.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            var filePath = $"NATIVES\\{(i % 50) + 1:D3}\\{parentId}.pdf";
            var extractedText = $"Sample extracted text content for document {parentId}.";

            var parentRecordValues = new List<string>
            {
                parentId, filePath, custodian, dateSent, author, fileSize,
                subjLine, senderAddr, recipientAddr, ccAddr, sentTime, extractedText,
            };

            if (this.request.Metadata.WithFamilies)
            {
                parentRecordValues.AddRange(new[] { parentId, childId, string.Empty });
            }

            yield return DatComposerShared.MakeRecord(this.headerColumns, parentId, parentRecordValues);

            if (hasAttachment)
            {
                var childFileSize = random.Next(1024, 10485760).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var childPath = $"NATIVES\\{(i % 50) + 1:D3}\\{childId}.pdf";
                var childExtractedText = $"Sample extracted text content for document {childId}.";

                var childRecordValues = new List<string>
                {
                    childId, childPath, custodian, string.Empty, string.Empty, childFileSize,
                    string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, childExtractedText,
                };

                if (this.request.Metadata.WithFamilies)
                {
                    childRecordValues.AddRange(new[] { parentId, childId, parentId });
                }

                yield return DatComposerShared.MakeRecord(this.headerColumns, childId, childRecordValues);
            }
        }
    }
}
