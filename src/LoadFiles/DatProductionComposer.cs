namespace Zipper.LoadFiles;

/// <summary>
/// Production Set mode composer for the DAT format. Builds Production Set header columns
/// (including redaction columns when enabled) and generates row values with Bates numbering,
/// native/image/text paths, custodian, and family-attachment expansion.
/// </summary>
internal sealed class DatProductionComposer
{
    private readonly FileGenerationRequest request;
    private readonly BatesSequence? batesSequence;
    private readonly IReadOnlyList<string> headerColumns;

    public DatProductionComposer(
        FileGenerationRequest request,
        BatesSequence? batesSequence,
        IReadOnlyList<string> headerColumns)
    {
        this.request = request;
        this.batesSequence = batesSequence;
        this.headerColumns = headerColumns;
    }

    /// <summary>
    /// Builds the header columns for Production Set mode, including redaction, EML, and
    /// family columns when the corresponding request flags are set.
    /// </summary>
    public static List<string> BuildHeaders(FileGenerationRequest request, string? namingConvention)
    {
        var headers = new List<string> { "DOCID", "BATES_NUMBER", "VOLUME", "NATIVE_PATH", "TEXT_PATH", "IMAGE_PATH", "CUSTODIAN", "DATE_CREATED", "FILE_SIZE", "FILE_TYPE" };
        if (request.Production.RedactedProduction)
        {
            headers.AddRange(new[] { "REDACTED_IMAGE_PATH", "REDACTED_TEXT_PATH", "NATIVE_WITHHELD", "REDACTION_REASON" });
        }

        if (request.Metadata.ShouldIncludeEmlColumns(request.Output))
        {
            headers.AddRange(new[] { "Attachment", "EmailSubject", "EmailFrom", "EmailTo", "EmailCC", "EmailSentDate" });
        }

        if (request.Metadata.WithFamilies)
        {
            headers.AddRange(new[] { "BEGATTACH", "ENDATTACH", "PARENTDOCID" });
        }

        return headers.Select(h => DatComposerShared.ApplyConvention(h, namingConvention)).ToList();
    }

