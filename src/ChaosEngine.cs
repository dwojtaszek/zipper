using System.Text;

namespace Zipper;

/// <summary>
/// Chaos Engine: deliberately injects structural and encoding anomalies into load file lines.
/// Acts as a line-level interceptor before output is written to the stream.
/// </summary>
/// <remarks>
/// This class is not thread-safe. Instances should not be shared across threads.
/// </remarks>
internal class ChaosEngine
{
    private static readonly string[] DatChaosTypes = { "mixed-delimiters", "quotes", "columns", "eol", "encoding" };
    private static readonly string[] OptChaosTypes = { "opt-boundary", "opt-columns", "opt-pagecount", "opt-path", "opt-batesid" };
    private static readonly char[] AlternativeDelimiters = { ',', '\t', '|' };

    private readonly HashSet<long> targetLines;
    private readonly HashSet<string> enabledTypes;
    private readonly LoadFileFormat format;
    private readonly string columnDelimiter;
    private readonly string quoteDelimiter;
    private readonly string eol;
    private readonly Random random;
    private readonly List<ChaosAnomaly> anomalies = new();
    private readonly HashSet<long> encodingAnomalyLines = new();
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
    /// <param name="eol">Configured end-of-line string.</param>
    /// <param name="seed">Optional random seed for reproducibility.</param>
    public ChaosEngine(
        long totalLines,
        string? chaosAmount,
        string? chaosTypes,
        LoadFileFormat format,
        string columnDelimiter,
        string quoteDelimiter,
        string eol,
        int? seed = null)
    {
        if (totalLines > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLines), "Chaos Engine does not support load files larger than Int32.MaxValue lines due to Floyd's sampling algorithm constraints.");
        }

        if (totalLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLines), "Chaos Engine requires a positive totalLines count.");
        }

        this.format = format;
        this.columnDelimiter = columnDelimiter;
        this.quoteDelimiter = quoteDelimiter;
        this.eol = string.IsNullOrEmpty(eol) ? "\r\n" : eol;
#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        this.random = seed.HasValue ? new Random(seed.Value) : new Random();
#pragma warning restore S2245

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
        int targetCount = ParseChaosAmount(chaosAmount, (int)totalLines);

        // Pre-compute which lines to corrupt
        this.targetLines = SelectTargetLines((int)totalLines, targetCount, this.random);
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
    public bool ShouldIntercept(long lineNumber) => this.targetLines.Contains(lineNumber);

    /// <summary>
    /// Intercepts and corrupts a line. Returns the modified line.
    /// </summary>
    /// <param name="lineNumber">1-based line number.</param>
    /// <param name="line">Original line content.</param>
    /// <param name="recordId">Record ID (e.g., "DOC00001054" or "HEADER").</param>
    /// <returns>Modified line with injected anomaly.</returns>
    public string Intercept(long lineNumber, string line, string recordId)
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
    public byte[]? GetEncodingAnomaly(long lineNumber, long nextLineNumber, Encoding encoding)
    {
        // Only inject when the Chaos Engine selected the encoding anomaly for this line.
        if (!this.encodingAnomalyLines.Remove(lineNumber))
        {
            return null;
        }

        byte[] invalidBytes;
        string encodingName = encoding.WebName.ToUpperInvariant();

        if (encodingName.Contains("UTF-16"))
        {
            // Unpaired high surrogate in little-endian: 0x00D8 as LE bytes
            invalidBytes = new byte[] { 0x00, 0xD8 };
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

    private static HashSet<long> SelectTargetLines(int totalLines, int count, Random random)
    {
        count = Math.Clamp(count, 0, totalLines);
        var selected = new HashSet<long>(count);

        // Floyd's algorithm: exact unique sample without allocating all line numbers.
        for (long j = (long)totalLines - count + 1; j <= totalLines; j++)
        {
            long candidate = random.NextInt64(1, j + 1);
            if (!selected.Add(candidate))
            {
                selected.Add((long)j);
            }
        }

        return selected;
    }

    private string ApplyDatChaos(long lineNumber, string line, string recordId, string chaosType)
    {
        string result = line;
        string column = "N/A";
        string description;

        switch (chaosType)
        {
            case "mixed-delimiters":
                result = this.ApplyMixedDelimiters(line, out int delimIndex);
                column = $"Delimiter {delimIndex}";
                description = $"Replaced delimiter {delimIndex} with an alternative delimiter character.";
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
            case "encoding":
                // Encoding anomalies are handled separately via GetEncodingAnomaly()
                // Mark the line so invalid bytes are injected after this record boundary.
                this.encodingAnomalyLines.Add(lineNumber);
                return line;
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

    private string ApplyOptChaos(long lineNumber, string line, string recordId, string chaosType)
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
            case "opt-path":
                result = ApplyOptPathCorruption(line);
                description = "Corrupted the image path (Column 3).";
                break;
            case "opt-batesid":
                result = ApplyOptBatesNumberCorruption(line);
                description = "Removed the Bates Number (Column 1).";
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
        if (string.IsNullOrEmpty(this.columnDelimiter))
        {
            delimiterIndex = 0;
            return line;
        }

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

        var alternatives = AlternativeDelimiters.Where(c => c.ToString() != this.columnDelimiter).ToArray();
        if (alternatives.Length == 0)
        {
            return line;
        }

        char replacementChar = alternatives[this.random.Next(alternatives.Length)];

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
                    return line.Insert(insertPos, this.eol);
                }
            }
        }

        // Fallback: insert near middle
        column = "Unknown";
        return line.Insert(line.Length / 2, this.eol);
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

    private static string ApplyOptPathCorruption(string line)
    {
        // OPT format: BatesNumber,Volume,ImagePath,DocBreak,BoxBreak,FolderBreak,PageCount
        var parts = line.Split(',');
        if (parts.Length >= 3)
        {
            parts[2] = "IMAGES\\..\\..\\invalid\\path.tif";
            return string.Join(",", parts);
        }

        return line;
    }

    private static string ApplyOptBatesNumberCorruption(string line)
    {
        int firstComma = line.IndexOf(',');
        if (firstComma >= 0)
        {
            return line.Substring(firstComma); // Leaves empty Bates Number
        }

        return string.Empty;
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
