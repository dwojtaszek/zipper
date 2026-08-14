using System.Text;

namespace Zipper.Cli;

internal static class ArgumentHelpers
{
    private static readonly Dictionary<string, long> SizeMultipliers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["KB"] = 1024,
        ["MB"] = 1024 * 1024,
        ["GB"] = 1024 * 1024 * 1024,
    };

    public static long? ParseSize(string size)
    {
        size = size.Trim();

        foreach (var (suffix, multiplier) in SizeMultipliers)
        {
            if (size.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var numberPart = size.Substring(0, size.Length - suffix.Length);
                return long.TryParse(numberPart, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value * multiplier : null;
            }
        }

        return null;
    }

    public static DistributionType? GetDistributionFromName(string name)
    {
        return name.ToUpperInvariant() switch
        {
            "PROPORTIONAL" => DistributionType.Proportional,
            "GAUSSIAN" => DistributionType.Gaussian,
            "EXPONENTIAL" => DistributionType.Exponential,
            _ => null,
        };
    }

    public static Encoding? GetEncodingFromName(string name) => EncodingHelper.GetEncoding(name);

    public static LoadFileFormat? GetLoadFileFormat(string name)
    {
        return name.ToUpperInvariant().Replace("-", string.Empty, StringComparison.Ordinal) switch
        {
            "DAT" => LoadFileFormat.Dat,
            "OPT" => LoadFileFormat.Opt,
            "CSV" => LoadFileFormat.Csv,
            "XML" => LoadFileFormat.EdrmXml,
            "EDRMXML" => LoadFileFormat.EdrmXml,
            "CONCORDANCE" => LoadFileFormat.Concordance,
            _ => null,
        };
    }
}