    public IEnumerable<LoadFileRecord> Compose(IReadOnlyList<FileData> processedFiles)
    {
        foreach (var fileData in processedFiles)
        {
            var (parentId, childId, hasAttachment) = DatComposerShared.GetFamilyIdentifiers(
                fileData, this.request, this.batesSequence);

            yield return DatComposerShared.MakeRecord(
                this.headerColumns,
                parentId,
                this.ProductionRowValues(fileData, new DatRowContext { IdOverride = parentId, BegAttach = parentId, EndAttach = childId, ParentDocId = string.Empty }));

            if (hasAttachment)
            {
                var attach = fileData.Attachment!.Value;
                var childExt = Path.GetExtension(FamilyPlan.SanitizeAttachmentFilename(attach.filename));
                var childBates = childId;
                var childNativePath = Path.Combine("NATIVES", fileData.WorkItem.FolderName, $"{childBates}{childExt}").Replace('/', '\\');
                var childTextPath = Path.Combine("TEXT", fileData.WorkItem.FolderName, $"{childBates}.txt").Replace('/', '\\');
                var childImagePath = Path.Combine("IMAGES", fileData.WorkItem.FolderName, $"{childBates}.tif").Replace('/', '\\');

                var childRedactedImageRelPath = this.request.Production.RedactedProduction
                    ? Path.Combine("REDACTED", "IMAGES", fileData.WorkItem.FolderName, $"{childBates}.tif").Replace('/', '\\')
                    : null;
                var childRedactedTextRelPath = this.request.Production.RedactedProduction && this.request.Output.WithText
                    ? Path.Combine("REDACTED", "TEXT", fileData.WorkItem.FolderName, $"{childBates}.txt").Replace('/', '\\')
                    : null;

                yield return DatComposerShared.MakeRecord(
                    this.headerColumns,
                    childId,
                    this.ProductionRowValues(fileData, new DatRowContext
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
                        RedactedImageRelPathOverride = childRedactedImageRelPath,
                        RedactedTextRelPathOverride = childRedactedTextRelPath,
                        NativeWithheldOverride = "NO",
                        RedactionReasonOverride = fileData.RedactionReason,
                    }));
            }
        }
    }

    private List<string> ProductionRowValues(FileData fileData, DatRowContext ctx)
    {
        var wi = fileData.WorkItem;
        var batesNumber = ctx.IdOverride ?? this.batesSequence!.Format(wi.Index - 1).ToString();
        var imagePath = ctx.ImagePathOverride ?? wi.FilePathInZip.Replace("NATIVES", "IMAGES", StringComparison.OrdinalIgnoreCase)
            .Replace(Path.GetExtension(wi.FilePathInZip), ".tif", StringComparison.Ordinal);
        // FilePathInZip always uses forward slashes (ZIP spec); replace '/' directly so the
        // backslash normalization also works on Windows (where DirectorySeparatorChar is '\').
        var nativePath = ctx.NativePathOverride ?? fileData.NativePathOverride ?? wi.FilePathInZip.Replace('/', '\\');
        // Derive text path from the original FilePathInZip (not the overridden nativePath) so
        // replace-with-placeholder policy doesn't produce wrong text paths in the DAT.
        var originalNativePath = wi.FilePathInZip.Replace('/', '\\');
        var textPath = ctx.TextPathOverride ?? (originalNativePath.StartsWith("NATIVES\\", StringComparison.OrdinalIgnoreCase) ? "TEXT\\" + originalNativePath.Substring(8) : originalNativePath).Replace($".{wi.EffectiveFileType(this.request)}", ".txt", StringComparison.Ordinal);
        var imagesPath = imagePath.Replace('/', '\\');

#pragma warning disable S2245
        var random = this.request.Metadata.Seed.HasValue ? new Random(unchecked((int)(this.request.Metadata.Seed.Value + wi.Index))) : Random.Shared;
#pragma warning restore S2245
        var now = DatComposerShared.EffectiveNow(this.request);
        var maxCustodians = Math.Max(2, this.request.Metadata.CustodianCountOverride ?? 10);
        var custodianProd = $"Custodian {random.Next(1, maxCustodians + 1)}";
        var dateCreated = now.AddDays(-random.Next(1, 730)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        var fileSize = ctx.FileSizeOverride ?? fileData.DataLength.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var fileType = ctx.IsChild ? Path.GetExtension(fileData.Attachment!.Value.filename).TrimStart('.').ToUpperInvariant() : wi.EffectiveFileType(this.request).ToUpperInvariant();

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

        if (this.request.Production.RedactedProduction)
        {
            var redactedImagePath = (ctx.RedactedImageRelPathOverride ?? fileData.RedactedImageRelPath)?.Replace('/', '\\') ?? string.Empty;
            // Only populate REDACTED_TEXT_PATH when text output is enabled; otherwise the file
            // on disk is not written and post-generation validation would flag the dangling reference.
            var redactedTextPath = (this.request.Output.WithText ? (ctx.RedactedTextRelPathOverride ?? fileData.RedactedTextRelPath) : null)?.Replace('/', '\\') ?? string.Empty;
            var nativeWithheld = ctx.NativeWithheldOverride ?? (fileData.NativePathOverride is not null ? "YES" : "NO");
            var redactionReason = ctx.RedactionReasonOverride ?? fileData.RedactionReason ?? string.Empty;
            v.Add(redactedImagePath);
            v.Add(redactedTextPath);
            v.Add(nativeWithheld);
            v.Add(redactionReason);
        }

        if (this.request.Metadata.ShouldIncludeEmlColumns(this.request.Output))
        {
            // In a File Type mix, Email Metadata appears only on Email records.
            var emlValues = !ctx.IsChild && string.Equals(wi.EffectiveFileType(this.request), "eml", StringComparison.Ordinal);
            var attachmentVal = emlValues && fileData.Attachment.HasValue ? fileData.Attachment.Value.filename : string.Empty;
            var subjectVal = !emlValues ? string.Empty : (fileData.Email?.Subject ?? $"Email Subject {wi.Index}");
            var fromVal = !emlValues ? string.Empty : (fileData.Email?.From ?? $"sender{wi.Index}@example.com");
            var toVal = !emlValues ? string.Empty : (fileData.Email?.To ?? $"recipient{wi.Index}@example.com");
            var ccVal = string.Empty;
            if (emlValues)
            {
                ccVal = fileData.Email is not null ? (fileData.Email.Cc ?? string.Empty) : $"cc{wi.Index}@example.com";
            }
            var sentDateVal = !emlValues ? string.Empty : (fileData.Email?.SentDate.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? now.AddDays(-random.Next(1, 30)).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            v.Add(attachmentVal);
            v.Add(subjectVal);
            v.Add(fromVal);
            v.Add(toVal);
            v.Add(ccVal);
            v.Add(sentDateVal);
        }

        if (this.request.Metadata.WithFamilies)
        {
            v.Add(ctx.BegAttach);
            v.Add(ctx.EndAttach);
            v.Add(ctx.ParentDocId);
        }

        return v;
    }
}
