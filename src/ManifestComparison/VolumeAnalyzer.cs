namespace Zipper.ManifestComparison;

/// <summary>
/// Volume rollups: per-volume Bates range and status (unchanged/added/removed/
/// changed) for a <see cref="ComparisonResult"/>.
/// Extracted from <see cref="ProductionManifestComparer"/> (#601 PR3).
/// </summary>
internal static class VolumeAnalyzer
{
    public static void AnalyzeVolumes(
        List<ComparisonRecord> priorRecords,
        List<ComparisonRecord> newRecords,
        List<VolumeResult> volumeAnalysis)
    {
        var priorGroups = priorRecords.GroupBy(r => r.ProductionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var newVols = newRecords.GroupBy(r => r.Volume, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var newVolumeRecords = newVols.ToDictionary(
            kvp => kvp.Key,
            kvp => GroupVolumeRecords(kvp.Value),
            StringComparer.OrdinalIgnoreCase);
        var newVolumeRanges = newVols.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Count > 0
                ? $"{kvp.Value.Min(r => r.BatesNumber)} - {kvp.Value.Max(r => r.BatesNumber)}"
                : string.Empty,
            StringComparer.OrdinalIgnoreCase);
        var priorVolumeNames = priorRecords.Select(r => r.Volume).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var priorKvp in priorGroups)
        {
            var priorProdId = priorKvp.Key;
            var priorRecs = priorKvp.Value;

            var priorVols = priorRecs.GroupBy(r => r.Volume, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            var volNames = priorVols.Keys.OrderBy(v => v).ToList();

            foreach (var vol in volNames)
            {
                var priorList = priorVols[vol];
                bool inNew = newVols.TryGetValue(vol, out var newList);

                var priorRange = priorList.Count > 0
                    ? $"{priorList.Min(r => r.BatesNumber)} - {priorList.Max(r => r.BatesNumber)}"
                    : string.Empty;

                var newRange = inNew
                    ? newVolumeRanges[vol]
                    : string.Empty;

                var status = "unchanged";
                if (!inNew)
                {
                    status = "removed";
                }
                else if (newList is not null && VolumeRecordsChanged(
                    priorList.Count,
                    newList.Count,
                    GroupVolumeRecords(priorList),
                    newVolumeRecords[vol]))
                {
                    status = "changed";
                }

                volumeAnalysis.Add(new VolumeResult
                {
                    ProductionId = priorProdId,
                    VolumeName = vol,
                    PriorBatesRange = priorRange,
                    NewBatesRange = newRange,
                    Status = status
                });
            }
        }

        // Also identify Volumes in the New Production Manifest that do not exist in any Prior Production Manifest
        var newVolsOnly = newVols.Keys.Where(v => !priorVolumeNames.Contains(v)).OrderBy(v => v).ToList();
        foreach (var vol in newVolsOnly)
        {
            volumeAnalysis.Add(new VolumeResult
            {
                ProductionId = newRecords.FirstOrDefault()?.ProductionId ?? "NewSet",
                VolumeName = vol,
                PriorBatesRange = string.Empty,
                NewBatesRange = newVolumeRanges[vol],
                Status = "added"
            });
        }
    }

    private static Dictionary<string, Dictionary<string, List<string>>> GroupVolumeRecords(
        IEnumerable<ComparisonRecord> records)
    {
        var grouped = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (!grouped.TryGetValue(record.BatesNumber, out var paths))
            {
                paths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                grouped[record.BatesNumber] = paths;
            }

            if (!paths.TryGetValue(record.FilePath, out var hashes))
            {
                hashes = [];
                paths[record.FilePath] = hashes;
            }

            hashes.Add(record.Hash);
        }

        return grouped;
    }

    private static bool VolumeRecordsChanged(
        int priorCount,
        int newCount,
        Dictionary<string, Dictionary<string, List<string>>> priorRecords,
        Dictionary<string, Dictionary<string, List<string>>> newRecords)
    {
        if (priorCount != newCount || priorRecords.Count != newRecords.Count)
        {
            return true;
        }

        foreach (var (batesNumber, priorPaths) in priorRecords)
        {
            if (!newRecords.TryGetValue(batesNumber, out var newPaths) || priorPaths.Count != newPaths.Count)
            {
                return true;
            }

            foreach (var (path, priorHashes) in priorPaths)
            {
                if (!newPaths.TryGetValue(path, out var newHashes) || !HashesMatch(priorHashes, newHashes))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HashesMatch(List<string> priorHashes, List<string> newHashes)
    {
        if (priorHashes.Count != newHashes.Count)
        {
            return false;
        }

        var priorCounts = CountNonEmptyHashes(priorHashes, out var priorEmptyCount);
        var newCounts = CountNonEmptyHashes(newHashes, out var newEmptyCount);
        var unmatchedPrior = priorCounts.Sum(pair => Math.Max(0, pair.Value - newCounts.GetValueOrDefault(pair.Key)));
        var unmatchedNew = newCounts.Sum(pair => Math.Max(0, pair.Value - priorCounts.GetValueOrDefault(pair.Key)));
        return unmatchedPrior <= newEmptyCount && unmatchedNew <= priorEmptyCount;
    }

    private static Dictionary<string, int> CountNonEmptyHashes(List<string> hashes, out int emptyCount)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        emptyCount = 0;
        foreach (var hash in hashes)
        {
            if (string.IsNullOrEmpty(hash))
            {
                emptyCount++;
            }
            else
            {
                counts[hash] = counts.GetValueOrDefault(hash) + 1;
            }
        }

        return counts;
    }
}
