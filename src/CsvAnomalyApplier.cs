using System.Text;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Applies RFC 4180-specific chaos anomalies to CSV lines:
/// <list type="bullet">
///   <item><term>csv-quote-escape</term><description>Breaks a doubled-quote escape (<c>""</c>) to a single <c>"</c>, producing an unbalanced quoted field that round-trips correctly in DAT but not CSV.</description></item>
///   <item><term>csv-embedded-newline</term><description>Injects a raw line break inside a quoted field, exploiting the RFC 4180 allowed-but-risky embedded-newline rule.</description></item>
///   <item><term>csv-bom</term><description>Injects a mid-file UTF-8 BOM byte sequence at a line boundary, which spreadsheet readers silently accept at the start but mis-render in the middle.</description></item>
/// </list>
/// </summary>
internal sealed class CsvAnomalyApplier : IAnomalyApplier
{
    private readonly Random random;
    private readonly HashSet<long> bomAnomalyLines = new();

    public CsvAnomalyApplier(Random random)
    {
        this.random = random;
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
            case "csv-quote-escape":
                result = ApplyBrokenDoubledQuote(line, out column, out description);
                break;
            case "csv-embedded-newline":
                result = ApplyEmbeddedNewline(line, out column, out description);
                break;
            case "csv-bom":
                this.bomAnomalyLines.Add(lineNumber);
                return line;
            default:
                return line;
        }

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

    public byte[]? GetEncodingAnomaly(long lineNumber, long nextLineNumber, Encoding encoding, List<ChaosAnomaly> anomalies)
    {
        if (!this.bomAnomalyLines.Remove(lineNumber))
        {
            return null;
        }

        // A mid-file UTF-8 BOM (EF BB BF): spreadsheet readers silently accept a BOM at the
        // very start but render mojibake when it appears in the middle of a file.
        byte[] utf8Bom = Encoding.UTF8.GetPreamble();
        anomalies.Add(new ChaosAnomaly
        {
            LineNumber = $"Boundary {lineNumber}-{nextLineNumber}",
            RecordID = "N/A",
            Column = "N/A",
            ErrorType = "csv-bom",
            Description = "Injected mid-file UTF-8 BOM bytes between CSV records; spreadsheet readers render the BOM as mojibake except at the file start.",
        });

        return utf8Bom;
    }

    /// <summary>
    /// Reduces one instance of a RFC 4180 doubled-quote escape (<c>""</c>) to a single <c>"</c>,
    /// leaving the quoted field with an unbalanced closing quote.
    /// </summary>
    private string ApplyBrokenDoubledQuote(string line, out string column, out string description)
    {
        var positions = new List<int>();
        int searchFrom = 0;
        while (searchFrom < line.Length - 1)
        {
            int pos = line.IndexOf("\"\"", searchFrom, StringComparison.Ordinal);
            if (pos < 0)
            {
                break;
            }

            positions.Add(pos);
            searchFrom = pos + 2;
        }

        if (positions.Count == 0)
        {
            // No existing escaped field: build a valid quoted field and break its escaping so
            // the anomaly still applies to ordinary CSV rows (the common case).
            if (!TryChooseField(line, out int fieldStart, out int fieldEnd, out int chosenFieldNumber) || fieldEnd <= fieldStart)
            {
                column = "N/A";
                description = string.Empty;
                return line;
            }

            string content = line[fieldStart..fieldEnd];
            int middle = content.Length / 2;
            string broken = "\"" + content[..middle] + "\"" + content[middle..] + "\"";
            column = $"Field {chosenFieldNumber}";
            description = $"Injected a broken RFC 4180 quoted field in field {chosenFieldNumber} (unbalanced quote), which DAT-style parsing round-trips but CSV rejects.";
            return string.Concat(line.AsSpan(0, fieldStart), broken, line.AsSpan(fieldEnd));
        }

        int targetPos = positions[this.random.Next(positions.Count)];

        // Count commas before the escape to determine field number (1-based)
        int targetField = 1;
        for (int i = 0; i < targetPos; i++)
        {
            if (line[i] == ',')
            {
                targetField++;
            }
        }

        column = $"Field {targetField}";
        description = $"Reduced RFC 4180 doubled-quote escape (\"\") to a single quote in field {targetField}, breaking round-trip CSV parsing.";

        return string.Concat(line.AsSpan(0, targetPos), "\"", line.AsSpan(targetPos + 2));
    }

    /// <summary>
    /// Injects a raw <c>\r\n</c> inside one quoted field found in <paramref name="line"/>.
    /// RFC 4180 §2.6 permits embedded newlines inside quoted fields, but many parsers reject them.
    /// </summary>
    private string ApplyEmbeddedNewline(string line, out string column, out string description)
    {
        var quotedFields = new List<(int Start, int End)>();
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] != '"' || (i > 0 && line[i - 1] != ','))
            {
                continue;
            }

            int searchFrom = i + 1;
            int end = -1;
            while (searchFrom < line.Length)
            {
                int q = line.IndexOf('"', searchFrom);
                if (q < 0)
                {
                    break;
                }

                // Doubled quote = escaped, skip both
                if (q + 1 < line.Length && line[q + 1] == '"')
                {
                    searchFrom = q + 2;
                    continue;
                }

                end = q;
                break;
            }

            if (end > i + 1)
            {
                quotedFields.Add((i, end));
            }
        }

        if (quotedFields.Count == 0)
        {
            // No quoted field: quote an ordinary field and embed the newline inside it.
            if (!TryChooseField(line, out int fieldStart, out int fieldEnd, out int chosenFieldNumber) || fieldEnd <= fieldStart)
            {
                column = "N/A";
                description = string.Empty;
                return line;
            }

            string content = line[fieldStart..fieldEnd];
            int middle = content.Length / 2;
            string quoted = "\"" + content[..middle] + "\r\n" + content[middle..] + "\"";
            column = $"Field {chosenFieldNumber}";
            description = $"Injected a raw CRLF inside a newly quoted field {chosenFieldNumber}; RFC 4180 allows it but many parsers split the record here.";
            return string.Concat(line.AsSpan(0, fieldStart), quoted, line.AsSpan(fieldEnd));
        }

        var field = quotedFields[this.random.Next(quotedFields.Count)];

        // Insert \r\n in the middle of the quoted field content
        int insertPos = field.Start + 1 + ((field.End - field.Start - 1) / 2);

        // Count field number
        int fieldNumber = 1;
        for (int i = 0; i < field.Start; i++)
        {
            if (line[i] == ',')
            {
                fieldNumber++;
            }
        }

        column = $"Field {fieldNumber}";
        description = $"Injected raw CRLF inside quoted field {fieldNumber}; RFC 4180 allows it but many parsers split the record here.";

        return line.Insert(insertPos, "\r\n");
    }

    private bool TryChooseField(string line, out int start, out int end, out int fieldNumber)
    {
        var fields = new List<(int Start, int End, int Number)>();
        int fieldStart = 0;
        int currentFieldNumber = 1;
        for (int i = 0; i <= line.Length; i++)
        {
            if (i == line.Length || line[i] == ',')
            {
                if (i > fieldStart)
                {
                    fields.Add((fieldStart, i, currentFieldNumber));
                }

                fieldStart = i + 1;
                currentFieldNumber++;
            }
        }

        if (fields.Count == 0)
        {
            start = 0;
            end = 0;
            fieldNumber = 0;
            return false;
        }

        (start, end, fieldNumber) = fields[this.random.Next(fields.Count)];
        return true;
    }
}
