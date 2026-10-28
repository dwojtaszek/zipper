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
            Description = $"Injected mid-file UTF-8 BOM bytes between CSV records; spreadsheet readers render the BOM as mojibake except at the file start.",
        });

        return utf8Bom;
    }

    /// <summary>
    /// Reduces one instance of a RFC 4180 doubled-quote escape (<c>""</c>) to a single <c>"</c>,
    /// leaving the quoted field with an unbalanced closing quote.
    /// </summary>
    private static string ApplyBrokenDoubledQuote(string line, out string column, out string description)
    {
        // Find the first occurrence of "" within a quoted field
        int pos = line.IndexOf("\"\"", StringComparison.Ordinal);
        if (pos < 0)
        {
            column = "N/A";
            description = string.Empty;
            return line;
        }

        // Count commas before pos to determine field number (1-based)
        int fieldNumber = 1;
        for (int i = 0; i < pos; i++)
        {
            if (line[i] == ',')
            {
                fieldNumber++;
            }
        }

        column = $"Field {fieldNumber}";
        description = $"Reduced RFC 4180 doubled-quote escape (\"\"\"\") to a single quote in field {fieldNumber}, breaking round-trip CSV parsing.";

        // Replace only the first occurrence
        return string.Concat(line.AsSpan(0, pos), "\"", line.AsSpan(pos + 2));
    }

    /// <summary>
    /// Injects a raw <c>\r\n</c> inside the first quoted field found in <paramref name="line"/>.
    /// RFC 4180 §2.6 permits embedded newlines inside quoted fields, but many parsers reject them.
    /// </summary>
    private string ApplyEmbeddedNewline(string line, out string column, out string description)
    {
        // Find a quoted field: starts with " after start-of-line or comma
        int quoteStart = -1;
        if (line.Length > 0 && line[0] == '"')
        {
            quoteStart = 0;
        }
        else
        {
            int commaPos = line.IndexOf(",\"", StringComparison.Ordinal);
            if (commaPos >= 0)
            {
                quoteStart = commaPos + 1;
            }
        }

        if (quoteStart < 0)
        {
            column = "N/A";
            description = string.Empty;
            return line;
        }

        // Find closing quote (scan from after the opening quote)
        int searchFrom = quoteStart + 1;
        int quoteEnd = -1;
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

            quoteEnd = q;
            break;
        }

        if (quoteEnd <= quoteStart + 1)
        {
            column = "N/A";
            description = string.Empty;
            return line;
        }

        // Insert \r\n in the middle of the quoted field content
        int insertPos = quoteStart + 1 + ((quoteEnd - quoteStart - 1) / 2);

        // Count field number
        int fieldNumber = 1;
        for (int i = 0; i < quoteStart; i++)
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
}
