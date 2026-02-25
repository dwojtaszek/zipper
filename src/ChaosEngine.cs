using System.Text;

namespace Zipper;

/// <summary>
/// Chaos Engine: deliberately injects structural and encoding anomalies into load file lines.
/// Acts as a line-level interceptor before output is written to the stream.
/// </summary>
internal class ChaosEngine
{
    private static readonly string[] DatChaosTypes = { "mixed-delimiters", "quotes", "columns", "eol", "encoding" };
    private static readonly string[] OptChaosTypes = { "opt-boundary", "opt-columns", "opt-pagecount" };
    private static readonly char[] AlternativeDelimiters = { ',', '\t', '|' };

    private readonly HashSet<int> targetLines;
    private readonly HashSet<string> enabledTypes;
    private readonly LoadFileFormat format;
    private readonly string columnDelimiter;
    private readonly string quoteDelimiter;
    private readonly Random random;
    private readonly List<ChaosAnomaly> anomalies = new();
    private int anomalyTypeIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChaosEngine"/> class.
    /// </summary>
    /// <param name="totalLines">Total number of lines (including header).</param>
    /// <param name="chaosAmount">Amount string: percentage (e.g., "1%") or exact count (e.g., "500").</param>
    /// <param name="chaosTypes">Comma-separated type filter, or null for all.</param>
    /// <param name="format">Load file format (Dat or Opt).</param>
    /// <param name="columnDelimiter">Configured column delimiter.</param>
    /// <param name="quoteDelimiter">Configured quote delimiter.</param>
    /// <param name="seed">Optional random seed for reproducibility.</param>
    public ChaosEngine(
        int totalLines,
        string? chaosAmount,
        string? chaosTypes,
        LoadFileFormat format,
        string columnDelimiter,
        string quoteDelimiter,
        int? seed = null)
    {
        this.format = format;
        this.columnDelimiter = columnDelimiter;
        this.quoteDelimiter = quoteDelimiter;
        this.random = seed.HasValue ? new Random(seed.Value) : new Random();

        // Determine enabled types
        var validTypes = format == LoadFileFormat.Opt ? OptChaosTypes : DatChaosTypes;
        if (!string.IsNullOrEmpty(chaosTypes))
        {
            this.enabledTypes = new HashSet<string>(
                chaosTypes.Split(',').Select(t => t.Trim().ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
            this.enabledTypes.IntersectWith(validTypes);
        }
        else
        {
            this.enabledTypes = new HashSet<string>(validTypes, StringComparer.OrdinalIgnoreCase);
        }

        // If quote-delim is "none", disable the quotes chaos type
        if (string.IsNullOrEmpty(quoteDelimiter))
        {
            this.enabledTypes.Remove("quotes");
        }

        // Compute target count
        int targetCount = ParseChaosAmount(chaosAmount, totalLines);

        // Pre-compute which lines to corrupt
        this.targetLines = SelectTargetLines(totalLines, targetCount, this.random);
    }

    /// <summary>
    /// Gets the list of injected anomalies for audit output.
    /// </summary>
    public IReadOnlyList<ChaosAnomaly> Anomalies => this.anomalies;

    /// <summary>
    /// Determines if a line should be intercepted by the chaos engine.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <returns>True if the line should be corrupted.</returns>
    public bool ShouldIntercept(int lineNumber) => this.targetLines.Contains(lineNumber);

    /// <summary>
    /// Intercepts and corrupts a line. Returns the modified line.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <param name="line">Original line content.</param>
    /// <param name="recordId">Record ID (e.g., "DOC00001054" or "HEADER").</param>
    /// <returns>Modified line with injected anomaly.</returns>
    public string Intercept(int lineNumber, string line, string recordId)
    {
        if (this.enabledTypes.Count == 0)
        {
            return line;
        }

        var typeList = this.enabledTypes.ToArray();
        var chosenType = typeList[this.anomalyTypeIndex % typeList.Length];
        this.anomalyTypeIndex++;

        return this.format == LoadFileFormat.Opt
            ? this.ApplyOptChaos(lineNumber, line, recordId, chosenType)
            : this.ApplyDatChaos(lineNumber, line, recordId, chosenType);
    }

    /// <summary>
    /// Generates an encoding anomaly (invalid bytes) to inject between two lines.
    /// </summary>
    /// <param name="lineNumber">Line number after which to inject.</param>
    /// <param name="nextLineNumber">Next line number.</param>
    /// <param name="encoding">Target encoding.</param>
    /// <returns>Invalid byte array, or null if encoding chaos is not enabled or not targeted.</returns>
    public byte[]? GetEncodingAnomaly(int lineNumber, int nextLineNumber, Encoding encoding)
    {
        if (!this.enabledTypes.Contains("encoding"))
        {
            return null;
        }

        // Only inject between lines that are targeted
        if (!this.targetLines.Contains(lineNumber))
        {
            return null;
        }

        byte[] invalidBytes;
        string encodingName = encoding.WebName.ToUpperInvariant();

        if (encodingName.Contains("UTF-16"))
        {
            // Invalid UTF-16 surrogate: high surrogate without low surrogate
            invalidBytes = new byte[] { 0xD8, 0x00 };
        }
        else if (encodingName.Contains("UTF-8") || encodingName == "UTF-8")
        {
            // Invalid UTF-8 continuation byte without start byte
            invalidBytes = new byte[] { 0xFE, 0xFF };
        }
        else
        {
            // For ASCII/Windows-1252: inject bytes > 127 that are undefined
            invalidBytes = new byte[] { 0x81, 0x8D, 0x8F };
        }

        this.anomalies.Add(new ChaosAnomaly
        {
            LineNumber = $"Boundary {lineNumber}-{nextLineNumber}",
            RecordID = "N/A",
            Column = "N/A",
            ErrorType = "encoding",
            Description = $"Injected invalid {encoding.EncodingName} byte sequence between lines.",
        });

        return invalidBytes;
    }

    private static int ParseChaosAmount(string? chaosAmount, int totalLines)
    {
        if (string.IsNullOrEmpty(chaosAmount))
        {
            // Default: 1%
            return Math.Max(1, totalLines / 100);
        }

        if (chaosAmount.EndsWith('%'))
        {
            if (double.TryParse(chaosAmount.TrimEnd('%'), out var pct))
            {
                return Math.Max(1, (int)(totalLines * pct / 100.0));
            }
        }

        if (int.TryParse(chaosAmount, out var exact))
        {
            return Math.Min(exact, totalLines);
        }

        return Math.Max(1, totalLines / 100);
    }

    private static HashSet<int> SelectTargetLines(int totalLines, int count, Random random)
    {
        var lines = new HashSet<int>();
        int attempts = 0;
        int maxAttempts = count * 10;

        while (lines.Count < count && attempts < maxAttempts)
        {
            // Line numbers are 1-based; include header (line 1) as a possible target
            int line = random.Next(1, totalLines + 1);
            lines.Add(line);
            attempts++;
        }

        return lines;
    }

    private string ApplyDatChaos(int lineNumber, string line, string recordId, string chaosType)
    {
        string result = line;
        string column = "N/A";
        string description;

        switch (chaosType)
        {
            case "mixed-delimiters":
                result = this.ApplyMixedDelimiters(line, out int delimIndex);
                column = $"Delimiter {delimIndex}";
                var replacement = result != line ? result[result.IndexOf(AlternativeDelimiters.FirstOrDefault(c => result.Contains(c)))] : '?';
                description = $"Replaced delimiter {delimIndex} with a char:{(result != line ? this.GetReplacementChar(line, result) : "?")} character.";
                break;
            case "quotes":
                result = this.ApplyDroppedQuote(line, out string affectedColumn);
                column = affectedColumn;
                description = $"Omitted the closing {FormatDelimiterDisplay(this.quoteDelimiter)} character on column {affectedColumn}.";
                break;
            case "columns":
                bool added = this.random.Next(2) == 0;
                result = this.ApplyColumnShift(line, added);
                description = added
                    ? "Added an extra column delimiter to break expected column count."
                    : "Removed a column delimiter to break expected column count.";
                break;
            case "eol":
                result = this.ApplyRawNewline(line, out string eolColumn);
                column = eolColumn;
                description = $"Injected raw unescaped newline into field {eolColumn}.";
                break;
            default:
                description = $"Unknown chaos type: {chaosType}";
                break;
        }

        this.anomalies.Add(new ChaosAnomaly
        {
            LineNumber = lineNumber.ToString(),
            RecordID = recordId,
            Column = column,
            ErrorType = chaosType,
            Description = description,
        });

        return result;
    }

    private string ApplyOptChaos(int lineNumber, string line, string recordId, string chaosType)
    {
        string result = line;
        string description;

        switch (chaosType)
        {
            case "opt-boundary":
                result = this.ApplyOptBoundaryFlip(line);
                description = "Altered the document boundary flag (Column 4).";
                break;
            case "opt-columns":
                bool addComma = this.random.Next(2) == 0;
                result = addComma ? line + "," : this.RemoveOneComma(line);
                description = addComma
                    ? "Added an extra comma to break the required 6-comma format."
                    : "Removed a comma to break the required 6-comma format.";
                break;
            case "opt-pagecount":
                result = this.ApplyOptPagecountCorruption(line);
                description = "Replaced page count integer with invalid value.";
                break;
            default:
                description = $"Unknown OPT chaos type: {chaosType}";
                break;
        }

        this.anomalies.Add(new ChaosAnomaly
        {
            LineNumber = lineNumber.ToString(),
            RecordID = recordId,
            Column = "N/A",
            ErrorType = chaosType,
            Description = description,
        });

        return result;
    }

    private string ApplyMixedDelimiters(string line, out int delimiterIndex)
    {
        // Find all delimiter positions
        var positions = new List<int>();
        int searchStart = 0;
        while (searchStart < line.Length)
        {
            int pos = line.IndexOf(this.columnDelimiter, searchStart, StringComparison.Ordinal);
            if (pos < 0)
            {
                break;
            }

            positions.Add(pos);
            searchStart = pos + this.columnDelimiter.Length;
        }

        if (positions.Count == 0)
        {
            delimiterIndex = 0;
            return line;
        }

        // Pick exactly one delimiter to replace
        delimiterIndex = this.random.Next(positions.Count) + 1;
        int targetPos = positions[delimiterIndex - 1];
        char replacementChar = AlternativeDelimiters[this.random.Next(AlternativeDelimiters.Length)];

        return string.Concat(
            line.AsSpan(0, targetPos),
            replacementChar.ToString(),
            line.AsSpan(targetPos + this.columnDelimiter.Length));
    }

    private string ApplyDroppedQuote(string line, out string column)
    {
        // Find the last occurrence of the quote delimiter and remove it
        int lastQuotePos = line.LastIndexOf(this.quoteDelimiter, StringComparison.Ordinal);
        if (lastQuotePos < 0)
        {
            column = "Unknown";
            return line;
        }

        // Estimate which column we're in by counting delimiters before this position
        int delimCount = 0;
        int searchPos = 0;
        while (searchPos < lastQuotePos)
        {
            int pos = line.IndexOf(this.columnDelimiter, searchPos, StringComparison.Ordinal);
            if (pos < 0 || pos >= lastQuotePos)
            {
                break;
            }

            delimCount++;
            searchPos = pos + this.columnDelimiter.Length;
        }

        column = $"Column {delimCount + 1}";

        return string.Concat(
            line.AsSpan(0, lastQuotePos),
            line.AsSpan(lastQuotePos + this.quoteDelimiter.Length));
    }

    private string ApplyColumnShift(string line, bool add)
    {
        if (add)
        {
            // Add a delimiter near the middle
            int midpoint = line.Length / 2;
            return line.Insert(midpoint, this.columnDelimiter);
        }

        // Remove one delimiter
        int delimPos = line.IndexOf(this.columnDelimiter, StringComparison.Ordinal);
        if (delimPos < 0)
        {
            return line;
        }

        return string.Concat(
            line.AsSpan(0, delimPos),
            line.AsSpan(delimPos + this.columnDelimiter.Length));
    }

    private string ApplyRawNewline(string line, out string column)
    {
        // Inject a raw \r\n into a text field (between a pair of quote delimiters)
        if (!string.IsNullOrEmpty(this.quoteDelimiter))
        {
            int firstQuote = line.IndexOf(this.quoteDelimiter, StringComparison.Ordinal);
            if (firstQuote >= 0)
            {
                int afterFirst = firstQuote + this.quoteDelimiter.Length;
                int secondQuote = line.IndexOf(this.quoteDelimiter, afterFirst, StringComparison.Ordinal);
                if (secondQuote > afterFirst)
                {
                    int insertPos = afterFirst + ((secondQuote - afterFirst) / 2);
                    column = "Field 1";
                    return line.Insert(insertPos, "\r\n");
                }
            }
        }

        // Fallback: insert near middle
        column = "Unknown";
        return line.Insert(line.Length / 2, "\r\n");
    }

    private string ApplyOptBoundaryFlip(string line)
    {
        // OPT format: columns separated by commas, column 4 is the doc boundary (Y or empty)
        var parts = line.Split(',');
        if (parts.Length >= 4)
        {
            parts[3] = parts[3].Trim() == "Y" ? string.Empty : "Y";
            return string.Join(",", parts);
        }

        return line;
    }

    private string RemoveOneComma(string line)
    {
        int commaPos = line.IndexOf(',');
        if (commaPos < 0)
        {
            return line;
        }

        return string.Concat(line.AsSpan(0, commaPos), line.AsSpan(commaPos + 1));
    }

    private string ApplyOptPagecountCorruption(string line)
    {
        // Last column in OPT is the page count
        int lastComma = line.LastIndexOf(',');
        if (lastComma < 0)
        {
            return line;
        }

        string corruptedValue = this.random.Next(2) == 0 ? "ABC" : "-1";
        return string.Concat(line.AsSpan(0, lastComma + 1), corruptedValue);
    }

    private char GetReplacementChar(string original, string modified)
    {
        for (int i = 0; i < Math.Min(original.Length, modified.Length); i++)
        {
            if (original[i] != modified[i])
            {
                return modified[i];
            }
        }

        return '?';
    }

    private static string FormatDelimiterDisplay(string delimiter)
    {
        if (string.IsNullOrEmpty(delimiter))
        {
            return "none";
        }

        int code = delimiter[0];
        return $"ascii:{code}";
    }
}
