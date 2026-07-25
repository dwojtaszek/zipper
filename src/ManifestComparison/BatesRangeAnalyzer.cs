namespace Zipper.ManifestComparison;

/// <summary>
/// Pure Bates Number range analysis: gap detection, overlap detection, and
/// skipped-Bates summarization for a <see cref="ComparisonResult"/>.
/// Extracted from <see cref="ProductionManifestComparer"/> (#601 PR3).
/// </summary>
internal static class BatesRangeAnalyzer
{
    public static void AnalyzeBatesRanges(
        List<ComparisonRecord> priorRecords,
        List<ComparisonRecord> newRecords,
        string mode,
        BatesAnalysis batesAnalysis,
        ResultDetails details)
    {
        // 1. Group records by Bates Prefix
        var priorBates = priorRecords.Select(r => r.BatesNumber).Where(b => !string.IsNullOrEmpty(b)).ToList();
        var newBates = newRecords.Select(r => r.BatesNumber).Where(b => !string.IsNullOrEmpty(b)).ToList();

        // Skipped Bates Numbers / Gap detection within the new set
        var gaps = FindGapsInSequence(newBates);
        details.Skipped.AddRange(gaps.Select(g => new BatesRangeReport { Start = g.Start, End = g.End }));
        batesAnalysis.Gaps.AddRange(details.Skipped);

        // Gap detection between prior max and new min in supplemental mode
        if (string.Equals(mode, "supplemental", StringComparison.OrdinalIgnoreCase) && priorBates.Count > 0 && newBates.Count > 0)
        {
            // Group by prefix to find boundaries
            var priorGroups = GroupBatesByPrefix(priorBates);
            var newGroups = GroupBatesByPrefix(newBates);

            foreach (var prefix in newGroups.Keys)
            {
                if (priorGroups.TryGetValue(prefix, out var priorGroup))
                {
                    var maxPrior = priorGroup.Max(g => g.Value);
                    var minNew = newGroups[prefix].Min(g => g.Value);
                    var digits = newGroups[prefix].First().Digits;

                    if (minNew > maxPrior + 1)
                    {
                        var gapStart = FormatBates(maxPrior + 1, prefix, digits);
                        var gapEnd = FormatBates(minNew - 1, prefix, digits);
                        batesAnalysis.Gaps.Add(new BatesRangeReport { Start = gapStart, End = gapEnd });
                    }
                }
            }
        }

        // Bates Number overlaps (duplicates between sets)
        if (priorBates.Count > 0 && newBates.Count > 0)
        {
            var priorSet = new HashSet<string>(priorBates, StringComparer.OrdinalIgnoreCase);
            var overlaps = new List<string>();
            foreach (var b in newBates)
            {
                if (priorSet.Contains(b))
                {
                    overlaps.Add(b);
                }
            }

            if (overlaps.Count > 0)
            {
                var mergedOverlaps = MergeConsecutiveBates(overlaps);
                batesAnalysis.Overlaps.AddRange(mergedOverlaps);
            }
        }

        ResultSummaryBatesAnalysis(batesAnalysis);
    }

    public static void ResultSummaryBatesAnalysis(BatesAnalysis batesAnalysis)
    {
        // Update summary skipped count
        int totalSkipped = 0;
        foreach (var gap in batesAnalysis.Gaps)
        {
            if (TryParseBates(gap.Start, out var p1, out long v1, out _) && TryParseBates(gap.End, out var p2, out long v2, out _) && p1 == p2)
            {
                totalSkipped += (int)(v2 - v1 + 1);
            }
            else
            {
                totalSkipped++;
            }
        }
        batesAnalysis.TotalSkippedBates = totalSkipped;
    }

    public static List<BatesRangeReport> FindGapsInSequence(List<string> batesList)
    {
        var gaps = new List<BatesRangeReport>();
        if (batesList.Count == 0) return gaps;

        var parsed = GroupBatesByPrefix(batesList);

        foreach (var kvp in parsed)
        {
            var prefix = kvp.Key;
            var items = kvp.Value.OrderBy(i => i.Value).ToList();
            if (items.Count <= 1) continue;

            for (int i = 0; i < items.Count - 1; i++)
            {
                var current = items[i].Value;
                var next = items[i + 1].Value;
                var digits = items[i].Digits;

                if (next > current + 1)
                {
                    gaps.Add(new BatesRangeReport
                    {
                        Start = FormatBates(current + 1, prefix, digits),
                        End = FormatBates(next - 1, prefix, digits)
                    });
                }
            }
        }

        return gaps;
    }

    public static Dictionary<string, List<(long Value, int Digits)>> GroupBatesByPrefix(List<string> batesList)
    {
        var groups = new Dictionary<string, List<(long Value, int Digits)>>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in batesList)
        {
            if (TryParseBates(b, out var prefix, out long val, out int digits))
            {
                if (!groups.TryGetValue(prefix, out var list))
                {
                    list = new List<(long Value, int Digits)>();
                    groups[prefix] = list;
                }
                list.Add((val, digits));
            }
        }
        return groups;
    }

    public static List<BatesRangeReport> MergeConsecutiveBates(List<string> batesList)
    {
        var merged = new List<BatesRangeReport>();
        if (batesList.Count == 0) return merged;

        var parsed = GroupBatesByPrefix(batesList);

        foreach (var kvp in parsed)
        {
            var prefix = kvp.Key;
            var items = kvp.Value.OrderBy(i => i.Value).Select(i => i.Value).Distinct().ToList();
            if (items.Count == 0) continue;

            long start = items[0];
            long last = items[0];
            int digits = kvp.Value[0].Digits;

            for (int i = 1; i < items.Count; i++)
            {
                if (items[i] == last + 1)
                {
                    last = items[i];
                }
                else
                {
                    merged.Add(new BatesRangeReport
                    {
                        Start = FormatBates(start, prefix, digits),
                        End = FormatBates(last, prefix, digits)
                    });
                    start = items[i];
                    last = items[i];
                }
            }

            merged.Add(new BatesRangeReport
            {
                Start = FormatBates(start, prefix, digits),
                End = FormatBates(last, prefix, digits)
            });
        }

        return merged;
    }

    public static bool TryParseBates(string bates, out string prefix, out long num, out int digits)
    {
        prefix = string.Empty;
        num = 0;
        digits = 0;
        if (string.IsNullOrEmpty(bates)) return false;

        int idx = bates.Length - 1;
        while (idx >= 0 && char.IsDigit(bates[idx]))
        {
            idx--;
        }

        prefix = bates.Substring(0, idx + 1);
        string numPart = bates.Substring(idx + 1);
        if (numPart.Length == 0) return false;

        digits = numPart.Length;
        return long.TryParse(numPart, System.Globalization.CultureInfo.InvariantCulture, out num);
    }

    public static string FormatBates(long value, string prefix, int digits)
    {
        return $"{prefix}{value.ToString($"D{digits}", System.Globalization.CultureInfo.InvariantCulture)}";
    }
}
