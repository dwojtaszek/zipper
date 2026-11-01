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
    /// Returns one warning per configured value that exceeds a documented platform limit. Each
    /// checked run is a Bates sequence that generation numbers: the Standard run, or each Production
    /// Set rolling set. <paramref name="documentCount"/> is the number of documents each run numbers.
    /// </summary>
    public static IReadOnlyList<string> Evaluate(
        BatesNumberConfig config,
        long documentCount,
        int rollingCount,
        RollingBatesMode rollingBatesMode)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentOutOfRangeException.ThrowIfLessThan(rollingCount, 1);

        var runs = PlannedRuns(config, documentCount, rollingCount, rollingBatesMode).Distinct().ToList();
        var warnings = new List<string>();

        foreach (var prefix in runs.Select(run => run.Prefix).Distinct(StringComparer.Ordinal))
        {
            if (prefix.Length > MaxPrefixCharacters)
            {
                warnings.Add(
                    $"Bates prefix '{prefix}' is {prefix.Length} characters; Everlaw limits prefixes to {MaxPrefixCharacters} characters. Shorten --bates-prefix.");
            }
        }

        var widestNumber = runs.Max(run => run.Last.ToString(CultureInfo.InvariantCulture).Length);
        var numberWidth = Math.Max(config.Digits, widestNumber) + (config.PageSuffix ? PageSuffixCharacters : 0);
        if (numberWidth > MaxNumberCharacters)
        {
            var suffixNote = config.PageSuffix ? " including the page suffix" : string.Empty;
            var remedy = config.PageSuffix
                ? "Reduce --bates-digits or --bates-start, or drop --bates-page-suffix."
                : "Reduce --bates-digits or --bates-start.";
            warnings.Add(
                $"Bates numbers are {numberWidth} characters{suffixNote}; Everlaw limits Bates numbers to {MaxNumberCharacters} characters. {remedy}");
        }

        foreach (var run in runs)
        {
            var span = run.Last - run.First + 1;
            if (span > MaxRangeNumbers)
            {
                warnings.Add(
                    $"Bates range {run.First} through {run.Last} spans {span.ToString("N0", CultureInfo.InvariantCulture)} Bates numbers; DISCO warns on ranges over {MaxRangeNumbers.ToString("N0", CultureInfo.InvariantCulture)}. Split the documents into smaller Bates ranges.");
            }
        }

        return warnings;
    }

    // Follows the numbering the generation uses: a rolling set takes its listed prefix and start; without a
    // listed start, restart sets begin at --bates-start and continuous sets begin where the previous set ended.
    private static IReadOnlyList<BatesRun> PlannedRuns(
        BatesNumberConfig config,
        long documentCount,
        int rollingCount,
        RollingBatesMode rollingBatesMode)
    {
        var runs = new List<BatesRun>(rollingCount);
        BigInteger nextStart = config.Start;
        var lastOffset = (BigInteger)Math.Max(documentCount - 1, 0) * config.Increment;

        for (var i = 0; i < rollingCount; i++)
        {
            BigInteger start = config.Starts is { } starts && i < starts.Count
                ? starts[i]
                : rollingBatesMode == RollingBatesMode.Restart ? config.Start : nextStart;
            var prefix = config.Prefixes is { } prefixes && i < prefixes.Count ? prefixes[i] : config.Prefix;

            runs.Add(new BatesRun(prefix, start, start + lastOffset));
            nextStart = start + (BigInteger)documentCount * config.Increment;
        }

        return runs;
    }

    /// <summary>One Bates sequence that generation numbers: its prefix and its first and last Bates numbers.</summary>
    private readonly record struct BatesRun(string Prefix, BigInteger First, BigInteger Last);
}
