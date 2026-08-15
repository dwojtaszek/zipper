namespace Zipper.SourceInput;

/// <summary>
/// The single home for Source Record invariants: path normalization and sanitization,
/// File Type validation, identity rules (character validity and uniqueness), Source
/// Metadata column mapping, and count rules. The Source CSV and Directory Template adapters
/// are thin I/O shells that parse/walk their input and feed raw rows into this shared
/// pipeline, so a new Source Record invariant needs exactly one edit.
/// </summary>
internal sealed class SourceRecordIntake
{
    /// <summary>Maximum Source Records a single input may define; Source Records are held in memory, so larger inputs must be split into multiple runs.</summary>
    internal const int MaxSourceRecords = 10_000_000;

    private readonly string _sourceLabel;
    private readonly int _maxRecords;
    private readonly List<SourceRecord> _records = new();
    private readonly HashSet<string> _seenPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenControlNumbers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenBatesNumbers = new(StringComparer.OrdinalIgnoreCase);

    internal SourceRecordIntake(string sourceLabel, int maxRecords = MaxSourceRecords)
    {
        this._sourceLabel = sourceLabel;
        this._maxRecords = maxRecords;
    }

    /// <summary>
    /// Adds one raw record to the pipeline. <paramref name="fileTypeText"/> is the
    /// user-declared File Type for Source CSV rows (validated, then checked against the
    /// path extension) or null for Directory Template entries (File Type inferred from the
    /// extension). <paramref name="rowContext"/> names the row or entry in errors.
    /// </summary>
    internal bool TryAdd(string rawPath, string? fileTypeText, string? controlNumber, string? batesNumber,
        IReadOnlyDictionary<string, string>? metadata, string rowContext, out string? error)
    {
        if (this._records.Count >= this._maxRecords)
        {
            error = $"{rowContext} exceeds the maximum of {this._maxRecords} Source Records; split the input into multiple runs.";
            return false;
        }

        if (!SourcePathSanitizer.TryNormalize(rawPath, out var relativePath, out var pathError))
        {
            error = $"{rowContext}: invalid FilePath: {pathError}";
            return false;
        }

        string fileType;
        if (fileTypeText is null)
        {
            var extension = Path.GetExtension(relativePath).ToLowerInvariant();
            if (!SourceFileTypeMap.TryFromExtension(extension, out fileType))
            {
                error = extension.Length == 0
                    ? $"Directory template file '{relativePath}' has no extension; the File Type cannot be inferred."
                    : $"Directory template file '{relativePath}' has unsupported extension '{extension}'. Supported: {SourceFileTypeMap.SupportedExtensionsDisplay}.";
                return false;
            }
        }
        else
        {
            fileType = fileTypeText.Trim().TrimStart('.').ToLowerInvariant();
            if (fileType.Length == 0)
            {
                error = $"{rowContext}: FileType is empty.";
                return false;
            }

            if (!FileGeneratorFactory.IsKnownType(fileType))
            {
                error = $"{rowContext}: unsupported File Type '{fileType}'. Supported types: pdf, jpg, tiff, eml, docx, xlsx.";
                return false;
            }

            var pathExtension = Path.GetExtension(relativePath).ToLowerInvariant();
            if (!SourceFileTypeMap.TryFromExtension(pathExtension, out var pathType)
                || !string.Equals(pathType, fileType, StringComparison.Ordinal))
            {
                error = $"{rowContext}: FilePath extension '{pathExtension}' does not match FileType '{fileType}'.";
                return false;
            }
        }

        if (!this._seenPaths.Add(relativePath))
        {
            error = fileTypeText is null
                ? $"Directory template contains duplicate relative path '{relativePath}'."
                : $"{rowContext}: Duplicate FilePath '{relativePath}'.";
            return false;
        }

        if (controlNumber is not null && !IsValidIdentityValue(controlNumber))
        {
            error = $"{rowContext}: ControlNumber contains invalid characters (control characters and path separators are not allowed).";
            return false;
        }

        if (batesNumber is not null && !IsValidIdentityValue(batesNumber))
        {
            error = $"{rowContext}: BatesNumber contains invalid characters (control characters and path separators are not allowed).";
            return false;
        }

        if (controlNumber is not null && !this._seenControlNumbers.Add(controlNumber))
        {
            error = $"{rowContext}: Duplicate ControlNumber '{controlNumber}' (identities must be unique across Source Records).";
            return false;
        }

        if (batesNumber is not null && !this._seenBatesNumbers.Add(batesNumber))
        {
            error = $"{rowContext}: Duplicate BatesNumber '{batesNumber}' (identities must be unique across Source Records).";
            return false;
        }

        this._records.Add(new SourceRecord
        {
            RelativePath = relativePath,
            FileType = fileType,
            ControlNumber = controlNumber,
            BatesNumber = batesNumber,
            Metadata = metadata,
        });
        error = null;
        return true;
    }

