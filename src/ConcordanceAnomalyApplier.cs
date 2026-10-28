using System.Text;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Applies Concordance-specific chaos anomalies to Concordance DAT lines.
/// Concordance uses ASCII 20 (DC4) as the column delimiter and ASCII 254 (þ) as the
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

    private List<int> GetFieldDelimiterPositions(string line, int endPosition)
    {
        var positions = new List<int>();
        bool inQuotes = false;
        for (int i = 0; i < endPosition; i++)
        {
            if (line[i] == this.quoteDelim)
            {
                if (inQuotes && i + 1 < endPosition && line[i + 1] == this.quoteDelim)
                {
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (line[i] == this.fieldDelim && !inQuotes)
            {
                positions.Add(i);
            }
        }

        return positions;
    }

    /// <summary>
    /// Replaces one configured Concordance field delimiter with an unexpected delimiter,
    /// deliberately diverging from the Concordance convention to break import.
    /// </summary>
    private string ApplyConcordanceDelimiterCorruption(string line, out string column, out string description)
    {
        List<int> positions = GetFieldDelimiterPositions(line, line.Length);

        if (positions.Count == 0)
        {
            column = "N/A";
            description = string.Empty;
            return line;
        }

        int targetIndex = this.random.Next(positions.Count);
        int targetPos = positions[targetIndex];
        int delimIndex = targetIndex + 1;
        string replacement = this.fieldDelim == ',' ? "\t" : ",";
        column = $"Delimiter {delimIndex}";
        description = $"Replaced Concordance field delimiter (ascii:{(int)this.fieldDelim}) at position {targetPos} with unexpected delimiter (ascii:{(int)replacement[0]}), breaking Concordance import parsing.";

        return string.Concat(line.AsSpan(0, targetPos), replacement, line.AsSpan(targetPos + 1));
    }

    /// <summary>
    /// Drops one Concordance quote delimiter, leaving a field with an
    /// unbalanced wrapper — the Concordance-specific analogue of the DAT <c>quotes</c> anomaly.
    /// </summary>
    private string ApplyConcordanceQuoteEscape(string line, out string column, out string description)
    {
        // Find the last quote delimiter to drop (mirrors DatAnomalyApplier.ApplyDroppedQuote strategy)
        int lastQuotePos = line.LastIndexOf(this.quoteDelim);
        if (lastQuotePos < 0)
        {
            column = "N/A";
            description = string.Empty;
            return line;
        }

        // Determine which field (1-based count of field delimiters before lastQuotePos)
        int delimCount = GetFieldDelimiterPositions(line, lastQuotePos).Count;

        column = $"Column {delimCount + 1}";
        description = $"Dropped the closing Concordance quote delimiter (ascii:{(int)this.quoteDelim}) on column {delimCount + 1}, breaking Concordance field parsing.";

        return string.Concat(line.AsSpan(0, lastQuotePos), line.AsSpan(lastQuotePos + 1));
    }
}
