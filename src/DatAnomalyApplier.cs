using System.Text;
using Zipper.LoadFiles;
using Zipper.SourceInput;
using Zipper.Validation;

namespace Zipper;

internal class DatAnomalyApplier : IAnomalyApplier
{
    private const int EmptyTrailingColumnCount = 4096;
    private const int MaxFieldLength = 32_768;
    private const int MaxPathCollisionAttempts = 64;
    private const string FieldLengthCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private static readonly string[] MalformedDateValues =
    [
        "00/00/0000",
        "2018-01-15 a.m.",
        "2018-044-09",
    ];
    private static readonly HashSet<string> PathColumnNames =
        new(["FILEPATH", "PATH", "NATIVEPATH", "TEXTPATH", "IMAGEPATH", "ITEMPATH"], StringComparer.Ordinal);
    private static readonly HashSet<string> IdentityColumnNames =
        new(["DOCID", "CONTROLNUMBER", "BATES", "BATESNUMBER", "BEGBATES", "ENDBATES", "IMAGEKEY"], StringComparer.Ordinal);

    private static readonly char[] AlternativeDelimiters = { ',', '\t', '|' };

    private readonly Random random;
    private readonly string columnDelimiter;
    private readonly string quoteDelimiter;
    private readonly string eol;
    private readonly HashSet<string> dateColumnNames;
    private readonly IReadOnlySet<string>? sourceFilePaths;
    private readonly IReadOnlySet<int>? dateColumnIndexes;
    private readonly HashSet<long> encodingAnomalyLines = new();
    private readonly char[] alternatives;
    private IReadOnlyList<string>? cachedColumns;
    private int cachedValueCount = -1;
    private int filePathColumnIndex = -1;
    private int pathColumnIndex = -1;
    private int dateColumnIndex = -1;
    private int fieldValueColumnIndex = -1;
    private string? oversizedFieldLengthValue;
    private string? alternateOversizedFieldLengthValue;

    public DatAnomalyApplier(
        Random random,
        string columnDelimiter,
        string quoteDelimiter,
        string eol,
        IReadOnlyCollection<string>? dateColumnNames = null,
        IReadOnlySet<string>? sourceFilePaths = null,
        IReadOnlySet<int>? dateColumnIndexes = null)
    {
        this.random = random;
        this.columnDelimiter = columnDelimiter;
        this.quoteDelimiter = quoteDelimiter;
        this.eol = eol;
        this.dateColumnNames = new HashSet<string>(
            (dateColumnNames ?? Array.Empty<string>()).Select(NormalizeColumnName),
            StringComparer.Ordinal);
        this.sourceFilePaths = sourceFilePaths;
        this.dateColumnIndexes = dateColumnIndexes;
        this.alternatives = AlternativeDelimiters.Where(c => !string.Equals(c.ToString(), columnDelimiter, StringComparison.Ordinal)).ToArray();
    }

    public string Apply(
        long lineNumber,
        string line,
        string recordId,
        string chaosType,
        List<ChaosAnomaly> anomalies,
        LoadFileRecord? record = null,
        ILoadFileSerializer? serializer = null)
    {
        string result = line;
        string column = "N/A";
        string description;

        switch (chaosType)
        {
            case "mixed-delimiters":
                result = ApplyMixedDelimiters(line, out int delimIndex);
                column = $"Delimiter {delimIndex}";
                description = $"Replaced delimiter {delimIndex} with an alternative delimiter character.";
                break;
            case "quotes":
                result = ApplyDroppedQuote(line, out string affectedColumn);
                column = affectedColumn;
                description = $"Omitted the closing {FormatDelimiterDisplay(quoteDelimiter)} character on column {affectedColumn}.";
                break;
            case "columns":
                bool added = random.Next(2) == 0;
                result = ApplyColumnShift(line, added);
                description = added
                    ? "Added an extra column delimiter to break expected column count."
                    : "Removed a column delimiter to break expected column count.";
                break;
            case "eol":
                result = ApplyRawNewline(line, out string eolColumn);
                column = eolColumn;
                description = $"Injected raw unescaped newline into field {eolColumn}.";
                break;
            case "encoding":
                encodingAnomalyLines.Add(lineNumber);
                return line;
            case "path-pointer":
                result = this.ApplyPathPointer(line, record, serializer, out column, out description);
                break;
            case "field-length":
                result = this.ApplyFieldLength(line, record, serializer, out column, out description);
                break;
            case "date-format":
                result = this.ApplyDateFormat(line, record, serializer, out column, out description);
                break;
            case "null-byte":
                result = this.ApplyNullByte(line, record, serializer, out column, out description);
                break;
            case "trailing-empties":
                result = this.ApplyTrailingEmpties(line, record, out column, out description);
                break;
            default:
                description = $"Unknown chaos type: {chaosType}";
                break;
        }

        // Only record anomaly if the line was actually modified
        if (result != line)
        {
            anomalies.Add(new ChaosAnomaly
            {
                LineNumber = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                RecordID = recordId,
                Column = column,
                ErrorType = chaosType,
                Description = description,
            });
        }

        return result;
    }

