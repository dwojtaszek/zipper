namespace Zipper;

internal class ChaosSampler
{
    private readonly HashSet<long> targetLines;

    public ChaosSampler(long totalLines, string? chaosAmount, Random random)
    {
        if (totalLines > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLines), "Chaos Engine does not support load files larger than Int32.MaxValue lines due to Floyd's sampling algorithm constraints.");
        }

        if (totalLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLines), "Chaos Engine requires a positive totalLines count.");
        }

        int targetCount = ParseChaosAmount(chaosAmount, (int)totalLines);

        this.targetLines = SelectTargetLines((int)totalLines, targetCount, random);
    }

    public bool ShouldIntercept(long lineNumber) => this.targetLines.Contains(lineNumber);

    public static int ParseChaosAmount(string? chaosAmount, int totalLines)
    {
        if (string.IsNullOrEmpty(chaosAmount))
        {
            return Math.Max(1, totalLines / 100);
        }

        if (chaosAmount.EndsWith('%'))
        {
            if (double.TryParse(chaosAmount.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                return Math.Max(1, (int)(totalLines * pct / 100.0));
            }
        }

        if (int.TryParse(chaosAmount, System.Globalization.CultureInfo.InvariantCulture, out var exact))
        {
            return Math.Min(exact, totalLines);
        }

        return Math.Max(1, totalLines / 100);
    }

    public static HashSet<long> SelectTargetLines(int totalLines, int count, Random random)
    {
        count = Math.Clamp(count, 0, totalLines);
        var selected = new HashSet<long>(count);

        for (long j = (long)totalLines - count + 1; j <= totalLines; j++)
        {
            long candidate = random.NextInt64(1, j + 1);
            if (!selected.Add(candidate))
            {
                selected.Add(j);
            }
        }

        return selected;
    }
}
