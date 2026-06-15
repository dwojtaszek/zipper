using System.Text;

namespace Zipper;

internal class DatAnomalyApplier : IAnomalyApplier
{
    private static readonly char[] AlternativeDelimiters = { ',', '\t', '|' };

    private readonly Random random;
    private readonly string columnDelimiter;
    private readonly string quoteDelimiter;
    private readonly string eol;
    private readonly HashSet<long> encodingAnomalyLines = new();
    private readonly char[] alternatives;

    public DatAnomalyApplier(Random random, string columnDelimiter, string quoteDelimiter, string eol)
    {
        this.random = random;
        this.columnDelimiter = columnDelimiter;
        this.quoteDelimiter = quoteDelimiter;
        this.eol = eol;
        this.alternatives = AlternativeDelimiters.Where(c => !string.Equals(c.ToString(), columnDelimiter, StringComparison.Ordinal)).ToArray();
    }

    public string Apply(long lineNumber, string line, string recordId, string chaosType, List<ChaosAnomaly> anomalies)
    {
        if (chaosType == "encoding")
        {
            encodingAnomalyLines.Add(lineNumber);
            return line;
        }

        (string result, string column, string description) = chaosType switch
        {
            "mixed-delimiters" => (
                ApplyMixedDelimiters(line, out int delimIndex),
                $"Delimiter {delimIndex}",
                $"Replaced delimiter {delimIndex} with an alternative delimiter character."
            ),
            "quotes" => (
                ApplyDroppedQuote(line, out string affectedColumn),
                affectedColumn,
                $"Omitted the closing {FormatDelimiterDisplay(quoteDelimiter)} character on column {affectedColumn}."
            ),
            "columns" => random.Next(2) == 0
                ? (ApplyColumnShift(line, true), "N/A", "Added an extra column delimiter to break expected column count.")
                : (ApplyColumnShift(line, false), "N/A", "Removed a column delimiter to break expected column count."),
            "eol" => (
                ApplyRawNewline(line, out string eolColumn),
                eolColumn,
                $"Injected raw unescaped newline into field {eolColumn}."
            ),
            _ => (line, "N/A", $"Unknown chaos type: {chaosType}")
        };

        anomalies.Add(new ChaosAnomaly
        {
            LineNumber = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RecordID = recordId,
            Column = column,
            ErrorType = chaosType,
            Description = description,
        });

        return result;
    }

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
