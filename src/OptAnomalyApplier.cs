using System.Text;

namespace Zipper;

internal class OptAnomalyApplier : IAnomalyApplier
{
    private readonly Random random;

    public OptAnomalyApplier(Random random)
    {
        this.random = random;
    }

    public string Apply(long lineNumber, string line, string recordId, string chaosType, List<ChaosAnomaly> anomalies)
    {
        string result = line;
        string description;

        switch (chaosType)
        {
            case "opt-boundary":
                result = ApplyOptBoundaryFlip(line);
                description = "Altered the document boundary flag (Column 4).";
                break;
            case "opt-columns":
                bool addComma = random.Next(2) == 0;
                result = addComma ? line + "," : RemoveOneComma(line);
                description = addComma
                    ? "Added an extra comma to break the required 6-comma format."
                    : "Removed a comma to break the required 6-comma format.";
                break;
            case "opt-pagecount":
                result = ApplyOptPagecountCorruption(line);
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

        anomalies.Add(new ChaosAnomaly
        {
            LineNumber = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
            RecordID = recordId,
            Column = "N/A",
            ErrorType = chaosType,
            Description = description,
        });

        return result;
    }

    public byte[]? GetEncodingAnomaly(long lineNumber, long nextLineNumber, Encoding encoding, List<ChaosAnomaly> anomalies)
    {
        return null; // OPT doesn't have an encoding anomaly by default
    }

    private string ApplyOptBoundaryFlip(string line)
    {
        int comma1 = line.IndexOf(',');
        if (comma1 < 0) return line;
        int comma2 = line.IndexOf(',', comma1 + 1);
        if (comma2 < 0) return line;
        int comma3 = line.IndexOf(',', comma2 + 1);
        if (comma3 < 0) return line;
        int comma4 = line.IndexOf(',', comma3 + 1);

        int fieldStart = comma3 + 1;
        int fieldLength = (comma4 < 0 ? line.Length : comma4) - fieldStart;

        ReadOnlySpan<char> field = line.AsSpan(fieldStart, fieldLength).Trim();
        bool isY = field.Equals("Y", StringComparison.Ordinal);
        string newValue = isY ? string.Empty : "Y";

        return string.Concat(
            line.AsSpan(0, fieldStart),
            newValue,
            comma4 < 0 ? ReadOnlySpan<char>.Empty : line.AsSpan(comma4)
        );
    }

    private string RemoveOneComma(string line)
    {
        int commaPos = line.IndexOf(',', StringComparison.Ordinal);
        if (commaPos < 0)
        {
            return line;
        }

        return string.Concat(line.AsSpan(0, commaPos), line.AsSpan(commaPos + 1));
    }

    private string ApplyOptPagecountCorruption(string line)
    {
        int lastComma = line.LastIndexOf(',');
        if (lastComma < 0)
        {
            return line;
        }

        string corruptedValue = random.Next(2) == 0 ? "ABC" : "-1";
        return string.Concat(line.AsSpan(0, lastComma + 1), corruptedValue);
    }

    private static string ApplyOptPathCorruption(string line)
    {
        int comma1 = line.IndexOf(',');
        if (comma1 < 0) return line;
        int comma2 = line.IndexOf(',', comma1 + 1);
        if (comma2 < 0) return line;
        int comma3 = line.IndexOf(',', comma2 + 1);

        int fieldStart = comma2 + 1;

        return string.Concat(
            line.AsSpan(0, fieldStart),
            "IMAGES\\..\\..\\invalid\\path.tif",
            comma3 < 0 ? ReadOnlySpan<char>.Empty : line.AsSpan(comma3)
        );
    }

    private static string ApplyOptBatesNumberCorruption(string line)
    {
        int firstComma = line.IndexOf(',', StringComparison.Ordinal);
        if (firstComma >= 0)
        {
            return line.Substring(firstComma);
        }

        return string.Empty;
    }
}