    private string ApplyPathPointer(
        string line,
        LoadFileRecord? record,
        ILoadFileSerializer? serializer,
        out string column,
        out string description)
    {
        column = "N/A";
        description = string.Empty;
        int pathIndex = this.FindPathColumnIndex(record);
        if (pathIndex < 0 || record is null || serializer is null)
        {
            return line;
        }

        string originalPath = GetValue(record, pathIndex);
        string baseMissingPath = CreateMissingPath(originalPath);
        string missingPath = baseMissingPath;
        string normalizedOriginalPath = originalPath.Replace('\\', '/');
        if (this.sourceFilePaths is not null)
        {
            GetPathReconciliationParts(baseMissingPath, out string missingDirectory, out string baseMissingFileName);
            string missingFileName = baseMissingFileName;
            int collisionIndex = 0;
            while (this.IsExistingSourcePath(missingDirectory, missingFileName))
            {
                if (collisionIndex == MaxPathCollisionAttempts)
                {
                    missingPath = string.Empty;
                    break;
                }

                collisionIndex++;
                missingPath = AddCollisionSuffix(baseMissingPath, collisionIndex);
                missingFileName = string.Concat(
                    baseMissingFileName,
                    "_",
                    collisionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        var pathFinding = PathReconciliationValidator.FindSinglePathMismatch(missingPath, normalizedOriginalPath);
        if (pathFinding is null)
        {
            missingPath = string.Empty;
            pathFinding = PathReconciliationValidator.FindSinglePathMismatch(missingPath, normalizedOriginalPath);
        }

        if (pathFinding is null || !TryRenderReplacement(record, serializer, pathIndex, missingPath, out string result))
        {
            return line;
        }

        column = record.Columns[pathIndex];
        description = $"{pathFinding.Category}: {pathFinding.Message}";
        return result;
    }

    private string ApplyFieldLength(
        string line,
        LoadFileRecord? record,
        ILoadFileSerializer? serializer,
        out string column,
        out string description)
    {
        column = "N/A";
        description = string.Empty;
        int lengthIndex = this.GetRecordColumnIndex(record, ref this.fieldValueColumnIndex);
        if (lengthIndex < 0 || record is null || serializer is null)
        {
            return line;
        }

        char valueCharacter = FieldLengthCharacters.First(c =>
            !this.columnDelimiter.Contains(c, StringComparison.Ordinal)
            && !this.quoteDelimiter.Contains(c, StringComparison.Ordinal));
        string oversizedValue = this.oversizedFieldLengthValue ??= new(valueCharacter, MaxFieldLength);
        if (string.Equals(GetValue(record, lengthIndex), oversizedValue, StringComparison.Ordinal))
        {
            valueCharacter = FieldLengthCharacters.First(c =>
                c != valueCharacter
                && !this.columnDelimiter.Contains(c, StringComparison.Ordinal)
                && !this.quoteDelimiter.Contains(c, StringComparison.Ordinal));
            oversizedValue = this.alternateOversizedFieldLengthValue ??= new(valueCharacter, MaxFieldLength);
        }
        if (!TryRenderReplacement(record, serializer, lengthIndex, oversizedValue, out string result))
        {
            return line;
        }

        column = record.Columns[lengthIndex];
        description = $"Replaced the value with {MaxFieldLength.ToString(System.Globalization.CultureInfo.InvariantCulture)} characters, beyond common platform field limits.";
        return result;
    }

    private string ApplyDateFormat(
        string line,
        LoadFileRecord? record,
        ILoadFileSerializer? serializer,
        out string column,
        out string description)
    {
        column = "N/A";
        description = string.Empty;
        int dateIndex = this.GetRecordColumnIndex(record, ref this.dateColumnIndex);
        string originalDate = GetValue(record, dateIndex);
        string? malformedDate = null;
        int startIndex = this.random.Next(MalformedDateValues.Length);
        for (int i = 0; i < MalformedDateValues.Length; i++)
        {
            string candidate = MalformedDateValues[(startIndex + i) % MalformedDateValues.Length];
            if (!string.Equals(candidate, originalDate, StringComparison.Ordinal))
            {
                malformedDate = candidate;
                break;
            }
        }
        if (malformedDate is null
            || !TryRenderReplacement(record, serializer, dateIndex, malformedDate, out string result)
            || record is null)
        {
            return line;
        }

        column = record.Columns[dateIndex];
        description = $"Replaced the date value with malformed value '{malformedDate}'.";
        return result;
    }

    private string ApplyNullByte(
        string line,
        LoadFileRecord? record,
        ILoadFileSerializer? serializer,
        out string column,
        out string description)
    {
        column = "N/A";
        description = string.Empty;
        int controlIndex = this.GetRecordColumnIndex(record, ref this.fieldValueColumnIndex);
        if (controlIndex < 0 || record is null || serializer is null)
        {
            return line;
        }

        string value = GetValue(record, controlIndex);
        string valueWithControls = value.Insert(value.Length / 2, "\0\u0001");
        if (!TryRenderReplacement(record, serializer, controlIndex, valueWithControls, out string result))
        {
            return line;
        }

        column = record.Columns[controlIndex];
        description = "Inserted a NULL byte and unsupported control character in the field.";
        return result;
    }

    private string ApplyTrailingEmpties(
        string line,
        LoadFileRecord? record,
        out string column,
        out string description)
    {
        column = "N/A";
        description = string.Empty;
        if (record is null || string.IsNullOrEmpty(this.columnDelimiter))
        {
            return line;
        }

        string result = string.Create(
            line.Length + (this.columnDelimiter.Length * EmptyTrailingColumnCount),
            (Line: line, Delimiter: this.columnDelimiter),
            static (destination, state) =>
            {
                state.Line.AsSpan().CopyTo(destination);
                int offset = state.Line.Length;
                for (int i = 0; i < EmptyTrailingColumnCount; i++)
                {
                    state.Delimiter.AsSpan().CopyTo(destination[offset..]);
                    offset += state.Delimiter.Length;
                }
            });
        column = record.Columns.Count > 0
            ? $"Trailing empty columns after {record.Columns[^1]}"
            : "Trailing empty columns";
        description = $"Appended {EmptyTrailingColumnCount.ToString(System.Globalization.CultureInfo.InvariantCulture)} empty trailing columns.";
        return result;
    }

    private void UpdateColumnIndexes(IReadOnlyList<string> columns, int valueCount)
    {
        if (ReferenceEquals(this.cachedColumns, columns) && this.cachedValueCount == valueCount)
        {
            return;
        }

        this.cachedColumns = columns;
        this.cachedValueCount = valueCount;
        this.filePathColumnIndex = -1;
        this.pathColumnIndex = -1;
        this.dateColumnIndex = -1;
        this.fieldValueColumnIndex = -1;
        for (int i = 0; i < valueCount; i++)
        {
            string normalized = NormalizeColumnName(columns[i]);
            bool isDateColumn = this.dateColumnIndexes?.Contains(i) ?? this.dateColumnNames.Contains(normalized);
            if (this.filePathColumnIndex < 0 && string.Equals(normalized, "FILEPATH", StringComparison.Ordinal))
            {
                this.filePathColumnIndex = i;
            }

            if (this.pathColumnIndex < 0 && IsPathColumnNormalized(normalized))
            {
                this.pathColumnIndex = i;
            }

            if (this.dateColumnIndex < 0 && isDateColumn)
            {
                this.dateColumnIndex = i;
            }

            if (this.fieldValueColumnIndex < 0 && !isDateColumn && this.IsFieldValueColumnNormalized(normalized))
            {
                this.fieldValueColumnIndex = i;
            }
        }
    }

    private int FindPathColumnIndex(LoadFileRecord? record)
    {
        if (record is null)
        {
            return -1;
        }

        int valueCount = Math.Min(record.Columns.Count, record.Values.Count);
        this.UpdateColumnIndexes(record.Columns, valueCount);
        return this.filePathColumnIndex >= 0 && this.filePathColumnIndex < valueCount
            ? this.filePathColumnIndex
            : this.pathColumnIndex >= 0 && this.pathColumnIndex < valueCount
                ? this.pathColumnIndex
                : -1;
    }

    private int GetRecordColumnIndex(LoadFileRecord? record, ref int columnIndex)
    {
        if (record is null)
        {
            return -1;
        }

        int valueCount = Math.Min(record.Columns.Count, record.Values.Count);
        this.UpdateColumnIndexes(record.Columns, valueCount);
        return columnIndex >= 0 && columnIndex < valueCount ? columnIndex : -1;
    }

    private bool IsExistingSourcePath(string directory, string fileNameWithoutExtension)
    {
        if (this.sourceFilePaths is null)
        {
            return false;
        }

        foreach (string extension in SourceFileTypeMap.SupportedExtensions)
        {
            string candidatePath = directory.Length == 0
                ? string.Concat(fileNameWithoutExtension, extension)
                : string.Concat(directory, "/", fileNameWithoutExtension, extension);
            if (this.sourceFilePaths.Contains(candidatePath))
            {
                return true;
            }
        }

        return false;
    }

    private static void GetPathReconciliationParts(string path, out string directory, out string fileNameWithoutExtension)
    {
        string normalizedPath = path.Replace('\\', '/');
        directory = Path.GetDirectoryName(normalizedPath)?.Replace('\\', '/') ?? string.Empty;
        fileNameWithoutExtension = Path.GetFileNameWithoutExtension(normalizedPath);
    }

    private static bool IsPathColumnNormalized(string normalized)
        => PathColumnNames.Contains(normalized) || normalized.EndsWith("PATH", StringComparison.Ordinal);

    private bool IsFieldValueColumnNormalized(string normalized)
        => !IsPathColumnNormalized(normalized)
            && !IdentityColumnNames.Contains(normalized)
            && normalized is not ("FILESIZE" or "PAGECOUNT");

    private static string GetValue(LoadFileRecord? record, int index) =>
        record is not null && index >= 0 && index < record.Values.Count ? record.Values[index] : string.Empty;

    private static bool TryRenderReplacement(
        LoadFileRecord? record,
        ILoadFileSerializer? serializer,
        int index,
        string replacement,
        out string line)
    {
        if (record is null || serializer is null || index < 0 || index >= record.Values.Count)
        {
            line = string.Empty;
            return false;
        }

        var values = record.Values.ToArray();
        values[index] = replacement;
        line = serializer.RenderRecord(new LoadFileRecord
        {
            Columns = record.Columns,
            Values = values,
            RecordId = record.RecordId,
        });
        return true;
    }

    private static string CreateMissingPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "NATIVES\\__MISSING__\\DOCUMENT.pdf";
        }

        int lastSeparator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        int extension = path.LastIndexOf('.');
        return extension > lastSeparator
            ? string.Concat(path.AsSpan(0, extension), "_MISSING", path.AsSpan(extension))
            : string.Concat(path, "_MISSING");
    }

    private static string AddCollisionSuffix(string path, int collisionIndex)
    {
        int lastSeparator = Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
        int extension = path.LastIndexOf('.');
        string suffix = $"_{collisionIndex.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return extension > lastSeparator
            ? string.Concat(path.AsSpan(0, extension), suffix, path.AsSpan(extension))
            : string.Concat(path, suffix);
    }

    private static string NormalizeColumnName(string column)
        => new(column.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public byte[]? GetEncodingAnomaly(long lineNumber, long nextLineNumber, Encoding encoding, List<ChaosAnomaly> anomalies)
    {
        if (!encodingAnomalyLines.Remove(lineNumber))
        {
            return null;
        }

        byte[] invalidBytes;
        string encodingName = encoding.WebName.ToUpperInvariant();

        if (encodingName.Contains("UTF-16", StringComparison.Ordinal))
        {
            invalidBytes = new byte[] { 0x00, 0xD8 };
        }
        else if (encodingName.Contains("UTF-8", StringComparison.Ordinal))
        {
            invalidBytes = new byte[] { 0xFE, 0xFF };
        }
        else
        {
            invalidBytes = new byte[] { 0x81, 0x8D, 0x8F };
        }

        anomalies.Add(new ChaosAnomaly
        {
            LineNumber = $"Boundary {lineNumber}-{nextLineNumber}",
            RecordID = "N/A",
            Column = "N/A",
            ErrorType = "encoding",
            Description = $"Injected invalid {encoding.EncodingName} byte sequence between lines.",
        });

        return invalidBytes;
    }

    private string ApplyMixedDelimiters(string line, out int delimiterIndex)
    {
        if (string.IsNullOrEmpty(columnDelimiter))
        {
            delimiterIndex = 0;
            return line;
        }

        var positions = new List<int>();
        int searchStart = 0;
        while (searchStart < line.Length)
        {
            int pos = line.IndexOf(columnDelimiter, searchStart, StringComparison.Ordinal);
            if (pos < 0)
            {
                break;
            }

            positions.Add(pos);
            searchStart = pos + columnDelimiter.Length;
        }

        if (positions.Count == 0)
        {
            delimiterIndex = 0;
            return line;
        }

        delimiterIndex = random.Next(positions.Count) + 1;
        int targetPos = positions[delimiterIndex - 1];

        if (this.alternatives.Length == 0)
        {
            return line;
        }

        char replacementChar = this.alternatives[random.Next(this.alternatives.Length)];

        return string.Concat(
            line.AsSpan(0, targetPos),
            replacementChar.ToString(),
            line.AsSpan(targetPos + columnDelimiter.Length));
    }

    private string ApplyDroppedQuote(string line, out string column)
    {
        if (string.IsNullOrEmpty(quoteDelimiter) || string.IsNullOrEmpty(columnDelimiter))
        {
            column = "Unknown";
            return line;
        }

        int lastQuotePos = line.LastIndexOf(quoteDelimiter, StringComparison.Ordinal);
        if (lastQuotePos < 0)
        {
            column = "Unknown";
            return line;
        }

        int delimCount = 0;
        int searchPos = 0;
        while (searchPos < lastQuotePos)
        {
            int pos = line.IndexOf(columnDelimiter, searchPos, StringComparison.Ordinal);
            if (pos < 0 || pos >= lastQuotePos)
            {
                break;
            }

            delimCount++;
            searchPos = pos + columnDelimiter.Length;
        }

        column = $"Column {delimCount + 1}";

        return string.Concat(
            line.AsSpan(0, lastQuotePos),
            line.AsSpan(lastQuotePos + quoteDelimiter.Length));
    }

    private string ApplyColumnShift(string line, bool add)
    {
        if (add)
        {
            int midpoint = line.Length / 2;
            return line.Insert(midpoint, columnDelimiter);
        }

        int delimPos = line.IndexOf(columnDelimiter, StringComparison.Ordinal);
        if (delimPos < 0)
        {
            return line;
        }

        return string.Concat(
            line.AsSpan(0, delimPos),
            line.AsSpan(delimPos + columnDelimiter.Length));
    }

    private string ApplyRawNewline(string line, out string column)
    {
        if (!string.IsNullOrEmpty(quoteDelimiter))
        {
            int firstQuote = line.IndexOf(quoteDelimiter, StringComparison.Ordinal);
            if (firstQuote >= 0)
            {
                int afterFirst = firstQuote + quoteDelimiter.Length;
                int secondQuote = line.IndexOf(quoteDelimiter, afterFirst, StringComparison.Ordinal);
                if (secondQuote > afterFirst)
                {
                    int insertPos = afterFirst + ((secondQuote - afterFirst) / 2);
                    column = "Field 1";
                    return line.Insert(insertPos, eol);
                }
            }
        }

        column = "Unknown";
        return line.Insert(line.Length / 2, eol);
    }

    private static string FormatDelimiterDisplay(string delimiter)
    {
        if (string.IsNullOrEmpty(delimiter))
        {
            return "none";
        }

        if (delimiter.Length == 1)
        {
            int code = delimiter[0];
            return $"ascii:{code}";
        }

        return $"string:'{delimiter}'";
    }
}
