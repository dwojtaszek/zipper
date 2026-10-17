using System.Text;
using Xunit;

namespace Zipper.Tests.TestHelpers;

/// <summary>
/// Test-side delimiter-aware line and record parser.
/// Used to verify load file output columns and values without depending on production format implementations.
/// </summary>
public static class DelimitedTestParser
{
    private const char UnicodeBom = '\uFEFF';

    /// <summary>
    /// Parses a single line into delimited fields, taking into account quote delimiters and doubled-quote escapes.
    /// </summary>
    /// <param name="line">The text line to parse.</param>
    /// <param name="delimiter">The column delimiter character.</param>
    /// <param name="quote">The optional quote delimiter character (or '\0' if unquoted).</param>
    /// <returns>A list of parsed field values.</returns>
    public static List<string> ParseLine(string line, char delimiter, char quote = '\0')
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.StartsWith(UnicodeBom))
        {
            line = line[1..];
        }

        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quote != '\0' && c == quote)
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == quote)
                {
                    current.Append(quote);
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        if (inQuotes)
        {
            throw new FormatException($"Unterminated quoted field in line: '{line}'");
        }

        fields.Add(current.ToString());
        return fields;
    }

    /// <summary>
    /// Parses a data line against a set of parsed headers, returning a dictionary mapping column header to field value.
    /// Asserts that the field count exactly matches the header count.
    /// </summary>
    /// <param name="headers">The collection of column header names.</param>
    /// <param name="line">The text line to parse.</param>
    /// <param name="delimiter">The column delimiter character.</param>
    /// <param name="quote">The optional quote delimiter character (or '\0' if unquoted).</param>
    /// <returns>A dictionary mapping column header to field value.</returns>
    public static Dictionary<string, string> ParseRecord(
        IReadOnlyList<string> headers,
        string line,
        char delimiter,
        char quote = '\0')
    {
        ArgumentNullException.ThrowIfNull(headers);
        ArgumentNullException.ThrowIfNull(line);

        var values = ParseLine(line, delimiter, quote);
        Assert.Equal(headers.Count, values.Count);

        var record = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < headers.Count; i++)
        {
            record.Add(headers[i], values[i]);
        }

        return record;
    }
}
