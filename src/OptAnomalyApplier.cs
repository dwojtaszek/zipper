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
            case "opt-batesnumber":
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
        var parts = line.Split(',');
        if (parts.Length >= 4)
        {
            parts[3] = string.Equals(parts[3].Trim(), "Y", StringComparison.Ordinal) ? string.Empty : "Y";
            return string.Join(",", parts);
        }

        return line;
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
        int firstComma = line.IndexOf(',', StringComparison.Ordinal);
        if (firstComma >= 0)
        {
            return line.Substring(firstComma);
        }

        return string.Empty;
    }
}