    /// <summary>
    /// Finalizes the intake: a source that yields no Source Records is an error. Returns the
    /// validated, normalized records in the order they were added.
    /// </summary>
    internal bool TryBuild(out IReadOnlyList<SourceRecord> records, out string? error)
    {
        if (this._records.Count == 0)
        {
            records = Array.Empty<SourceRecord>();
            error = $"{this._sourceLabel} contains no Source Records.";
            return false;
        }

        records = this._records;
        error = null;
        return true;
    }

    /// <summary>
    /// Maps a Source CSV header row to column roles. Required columns are FilePath and
    /// FileType; ControlNumber (alias DocId) and BatesNumber (aliases Bates, BegBates) are
    /// optional; every other column becomes Source Metadata. Header matching is
    /// case-insensitive and ignores spaces, underscores, and hyphens.
    /// </summary>
    internal static bool TryMapCsvColumns(IReadOnlyList<string> header, out CsvColumnLayout layout, out string error)
    {
        layout = new CsvColumnLayout(-1, -1, -1, -1, Array.Empty<(int Index, string Name)>());
        error = string.Empty;

        var pathIndex = FindColumn(header, out var pathAmbiguous, "filepath");
        var typeIndex = FindColumn(header, out var typeAmbiguous, "filetype");
        var controlIndex = FindColumn(header, out var controlAmbiguous, "controlnumber", "docid");
        var batesIndex = FindColumn(header, out var batesAmbiguous, "batesnumber", "bates", "begbates");

        if (pathIndex < 0)
        {
            error = "Source CSV header is missing the required 'FilePath' column.";
            return false;
        }

        if (typeIndex < 0)
        {
            error = "Source CSV header is missing the required 'FileType' column.";
            return false;
        }

        if (pathAmbiguous || typeAmbiguous || controlAmbiguous || batesAmbiguous)
        {
            error = "Source CSV header maps multiple columns to the same field (aliases such as DocId/ControlNumber or Bates/BegBates are mutually exclusive).";
            return false;
        }

        var known = new HashSet<int> { pathIndex, typeIndex };
        if (controlIndex >= 0)
        {
            known.Add(controlIndex);
        }

        if (batesIndex >= 0)
        {
            known.Add(batesIndex);
        }

        var metadataColumns = new List<(int Index, string Name)>();
        var seenHeaders = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < header.Count; i++)
        {
            var name = header[i].Trim();
            if (!seenHeaders.Add(NormalizeHeader(name)))
            {
                error = $"Source CSV header contains a duplicate column '{name}'.";
                return false;
            }

            if (!known.Contains(i))
            {
                metadataColumns.Add((i, name));
            }
        }

        layout = new CsvColumnLayout(pathIndex, typeIndex, controlIndex, batesIndex, metadataColumns);
        return true;
    }

    private static bool IsValidIdentityValue(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c) || c is '/' or '\\')
            {
                return false;
            }
        }

        return true;
    }

    private static int FindColumn(IReadOnlyList<string> header, out bool ambiguous, params string[] acceptedNames)
    {
        ambiguous = false;
        var found = -1;
        for (int i = 0; i < header.Count; i++)
        {
            var normalized = NormalizeHeader(header[i]);
            foreach (var accepted in acceptedNames)
            {
                if (string.Equals(normalized, accepted, StringComparison.Ordinal))
                {
                    if (found >= 0)
                    {
                        ambiguous = true;
                    }
                    else
                    {
                        found = i;
                    }
                }
            }
        }

        return found;
    }

    private static string NormalizeHeader(string name)
        => name.Trim().ToLowerInvariant().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal);
}

/// <summary>Column roles of a Source CSV header, produced by <see cref="SourceRecordIntake.TryMapCsvColumns"/>.</summary>
internal sealed record CsvColumnLayout(
    int PathIndex,
    int TypeIndex,
    int ControlIndex,
    int BatesIndex,
    IReadOnlyList<(int Index, string Name)> MetadataColumns);
