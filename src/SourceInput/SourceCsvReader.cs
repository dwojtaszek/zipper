using System.Text;

namespace Zipper.SourceInput;

/// <summary>
/// Thin I/O shell for Source CSV intake: parses the CSV text (quoting, escaping, embedded
/// newlines, UTF-8 BOM, blank lines) and feeds the raw rows into the shared
/// <see cref="SourceRecordIntake"/> pipeline, which owns column mapping, path/File Type
/// validation, identity rules, and count rules. The header row must contain FilePath and
/// FileType columns; ControlNumber and BatesNumber are optional; every other column becomes
/// source Metadata for Column Profile mapping.
/// </summary>
internal static class SourceCsvReader
{
    internal static bool TryRead(string filePath, out IReadOnlyList<SourceRecord> records, out string? error, int maxRecords = SourceRecordIntake.MaxSourceRecords)
    {
        records = Array.Empty<SourceRecord>();
        error = null;

        List<(int RecordNumber, List<string> Fields)> rows;
        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            rows = ParseRows(reader, maxRecords);
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

        if (!SourceRecordIntake.TryMapCsvColumns(rows[0].Fields, out var layout, out var headerError))
        {
            error = headerError;
            return false;
        }

        var intake = new SourceRecordIntake($"Source CSV '{filePath}'", maxRecords);
        for (int r = 1; r < rows.Count; r++)
        {
            var row = rows[r].Fields;
            var rowNumber = rows[r].RecordNumber;

            string Cell(int index) => index >= 0 && index < row.Count ? row[index] : string.Empty;

            Dictionary<string, string>? metadata = null;
            foreach (var (index, name) in layout.MetadataColumns)
            {
                var value = Cell(index);
                if (!string.IsNullOrEmpty(value))
                {
                    metadata ??= new Dictionary<string, string>(StringComparer.Ordinal);
                    metadata[name] = value;
                }
            }

            var control = NullIfEmpty(Cell(layout.ControlIndex));
            var bates = NullIfEmpty(Cell(layout.BatesIndex));

            if (!intake.TryAdd(Cell(layout.PathIndex), Cell(layout.TypeIndex), control, bates, metadata, $"Row {rowNumber}", out var rowError))
            {
                error = rowError;
                return false;
            }
        }

        return intake.TryBuild(out records, out error);
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static List<(int RecordNumber, List<string> Fields)> ParseRows(TextReader reader, int maxRecords)
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
                        _ = reader.Read();
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
                        _ = reader.Read();
                    }

                    goto case '\n';
                case '\n':
                    EndField(row, field, fieldWasQuoted);
                    fieldWasQuoted = false;
                    afterClosingQuote = false;
                    if (row.Exists(static f => f.Length > 0))
                    {
                        // Parse-time memory bound: raw rows are materialized before intake
                        // validation, so the cap is enforced here to keep that list bounded
                        // (REQ-207). The intake re-enforces the same cap on validated records.
                        if (rows.Count > maxRecords)
                        {
                            throw new InvalidDataException($"Source CSV exceeds the maximum of {maxRecords} data rows (record {recordNumber}); split the input into multiple runs.");
                        }

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
                if (rows.Count > maxRecords)
                {
                    throw new InvalidDataException($"Source CSV exceeds the maximum of {maxRecords} data rows (record {recordNumber}); split the input into multiple runs.");
                }

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
