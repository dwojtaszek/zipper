using System.Globalization;
using System.Numerics;
using Zipper.Config;

namespace Zipper;

/// <summary>
/// Compares a Bates configuration with the limits that review platforms document: the Everlaw
/// prefix and Bates Number lengths and the DISCO 30,000 Bates range warning (REQ-240). The
/// findings are advisory; generation still uses the configured values.
/// </summary>
public static class BatesPlatformLimits
{
    /// <summary>Everlaw maximum Bates prefix length, in characters.</summary>
    public const int MaxPrefixCharacters = 36;

    /// <summary>Everlaw maximum Bates Number length, in characters.</summary>
    public const int MaxNumberCharacters = 16;

    /// <summary>DISCO Bates range size that triggers its validation warning.</summary>
    public const int MaxRangeNumbers = 30_000;

    // "_NNN" page suffix (REQ-239): three digits cover pages 1 through 999.
    private const int PageSuffixCharacters = 4;

    /// <summary>
    /// Returns one warning per configured value that exceeds a documented platform limit.
    /// <paramref name="count"/> is the number of documents the configuration numbers; a null or
    /// non-positive count skips the range check.
    /// </summary>
    public static IReadOnlyList<string> Evaluate(BatesNumberConfig config, long? count)
    {
        ArgumentNullException.ThrowIfNull(config);

        var warnings = new List<string>();

        var prefixes = config.Prefixes is { Count: > 0 } listed ? listed : new[] { config.Prefix };
        foreach (var prefix in prefixes)
        {
            if (prefix.Length > MaxPrefixCharacters)
            {
                warnings.Add(
                    $"Bates prefix '{prefix}' is {prefix.Length} characters; Everlaw limits prefixes to {MaxPrefixCharacters} characters. Shorten --bates-prefix.");
            }
        }

        var numberWidth = Math.Max(config.Digits, WidestNumberWidth(config, count))
            + (config.PageSuffix ? PageSuffixCharacters : 0);
        if (numberWidth > MaxNumberCharacters)
        {
            var suffixNote = config.PageSuffix ? " including the page suffix" : string.Empty;
            var remedy = config.PageSuffix
                ? "Reduce --bates-digits or --bates-start, or drop --bates-page-suffix."
                : "Reduce --bates-digits or --bates-start.";
            warnings.Add(
                $"Bates numbers are {numberWidth} characters{suffixNote}; Everlaw limits Bates numbers to {MaxNumberCharacters} characters. {remedy}");
        }

        if (count is > 0)
        {
            BigInteger span = ((BigInteger)count.Value - 1) * config.Increment + 1;
            if (span > MaxRangeNumbers)
            {
                BigInteger end = config.Start + span - 1;
                warnings.Add(
                    $"Bates range {config.Start} through {end} spans {span.ToString("N0", CultureInfo.InvariantCulture)} Bates numbers; DISCO warns on ranges over {MaxRangeNumbers.ToString("N0", CultureInfo.InvariantCulture)}. Split the documents into smaller Bates ranges.");
            }
        }

        return warnings;
    }

    // Increment is positive, so the last number generated is the widest.
    private static int WidestNumberWidth(BatesNumberConfig config, long? count)
    {
        BigInteger last = config.Start;
        if (count is > 0)
        {
            last += ((BigInteger)count.Value - 1) * config.Increment;
        }

        return last.ToString(CultureInfo.InvariantCulture).Length;
    }
}
