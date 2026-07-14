using System.Text;
using Zipper.Config;

namespace Zipper.Validation;

public sealed class ValidatorRunner
{
    public ValidationResult ValidateLoadFile(
        string loadFilePath,
        string format,
        IReadOnlyList<string>? headerColumns,
        string? expectedEol,
        IEnumerable<string>? expectedPaths = null,
        BatesNumberConfig? bates = null,
        Encoding? encoding = null,
        char columnDelimiter = '\x14',
        char quoteDelimiter = '\xfe')
    {
        ArgumentNullException.ThrowIfNull(loadFilePath);
        var result = ValidateLines(File.ReadLines(loadFilePath, encoding ?? Encoding.UTF8), loadFilePath, format, headerColumns, expectedPaths, bates, columnDelimiter, quoteDelimiter);

        if (!string.IsNullOrEmpty(expectedEol))
            ValidateLineEndings(loadFilePath, expectedEol, encoding ?? Encoding.UTF8, result);

        return result;
    }

    public ValidationResult ValidateLoadFile(
        TextReader reader,
        string displayPath,
        string format,
        IReadOnlyList<string>? headerColumns,
        IEnumerable<string>? expectedPaths = null,
        BatesNumberConfig? bates = null,
        char columnDelimiter = '\x14',
        char quoteDelimiter = '\xfe')
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(displayPath);
        return ValidateLines(ReadLines(reader), displayPath, format, headerColumns, expectedPaths, bates, columnDelimiter, quoteDelimiter);
    }

    private static ValidationResult ValidateLines(
        IEnumerable<string> lines,
        string loadFilePath,
        string format,
        IReadOnlyList<string>? headerColumns,
        IEnumerable<string>? expectedPaths,
        BatesNumberConfig? bates,
        char columnDelimiter,
        char quoteDelimiter)
    {
        ArgumentNullException.ThrowIfNull(format);
        var result = new ValidationResult();
        var normalizedFormat = format.ToLowerInvariant();
        var columns = headerColumns;
        var ids = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var expectedPathSet = expectedPaths is null ? null : new HashSet<string>(expectedPaths, StringComparer.OrdinalIgnoreCase);
        var expectedSidecarSet = expectedPathSet?.Select(GetSidecarKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        long? previousBates = null;
        long lineNumber = 0;
        var csvRecord = new StringBuilder();

        foreach (var line in lines)
        {
            lineNumber++;
            var record = line;
            if (normalizedFormat == "csv")
            {
                if (csvRecord.Length > 0)
                    csvRecord.Append('\n');
                csvRecord.Append(line);
                if (HasOpenCsvQuote(csvRecord))
                    continue;
                record = csvRecord.ToString();
                csvRecord.Clear();
            }

            if (record.Length == 0)
                continue;

            if (normalizedFormat == "opt")
            {
                ValidateOptLine(record, loadFilePath, lineNumber, result);
                continue;
            }

            var fields = SplitFields(record, normalizedFormat, columnDelimiter, quoteDelimiter);
            if (columns is null)
            {
                columns = fields;
                continue;
            }

            if (fields.Count != columns.Count)
            {
                result.Add(new ValidationFinding(
                    ValidationSeverity.Error,
                    "ColumnCount",
                    $"Expected {columns.Count} columns, got {fields.Count} on line {lineNumber}",
                    loadFilePath,
                    lineNumber));
                continue;
            }

            ValidateRecord(columns, fields, ids, expectedPathSet, expectedSidecarSet, bates, ref previousBates, loadFilePath, lineNumber, result);
        }

        if (normalizedFormat == "csv" && csvRecord.Length > 0)
        {
            result.Add(new ValidationFinding(
                ValidationSeverity.Error,
                "MalformedCsv",
                "Unclosed quote at the end of the CSV file.",
                loadFilePath,
                lineNumber));
        }

        return result;
    }

    private static bool HasOpenCsvQuote(StringBuilder record)
    {
        bool quoted = false;
        for (int i = 0; i < record.Length; i++)
        {
            if (record[i] != '"')
                continue;

            if (quoted && i + 1 < record.Length && record[i + 1] == '"')
            {
                i++;
                continue;
            }

            quoted = !quoted;
        }

        return quoted;
    }

    private static IEnumerable<string> ReadLines(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
            yield return line;
    }

    private static void ValidateRecord(
        IReadOnlyList<string> columns,
        IReadOnlyList<string> fields,
        Dictionary<string, HashSet<string>> ids,
        HashSet<string>? expectedPathSet,
        HashSet<string>? expectedSidecarSet,
        BatesNumberConfig? bates,
        ref long? previousBates,
        string loadFilePath,
        long lineNumber,
        ValidationResult result)
    {
        for (int i = 0; i < columns.Count; i++)
        {
            var column = NormalizeColumn(columns[i]);
            var value = fields[i];
            if (value.Length == 0)
                continue;

            if (column is "CONTROLNUMBER" or "DOCID" or "BATESNUMBER" or "BATES")
            {
                if (!ids.TryGetValue(column, out var seen))
                {
                    seen = new HashSet<string>(StringComparer.Ordinal);
                    ids[column] = seen;
                }

                if (!seen.Add(value))
                {
                    result.Add(new ValidationFinding(ValidationSeverity.Error, "UniqueId", $"Duplicate {columns[i]}: '{value}'", loadFilePath, lineNumber));
                }

                if (bates is not null && (column is "BATESNUMBER" or "BATES"))
                    ValidateBates(value, bates, ref previousBates, loadFilePath, lineNumber, result);
            }

            if (column is "FILEPATH" or "PATH" or "NATIVEPATH" or "TEXTPATH" or "IMAGEPATH")
                ValidatePath(value, expectedPathSet, expectedSidecarSet, loadFilePath, lineNumber, result);
        }
    }

    private static void ValidatePath(string value, HashSet<string>? expectedPathSet, HashSet<string>? expectedSidecarSet, string loadFilePath, long lineNumber, ValidationResult result)
    {
        if (expectedPathSet is null)
            return;

        var normalized = value.Replace('\\', '/');
        if (expectedPathSet.Contains(normalized))
            return;

        if (expectedSidecarSet!.Contains(GetSidecarKey(normalized)))
            return;

        result.Add(new ValidationFinding(
            ValidationSeverity.Error,
            "PathReconciliation",
            $"Load file path '{value}' does not resolve to any archive entry or sidecar file.",
            loadFilePath,
            lineNumber));
    }

    private static string GetSidecarKey(string path)
        => $"{Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty}|{Path.GetFileNameWithoutExtension(path)}";

    private static void ValidateBates(string value, BatesNumberConfig bates, ref long? previousBates, string loadFilePath, long lineNumber, ValidationResult result)
    {
        if (!value.StartsWith(bates.Prefix, StringComparison.Ordinal) ||
            !long.TryParse(value[bates.Prefix.Length..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var current))
            return;

        if (previousBates.HasValue && current != previousBates.Value + bates.Increment)
        {
            var expected = bates.Prefix + (previousBates.Value + bates.Increment).ToString($"D{bates.Digits}", System.Globalization.CultureInfo.InvariantCulture);
            result.Add(new ValidationFinding(ValidationSeverity.Error, "BatesContinuity", $"Expected Bates number {expected}, got '{value}'", loadFilePath, lineNumber));
        }

        previousBates = current;
    }

    private static void ValidateOptLine(string line, string loadFilePath, long lineNumber, ValidationResult result)
    {
        int count = line.Count(c => c == ',') + 1;
        if (count != 7)
            result.Add(new ValidationFinding(ValidationSeverity.Error, "OptBoundary", $"OPT line {lineNumber} has {count} columns, expected 7", loadFilePath, lineNumber));
    }

    private static IReadOnlyList<string> SplitFields(string line, string format, char columnDelimiter, char quoteDelimiter)
    {
        char delimiter = format == "csv" ? ',' : columnDelimiter;
        char quote = format == "csv" ? '"' : quoteDelimiter;
        var fields = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (current == quote)
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == quote)
                {
                    field.Append(quote);
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (current == delimiter && !quoted)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(current);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }

    private static string NormalizeColumn(string column)
        => new string(column.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    public ValidationResult ValidateLineEndings(string loadFilePath, string expectedEol, Encoding? encoding = null)
    {
        var result = new ValidationResult();
        ValidateLineEndings(loadFilePath, expectedEol, encoding ?? Encoding.UTF8, result);
        return result;
    }

    private static void ValidateLineEndings(string loadFilePath, string expectedEol, Encoding encoding, ValidationResult result)
    {
        using var reader = new StreamReader(loadFilePath, encoding, detectEncodingFromByteOrderMarks: true);
        int current;
        long lineNumber = 1;
        while ((current = reader.Read()) != -1)
        {
            if (current == '\r')
            {
                if (reader.Peek() == '\n')
                {
                    reader.Read();
                    if (expectedEol != "\r\n")
                        break;
                }
                else if (expectedEol != "\r")
                {
                    break;
                }
                lineNumber++;
            }
            else if (current == '\n')
            {
                if (expectedEol != "\n")
                    break;
                lineNumber++;
            }
        }

        if (current != -1)
            result.Add(new ValidationFinding(ValidationSeverity.Error, "LineEnding", $"Inconsistent line ending on or before line {lineNumber}.", loadFilePath, lineNumber));
    }
}
