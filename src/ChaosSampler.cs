namespace Zipper;

internal class ChaosSampler
{
    private readonly HashSet<long> targetLines;

    public ChaosSampler(long totalLines, string? chaosAmount, Random random)
    {
        ValidateTotalLines(totalLines);

        int targetCount = ParseChaosAmount(chaosAmount, (int)totalLines);
        this.targetLines = SelectTargetLines((int)totalLines, targetCount, random);
    }

    internal static void ValidateTotalLines(long totalLines)
    {
        if (totalLines > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLines), "Chaos Engine does not support load files larger than Int32.MaxValue lines due to Floyd's sampling algorithm constraints.");
        }

        if (totalLines <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLines), "Chaos Engine requires a positive totalLines count.");
        }
    }

    public bool ShouldIntercept(long lineNumber) => this.targetLines.Contains(lineNumber);

    public static int ParseChaosAmount(string? chaosAmount, int totalLines)
    {
        if (string.IsNullOrEmpty(chaosAmount))
        {
            return Math.Max(1, totalLines / 100);
        }

        string trimmed = chaosAmount.Trim();

        if (trimmed.Length > 0 && trimmed[^1] == '%')
        {
            string pctStr = trimmed[..^1].Trim();
            if (double.TryParse(pctStr, System.Globalization.CultureInfo.InvariantCulture, out var pct))
            {
                if (pct < 0)
                {
                    throw new ArgumentException("Chaos amount percentage cannot be negative.", nameof(chaosAmount));
                }
                if (pct == 0)
                {
                    return 0;
                }
                if (pct == 0)
                {
                    return 0;
                }
                var count = (int)(totalLines * pct / 100.0);
                if (count < 1)
                {
                    count = 1;
                }
                return count;
            }
            throw new ArgumentException($"Invalid chaos amount percentage format: '{chaosAmount}'.", nameof(chaosAmount));
        }

        if (int.TryParse(trimmed, System.Globalization.CultureInfo.InvariantCulture, out var exact))
        {
            if (exact < 0)
            {
                throw new ArgumentException("Chaos amount cannot be negative.", nameof(chaosAmount));
            }
            if (exact == 0)
            {
                return 0;
            }
            return Math.Min(exact, totalLines);
        }

        throw new ArgumentException($"Invalid chaos amount format: '{chaosAmount}'. Must be a percentage (e.g., '1%') or an exact count (e.g., '500').", nameof(chaosAmount));
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
