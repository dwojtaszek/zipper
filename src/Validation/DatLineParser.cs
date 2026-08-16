using System.Text;

namespace Zipper.Validation;

/// <summary>
/// Shared DAT line parser. Centralizes the delimiter-aware field splitter so
/// that post-generation validators and manifest comparison do not each carry
/// their own copy of the same parsing loop.
/// </summary>
public static class DatLineParser
{
    /// <summary>
    /// Splits a single DAT / Concordance line into fields, honouring the
    /// configured column delimiter and quote delimiter.
    /// </summary>
    public static List<string> Parse(string line, char colDelim, char quoteDelim)
    {
        ArgumentNullException.ThrowIfNull(line);
        var fields = new List<string>();
        var currentField = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoteDelim != '\0' && c == quoteDelim)
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == quoteDelim)
                {
                    currentField.Append(quoteDelim);
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == colDelim && !inQuotes)
            {
                fields.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
        fields.Add(currentField.ToString());
        return fields;
    }
}
