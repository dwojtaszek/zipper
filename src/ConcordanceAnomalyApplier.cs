using System.Text;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Applies Concordance-specific chaos anomalies to Concordance DAT lines.
/// Concordance uses ASCII 20 (§) as the column delimiter and ASCII 254 (þ) as the
/// quote delimiter — distinct from the generic DAT arms in <see cref="DatAnomalyApplier"/>.
/// <list type="bullet">
///   <item><term>concordance-delimiter</term><description>Replaces one Concordance field delimiter (ASCII 20) with an ASCII comma, breaking Concordance import while leaving standard-DAT parsers confused.</description></item>
///   <item><term>concordance-quote-escape</term><description>Drops one ASCII 254 quote delimiter, leaving a field with an unbalanced wrapper — the Concordance-specific analogue of the DAT <c>quotes</c> anomaly.</description></item>
/// </list>
/// </summary>
internal sealed class ConcordanceAnomalyApplier : IAnomalyApplier
{
    private readonly Random random;
    private readonly char fieldDelim;
    private readonly char quoteDelim;

    public ConcordanceAnomalyApplier(Random random, char fieldDelim, char quoteDelim)
    {
        this.random = random;
        this.fieldDelim = fieldDelim;
        this.quoteDelim = quoteDelim;
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
            case "concordance-delimiter":
                result = ApplyConcordanceDelimiterCorruption(line, out column, out description);
                break;
            case "concordance-quote-escape":
                result = ApplyConcordanceQuoteEscape(line, out column, out description);
                break;
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

    /// <summary>
    /// Concordance does not use encoding anomaly bytes between lines.
    /// Always returns <c>null</c>.
    /// </summary>
    public byte[]? GetEncodingAnomaly(long lineNumber, long nextLineNumber, Encoding encoding, List<ChaosAnomaly> anomalies)
        => null;

    /// <summary>
    /// Replaces one Concordance field delimiter (ASCII 20) with an ASCII comma,
    /// deliberately diverging from the Concordance delimiter convention to break import.
    /// </summary>
    private string ApplyConcordanceDelimiterCorruption(string line, out string column, out string description)
    {
        // Collect all positions of the Concordance field delimiter
        var positions = new List<int>();
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == this.fieldDelim)
            {
                positions.Add(i);
            }
        }

        if (positions.Count == 0)
        {
            column = "N/A";
            description = string.Empty;
            return line;
        }

        int targetPos = positions[this.random.Next(positions.Count)];
        int delimIndex = positions.IndexOf(targetPos) + 1;
        column = $"Delimiter {delimIndex}";
        description = $"Replaced Concordance field delimiter (ascii:{(int)this.fieldDelim}) at position {targetPos} with comma (ascii:44), breaking Concordance import parsing.";

        return string.Concat(line.AsSpan(0, targetPos), ",", line.AsSpan(targetPos + 1));
    }

    /// <summary>
    /// Drops one Concordance quote delimiter (ASCII 254), leaving a field with an
    /// unbalanced wrapper — the Concordance-specific analogue of the DAT <c>quotes</c> anomaly.
    /// </summary>
    private static string ApplyConcordanceQuoteEscape(string line, out string column, out string description)
    {
        // Find the last quote delimiter to drop (mirrors DatAnomalyApplier.ApplyDroppedQuote strategy)
        int lastQuotePos = line.LastIndexOf('\u00fe');
        if (lastQuotePos < 0)
        {
            column = "N/A";
            description = string.Empty;
            return line;
        }

        // Determine which field (1-based count of field delimiters before lastQuotePos)
        int delimCount = 0;
        for (int i = 0; i < lastQuotePos; i++)
        {
            if (line[i] == '\u0014')
            {
                delimCount++;
            }
        }

        column = $"Column {delimCount + 1}";
        description = $"Dropped the closing Concordance quote delimiter (ascii:254) on column {delimCount + 1}, breaking Concordance field parsing.";

        return string.Concat(line.AsSpan(0, lastQuotePos), line.AsSpan(lastQuotePos + 1));
    }
}
