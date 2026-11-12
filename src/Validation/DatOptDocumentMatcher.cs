namespace Zipper.Validation;

/// <summary>Resolved DAT header column positions (-1 when the column is absent).</summary>
internal sealed record DatHeaderSchema(
    int DocIdIndex,
    int BatesColumnIndex,
    int BatesIdentityIndex,
    int VolumeIndex,
    int NativePathIndex,
    int TextPathIndex,
    int ImagePathIndex,
    int PageCountIndex,
    int BegAttachIndex,
    int EndAttachIndex,
    int ParentDocIdIndex,
    int RedactedImagePathIndex,
    int RedactedTextPathIndex,
    int NativeWithheldIndex);

/// <summary>
/// Shared DAT / OPT document-table matching. Centralizes the header-role resolution,
/// DocBreak detection, and Bates Number / Control Number key matching with Page Suffix
/// normalization that the Chaos cross-format anomaly applier and the Production Set
/// post-generation validators previously each carried their own copy of.
/// </summary>
internal static class DatOptDocumentMatcher
{
    /// <summary>Page Suffix of a document's first page (REQ-239).</summary>
    public const string FirstPageSuffix = "_001";

    private static readonly string[] PageCountHeaderNames =
    {
        "PAGECOUNT",
        "PAGE_COUNT",
        "PAGE COUNT",
        "PAGECNT",
        "PAGES",
        "PAGE_CNT",
    };

    /// <summary>Index of the first header matching any of <paramref name="columnNames"/>, or -1.</summary>
    public static int FindColumnIndex(IReadOnlyList<string> headers, params string[] columnNames)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            foreach (var name in columnNames)
            {
                if (string.Equals(headers[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>Resolves every known DAT header role in one pass.</summary>
    public static DatHeaderSchema ResolveDatHeaderSchema(IReadOnlyList<string> headers)
    {
        // BatesColumnIndex names the explicit Bates column alone (it also names the desync-order
        // audit column); BatesIdentityIndex falls back to BEGBATES, which the standard profile
        // writes, when no explicit Bates column exists.
        int batesColumnIndex = FindColumnIndex(headers, "BATES_NUMBER", "BATES", "Bates Number");
        return new DatHeaderSchema(
            DocIdIndex: FindColumnIndex(headers, "DOCID", "Control Number"),
            BatesColumnIndex: batesColumnIndex,
            BatesIdentityIndex: batesColumnIndex >= 0 ? batesColumnIndex : FindColumnIndex(headers, "BEGBATES"),
            VolumeIndex: FindColumnIndex(headers, "VOLUME", "Volume"),
            NativePathIndex: FindColumnIndex(headers, "NATIVE_PATH", "PATH", "FILE_PATH", "Native Path", "File Path"),
            TextPathIndex: FindColumnIndex(headers, "TEXT_PATH", "TEXT", "Text Path", "EXTRACTED_TEXT"),
            ImagePathIndex: FindColumnIndex(headers, "IMAGE_PATH", "IMAGE", "Image Path"),
            PageCountIndex: FindColumnIndex(headers, PageCountHeaderNames),
            BegAttachIndex: FindColumnIndex(headers, "BEGATTACH", "BEG_ATTACH", "BEG ATTACH"),
            EndAttachIndex: FindColumnIndex(headers, "ENDATTACH", "END_ATTACH", "END ATTACH"),
            ParentDocIdIndex: FindColumnIndex(headers, "PARENTDOCID", "Parent Doc ID"),
            RedactedImagePathIndex: FindColumnIndex(headers, "REDACTED_IMAGE_PATH"),
            RedactedTextPathIndex: FindColumnIndex(headers, "REDACTED_TEXT_PATH"),
            NativeWithheldIndex: FindColumnIndex(headers, "NATIVE_WITHHELD"));
    }

    /// <summary>Whether an OPT DocBreak field marks the first page row of a document.</summary>
    public static bool IsOptDocumentBreak(string docBreakField)
    {
        var trimmed = docBreakField.Trim();
        return string.Equals(trimmed, "Y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "1", StringComparison.Ordinal);
    }

    /// <summary>
    /// Indexes DAT records for OPT-key lookup. Bates keys are inserted first so a Bates match
    /// takes precedence over a DocId that repeats it; DocId keys fill what remains.
    /// </summary>
    public static Dictionary<string, TRecord> BuildDatIndex<TRecord>(
        IEnumerable<TRecord> records,
        Func<TRecord, string?> getDocId,
        Func<TRecord, string?> getBates,
        bool includeBates)
    {
        var index = new Dictionary<string, TRecord>(StringComparer.OrdinalIgnoreCase);
        if (includeBates)
        {
            foreach (var record in records)
            {
                var bates = getBates(record);
                if (!string.IsNullOrEmpty(bates))
                {
                    index.TryAdd(bates, record);
                }
            }
        }

        foreach (var record in records)
        {
            var docId = getDocId(record);
            if (!string.IsNullOrEmpty(docId))
            {
                index.TryAdd(docId, record);
            }
        }

        return index;
    }

    /// <summary>Indexes OPT documents by their break-row key.</summary>
    public static Dictionary<string, TOpt> BuildOptIndex<TOpt>(IEnumerable<TOpt> documents, Func<TOpt, string> getDocKey)
    {
        var index = new Dictionary<string, TOpt>(StringComparer.OrdinalIgnoreCase);
        foreach (var document in documents)
        {
            index.TryAdd(getDocKey(document), document);
        }

        return index;
    }

    /// <summary>Finds the OPT document for a DAT key, trying the key itself then its first-page form.</summary>
    public static TOpt? FindMatchingOptByKey<TOpt>(IReadOnlyDictionary<string, TOpt> optByKey, string? datKey)
    {
        if (string.IsNullOrEmpty(datKey))
        {
            return default;
        }

        if (optByKey.TryGetValue(datKey, out var document))
        {
            return document;
        }

        return optByKey.TryGetValue($"{datKey}{FirstPageSuffix}", out document) ? document : default;
    }

    /// <summary>Finds the DAT record for an OPT document key, stripping a trailing Page Suffix.</summary>
    public static TRecord? FindMatchingDatByOptKey<TRecord>(IReadOnlyDictionary<string, TRecord> datByKey, string? optDocKey)
    {
        if (string.IsNullOrEmpty(optDocKey))
        {
            return default;
        }

        if (datByKey.TryGetValue(optDocKey, out var record))
        {
            return record;
        }

        return optDocKey.EndsWith(FirstPageSuffix, StringComparison.OrdinalIgnoreCase)
            && optDocKey.Length > FirstPageSuffix.Length
            && datByKey.TryGetValue(optDocKey[..^FirstPageSuffix.Length], out record)
                ? record
                : default;
    }

    /// <summary>Whether a DAT key names an OPT document key, allowing for the first-page suffix.</summary>
    public static bool DocumentKeyMatches(string datKey, string optKey)
        => string.Equals(datKey, optKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals($"{datKey}{FirstPageSuffix}", optKey, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a DAT record's Bates (preferred) or DocId names the OPT document key.</summary>
    public static bool AreDocumentKeysMatching(string? datDocId, string? datBates, string optDocKey)
    {
        if (string.IsNullOrEmpty(optDocKey))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(datBates))
        {
            return DocumentKeyMatches(datBates, optDocKey);
        }

        return !string.IsNullOrEmpty(datDocId) && DocumentKeyMatches(datDocId, optDocKey);
    }
}
