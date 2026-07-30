using System.Text;

namespace Zipper.SourceInput;

/// <summary>
/// Reads a source-driven generation CSV into <see cref="SourceRecord"/> rows. The header row
/// must contain FilePath and FileType columns; ControlNumber and BatesNumber are optional;
/// every other column becomes source Metadata for Column Profile mapping. Quoted fields,
/// escaped quotes, embedded newlines, and UTF-8 BOMs are supported.
/// </summary>
internal static class SourceCsvReader
{
    internal static bool TryRead(string filePath, out IReadOnlyList<SourceRecord> records, out string? error)
    {
        records = Array.Empty<SourceRecord>();
        error = null;

        List<(int RecordNumber, List<string> Fields)> rows;
        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            rows = ParseRows(reader);
        }
        catch (InvalidDataException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Cannot read source CSV '{filePath}': {ex.Message}";
            return false;
        }

        if (rows.Count == 0)
        {
            error = $"Source CSV '{filePath}' is empty (no header row).";
            return false;
        }

        var header = rows[0].Fields;
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

        var result = new List<SourceRecord>(rows.Count - 1);
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r].Fields;
            var rowNumber = rows[r].RecordNumber;

            string Cell(int index) => index >= 0 && index < row.Count ? row[index] : string.Empty;

            if (!SourcePathSanitizer.TryNormalize(Cell(pathIndex), out var relativePath, out var pathError))
            {
                error = $"Row {rowNumber}: invalid FilePath: {pathError}";
                return false;
            }

            var fileType = Cell(typeIndex).Trim().TrimStart('.').ToLowerInvariant();
            if (fileType.Length == 0)
            {
                error = $"Row {rowNumber}: FileType is empty.";
                return false;
            }

            if (!FileGeneratorFactory.IsKnownType(fileType))
            {
                error = $"Row {rowNumber}: unsupported File Type '{fileType}'. Supported types: pdf, jpg, tiff, eml, docx, xlsx.";
                return false;
            }

            var pathExtension = Path.GetExtension(relativePath).ToLowerInvariant();
            if (!SourceFileTypeMap.TryFromExtension(pathExtension, out var pathType)
                || !string.Equals(pathType, fileType, StringComparison.Ordinal))
            {
                error = $"Row {rowNumber}: FilePath extension '{pathExtension}' does not match FileType '{fileType}'.";
                return false;
            }

            if (!seenPaths.Add(relativePath))
            {
                error = $"Row {rowNumber}: Duplicate FilePath '{relativePath}'.";
                return false;
            }

            var control = NullIfEmpty(Cell(controlIndex));
            var bates = NullIfEmpty(Cell(batesIndex));

            if (control is not null && !IsValidIdentityValue(control))
            {
                error = $"Row {rowNumber}: ControlNumber contains invalid characters (control characters and path separators are not allowed).";
                return false;
            }

            if (bates is not null && !IsValidIdentityValue(bates))
            {
                error = $"Row {rowNumber}: BatesNumber contains invalid characters (control characters and path separators are not allowed).";
                return false;
            }

            Dictionary<string, string>? metadata = null;
            foreach (var (index, name) in metadataColumns)
            {
                var value = Cell(index);
                if (!string.IsNullOrEmpty(value))
                {
                    metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    metadata[name] = value;
                }
            }

            result.Add(new SourceRecord
            {
                RelativePath = relativePath,
                FileType = fileType,
                ControlNumber = control,
                BatesNumber = bates,
                Metadata = metadata,
            });
        }

        if (result.Count == 0)
        {
            error = $"Source CSV '{filePath}' contains no data rows.";
            return false;
        }

        records = result;
        return true;
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

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

    private static List<(int RecordNumber, List<string> Fields)> ParseRows(TextReader reader)
    {
        var rows = new List<(int, List<string>)>();
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;
        var fieldWasQuoted = false;
        var afterClosingQuote = false;
        var recordNumber = 1;

        while (true)
        {
            var next = reader.Read();
            char c;
            if (next < 0)
            {
                if (inQuotes)
                {
                    throw new InvalidDataException($"Source CSV has an unterminated quoted field (record {recordNumber}).");
                }

                break;
            }

            c = (char)next;

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                    }
                    else
                    {
                        inQuotes = false;
                        afterClosingQuote = true;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (afterClosingQuote && c != ',' && c != '\r' && c != '\n')
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                throw new InvalidDataException($"Source CSV has an unexpected character after a closing quote (record {recordNumber}).");
            }

            switch (c)
            {
                case '"' when field.Length == 0 && !fieldWasQuoted:
                    inQuotes = true;
                    fieldWasQuoted = true;
                    break;
                case ',':
                    EndField(row, field, fieldWasQuoted);
                    fieldWasQuoted = false;
                    afterClosingQuote = false;
                    break;
                case '\r':
                    if (reader.Peek() == '\n')
                    {
                        reader.Read();
                    }

                    goto case '\n';
                case '\n':
                    EndField(row, field, fieldWasQuoted);
                    fieldWasQuoted = false;
                    afterClosingQuote = false;
                    if (row.Exists(static f => f.Length > 0))
                    {
                        rows.Add((recordNumber, row));
                    }

                    row = new List<string>();
                    recordNumber++;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0 || fieldWasQuoted)
        {
            EndField(row, field, fieldWasQuoted);
            if (row.Exists(static f => f.Length > 0))
            {
                rows.Add((recordNumber, row));
            }
        }

        return rows;
    }

    private static void EndField(List<string> row, StringBuilder field, bool wasQuoted)
    {
        row.Add(wasQuoted ? field.ToString() : field.ToString().Trim());
        field.Clear();
    }

}
