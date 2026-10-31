namespace Zipper.Validation;

internal sealed record DatRecordInfo(
    string DocId,
    string? BatesNumber,
    string? Volume,
    string? ImagePath,
    string? PageCountRaw,
    long LineNumber);

internal sealed class OptDocumentInfo
{
    public required string DocKey { get; init; }
    public required long BreakLineNumber { get; init; }
    public required string? DeclaredPageCountRaw { get; init; }
    public required string FirstImagePath { get; init; }
    public int PageCount { get; set; } = 1;
}

internal static class ProductionSetDatOptReconciliationValidator
{
    private const string FirstPageSuffix = "_001";
    private const string PageNumberFormat = "D3";

    internal static void ValidateOptImagePathForm(
        string optRelPath,
        string imagePath,
        string? volume,
        long lineNumber,
        ICollection<ValidationReportFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            findings.Add(ValidationReportFinding.Error(
                "PathReconciliation",
                optRelPath,
                $"OPT image path is empty on line {lineNumber}.",
                lineNumber));
        }
        else if (IsInvalidProductionImagePath(imagePath, volume, requireVolume: false, out var reason))
        {
            findings.Add(ValidationReportFinding.Error(
                "PathReconciliation",
                optRelPath,
                $"OPT image path '{imagePath}' on line {lineNumber} {reason}.",
                lineNumber));
        }
    }

    internal static void ValidateContinuationPage(
        string optRelPath,
        OptDocumentInfo document,
        string pageKey,
        string imagePath,
        long lineNumber,
        ICollection<ValidationReportFinding> findings)
    {
        var baseKey = document.DocKey.EndsWith(FirstPageSuffix, StringComparison.OrdinalIgnoreCase)
            ? document.DocKey[..^FirstPageSuffix.Length]
            : document.DocKey;
        var pageNumber = document.PageCount.ToString(PageNumberFormat, System.Globalization.CultureInfo.InvariantCulture);
        var expectedPageKey = $"{baseKey}_{pageNumber}";

        if (!string.Equals(pageKey, expectedPageKey, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(ValidationReportFinding.Error(
                "OptBoundary",
                optRelPath,
                $"OPT continuation page '{pageKey}' on line {lineNumber} does not match document '{document.DocKey}'; expected page key '{expectedPageKey}'.",
                lineNumber));
        }

        if (string.IsNullOrWhiteSpace(document.FirstImagePath) || string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        var firstImagePath = document.FirstImagePath.Replace('\\', '/');
        var imageDirectory = Path.GetDirectoryName(firstImagePath)?.Replace('\\', '/') ?? string.Empty;
        var imageExtension = Path.GetExtension(firstImagePath);
        var firstImageName = Path.GetFileNameWithoutExtension(firstImagePath);
        var baseImageName = firstImageName.EndsWith(FirstPageSuffix, StringComparison.OrdinalIgnoreCase)
            ? firstImageName[..^FirstPageSuffix.Length]
            : firstImageName;
        var expectedImageName = $"{baseImageName}_{pageNumber}{imageExtension}";
        var expectedImagePath = string.IsNullOrEmpty(imageDirectory)
            ? expectedImageName
            : $"{imageDirectory}/{expectedImageName}";

        if (!string.Equals(imagePath.Replace('\\', '/'), expectedImagePath, StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(ValidationReportFinding.Error(
                "PathReconciliation",
                optRelPath,
                $"OPT continuation image path '{imagePath}' on line {lineNumber} does not match expected page path '{expectedImagePath}' for document '{document.DocKey}'.",
                lineNumber));
        }
    }

    internal static void ValidateOptPageCounts(
        string optRelPath,
        IReadOnlyList<OptDocumentInfo> optDocuments,
        ICollection<ValidationReportFinding> findings)
    {
        foreach (var optDoc in optDocuments)
        {
            int actualOptPages = optDoc.PageCount;
            if (!TryParsePositivePageCount(optDoc.DeclaredPageCountRaw, out int declaredOptPages))
            {
                var message = string.IsNullOrWhiteSpace(optDoc.DeclaredPageCountRaw)
                    ? $"OPT document '{optDoc.DocKey}' on line {optDoc.BreakLineNumber} has no page count."
                    : $"OPT document '{optDoc.DocKey}' on line {optDoc.BreakLineNumber} has invalid page count '{optDoc.DeclaredPageCountRaw}'.";
                findings.Add(ValidationReportFinding.Error(
                    "OptBoundary",
                    optRelPath,
                    message,
                    optDoc.BreakLineNumber));
            }
            else if (declaredOptPages != actualOptPages)
            {
                findings.Add(ValidationReportFinding.Error(
                    "OptBoundary",
                    optRelPath,
                    $"OPT document '{optDoc.DocKey}' page count mismatch on line {optDoc.BreakLineNumber}: declared {declaredOptPages}, but contains {actualOptPages} page row(s).",
                    optDoc.BreakLineNumber));
            }
        }
    }

    internal static void Reconcile(
        string datPath,
        string optPath,
        string datRelPath,
        string optRelPath,
        IReadOnlyList<DatRecordInfo> datRecords,
        IReadOnlyList<OptDocumentInfo> optDocuments,
        ICollection<ValidationReportFinding> findings)
    {
        if (!File.Exists(datPath) || !File.Exists(optPath))
        {
            return;
        }

        ValidatePathForms(datRecords, datRelPath, findings);

        var optDocMap = new Dictionary<string, OptDocumentInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (var optDoc in optDocuments)
        {
            if (!optDocMap.TryAdd(optDoc.DocKey, optDoc))
            {
                findings.Add(ValidationReportFinding.Error(
                    "UniqueId",
                    optRelPath,
                    $"Duplicate document identifier in OPT: '{optDoc.DocKey}'",
                    optDoc.BreakLineNumber));
            }
        }

        var datDocMap = new Dictionary<string, DatRecordInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var datDoc in datRecords)
        {
            if (!string.IsNullOrEmpty(datDoc.BatesNumber))
            {
                datDocMap.TryAdd(datDoc.BatesNumber, datDoc);
            }
        }
        foreach (var datDoc in datRecords)
        {
            if (string.IsNullOrEmpty(datDoc.BatesNumber) && !string.IsNullOrEmpty(datDoc.DocId))
            {
                datDocMap.TryAdd(datDoc.DocId, datDoc);
            }
        }

        foreach (var datDoc in datRecords)
        {
            if (FindMatchingOptDocument(optDocMap, datDoc) is null)
            {
                findings.Add(ValidationReportFinding.Error(
                    "OptBoundary",
                    datRelPath,
                    $"DAT document '{datDoc.DocId}' does not exist in OPT load file.",
                    datDoc.LineNumber));
            }
        }

        var matchedDatDocuments = new Dictionary<long, string>();
        foreach (var optDoc in optDocuments)
        {
            var datDoc = FindMatchingDatRecord(datDocMap, optDoc.DocKey);
            if (datDoc is null)
            {
                findings.Add(ValidationReportFinding.Error(
                    "OptBoundary",
                    optRelPath,
                    $"OPT document '{optDoc.DocKey}' on line {optDoc.BreakLineNumber} does not exist in DAT load file.",
                    optDoc.BreakLineNumber));
                continue;
            }

            if (matchedDatDocuments.TryGetValue(datDoc.LineNumber, out var matchedOptDocKey))
            {
                findings.Add(ValidationReportFinding.Error(
                    "OptBoundary",
                    optRelPath,
                    $"OPT document '{optDoc.DocKey}' on line {optDoc.BreakLineNumber} maps to DAT document '{datDoc.DocId}', which is already matched by OPT document '{matchedOptDocKey}'.",
                    optDoc.BreakLineNumber));
            }
            else
            {
                matchedDatDocuments.Add(datDoc.LineNumber, optDoc.DocKey);
            }
        }

        int alignCount = Math.Min(datRecords.Count, optDocuments.Count);
        for (int i = 0; i < alignCount; i++)
        {
            var datDoc = datRecords[i];
            var optDoc = optDocuments[i];
            bool matches = AreDocumentKeysMatching(datDoc.DocId, datDoc.BatesNumber, optDoc.DocKey);

            if (!matches)
            {
                findings.Add(ValidationReportFinding.Error(
                    "BatesConsistency",
                    optRelPath,
                    $"Document sort order mismatch between DAT and OPT at document index {i + 1}: DAT has '{datDoc.DocId}', but OPT has '{optDoc.DocKey}'.",
                    optDoc.BreakLineNumber));
                break;
            }
        }

        foreach (var datDoc in datRecords)
        {
            var optDoc = FindMatchingOptDocument(optDocMap, datDoc);
            if (optDoc is null)
            {
                continue;
            }

            int actualOptPages = optDoc.PageCount;
            if (!string.IsNullOrWhiteSpace(datDoc.PageCountRaw))
            {
                if (!TryParsePositivePageCount(datDoc.PageCountRaw, out int datPageCount))
                {
                    findings.Add(ValidationReportFinding.Error(
                        "InvalidValue",
                        datRelPath,
                        $"Invalid page count value '{datDoc.PageCountRaw}' for document '{datDoc.DocId}' on line {datDoc.LineNumber}.",
                        datDoc.LineNumber));
                }
                else if (datPageCount != actualOptPages)
                {
                    findings.Add(ValidationReportFinding.Error(
                        "OptBoundary",
                        datRelPath,
                        $"Page count mismatch for document '{datDoc.DocId}': DAT specifies {datPageCount}, but OPT contains {actualOptPages} page(s).",
                        datDoc.LineNumber));
                }
            }

            if (!string.IsNullOrWhiteSpace(datDoc.ImagePath)
                && optDoc.PageCount > 0
                && !string.IsNullOrWhiteSpace(optDoc.FirstImagePath)
                && !AreImagePathsReconciled(datDoc.ImagePath, optDoc.FirstImagePath))
            {
                findings.Add(ValidationReportFinding.Error(
                    "PathReconciliation",
                    datRelPath,
                    $"Image path mismatch for document '{datDoc.DocId}': DAT references '{datDoc.ImagePath}', but OPT page 1 references '{optDoc.FirstImagePath}'.",
                    datDoc.LineNumber));
            }
        }
    }

    private static bool TryParsePositivePageCount(string? value, out int pageCount)
        => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out pageCount)
            && pageCount > 0;

    private static void ValidatePathForms(
        IReadOnlyList<DatRecordInfo> datRecords,
        string datRelPath,
        ICollection<ValidationReportFinding> findings)
    {
        foreach (var datDoc in datRecords)
        {
            if (string.IsNullOrWhiteSpace(datDoc.ImagePath))
            {
                findings.Add(ValidationReportFinding.Error(
                    "PathReconciliation",
                    datRelPath,
                    $"DAT image path is empty for document '{datDoc.DocId}' on line {datDoc.LineNumber}.",
                    datDoc.LineNumber));
                continue;
            }

            if (IsInvalidProductionImagePath(datDoc.ImagePath, datDoc.Volume, requireVolume: datDoc.Volume is not null, out var reason))
            {
                findings.Add(ValidationReportFinding.Error(
                    "PathReconciliation",
                    datRelPath,
                    $"DAT image path '{datDoc.ImagePath}' on line {datDoc.LineNumber} {reason}.",
                    datDoc.LineNumber));
            }
        }
    }

    private static bool IsInvalidProductionImagePath(string path, string? volume, bool requireVolume, out string reason)
    {
        if (Path.IsPathRooted(path)
            || path.StartsWith('/')
            || path.StartsWith('\\')
            || (path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')))
        {
            reason = "is not a valid relative path";
            return true;
        }

        var normalized = path.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p == ".."))
        {
            reason = "contains directory traversal segments";
            return true;
        }

        if (parts.Length < 3 || !string.Equals(parts[0], "IMAGES", StringComparison.OrdinalIgnoreCase))
        {
            reason = "is not volume-relative under the IMAGES directory";
            return true;
        }

        if (requireVolume && string.IsNullOrWhiteSpace(volume))
        {
            reason = "has an empty Volume value";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(volume) && !string.Equals(parts[1], volume, StringComparison.OrdinalIgnoreCase))
        {
            reason = $"does not match Volume '{volume}' (the path is under '{parts[1]}')";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool AreImagePathsReconciled(string datImagePath, string optImagePath)
    {
        var normDat = datImagePath.Replace('\\', '/');
        var normOpt = optImagePath.Replace('\\', '/');

        if (string.Equals(normDat, normOpt, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var dirDat = Path.GetDirectoryName(normDat)?.Replace('\\', '/') ?? string.Empty;
        var dirOpt = Path.GetDirectoryName(normOpt)?.Replace('\\', '/') ?? string.Empty;
        if (!string.Equals(dirDat, dirOpt, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileDat = Path.GetFileNameWithoutExtension(normDat);
        var fileOpt = Path.GetFileNameWithoutExtension(normOpt);
        var extDat = Path.GetExtension(normDat);
        var extOpt = Path.GetExtension(normOpt);

        if (!string.Equals(extDat, extOpt, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return fileOpt.Equals($"{fileDat}{FirstPageSuffix}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool AreDocumentKeysMatching(string? datDocId, string? datBates, string optDocKey)
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

    private static bool DocumentKeyMatches(string datKey, string optKey)
        => string.Equals(datKey, optKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals($"{datKey}{FirstPageSuffix}", optKey, StringComparison.OrdinalIgnoreCase);

    private static OptDocumentInfo? FindMatchingOptDocument(
        IReadOnlyDictionary<string, OptDocumentInfo> optDocuments,
        DatRecordInfo datDocument)
    {
        if (!string.IsNullOrEmpty(datDocument.BatesNumber))
        {
            if (optDocuments.TryGetValue(datDocument.BatesNumber, out var optDocument))
            {
                return optDocument;
            }

            return optDocuments.TryGetValue($"{datDocument.BatesNumber}{FirstPageSuffix}", out optDocument)
                ? optDocument
                : null;
        }

        if (!string.IsNullOrEmpty(datDocument.DocId))
        {
            if (optDocuments.TryGetValue(datDocument.DocId, out var optDocument))
            {
                return optDocument;
            }

            if (optDocuments.TryGetValue($"{datDocument.DocId}{FirstPageSuffix}", out optDocument))
            {
                return optDocument;
            }
        }

        return null;
    }

    private static DatRecordInfo? FindMatchingDatRecord(
        IReadOnlyDictionary<string, DatRecordInfo> datDocuments,
        string optDocKey)
    {
        if (datDocuments.TryGetValue(optDocKey, out var datDocument))
        {
            return datDocument;
        }

        return optDocKey.EndsWith(FirstPageSuffix, StringComparison.OrdinalIgnoreCase)
            && optDocKey.Length > FirstPageSuffix.Length
            && datDocuments.TryGetValue(optDocKey[..^FirstPageSuffix.Length], out datDocument)
                ? datDocument
                : null;
    }
}
