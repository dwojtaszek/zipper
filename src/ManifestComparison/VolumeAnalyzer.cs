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

                var newRange = inNew && newList is not null && newList.Count > 0
                    ? $"{newList.Min(r => r.BatesNumber)} - {newList.Max(r => r.BatesNumber)}"
                    : string.Empty;

                var status = "unchanged";
                if (!inNew)
                {
                    status = "removed";
                }
                else if (newList is not null)
                {
                    var priorSet = new Dictionary<string, ComparisonRecord>(StringComparer.OrdinalIgnoreCase);
                    foreach (var r in priorList)
                    {
                        if (!string.IsNullOrEmpty(r.BatesNumber))
                        {
                            priorSet[r.BatesNumber] = r;
                        }
                    }

                    bool hasChanges = false;
                    foreach (var nr in newList)
                    {
                        if (priorSet.TryGetValue(nr.BatesNumber, out var pr))
                        {
                            if (!string.Equals(nr.FilePath, pr.FilePath, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(nr.Hash) && !string.IsNullOrEmpty(pr.Hash) && !string.Equals(nr.Hash, pr.Hash, StringComparison.OrdinalIgnoreCase)))
                            {
                                hasChanges = true;
                                break;
                            }
                        }
                    }

                    if (hasChanges || priorList.Count != newList.Count)
                    {
                        status = "changed";
                    }
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

        // Also identify volumes in the new set that do not exist in ANY prior set
        var newVolsOnly = newVols.Keys.Where(v => !priorRecords.Any(r => string.Equals(r.Volume, v, StringComparison.OrdinalIgnoreCase))).OrderBy(v => v).ToList();
        foreach (var vol in newVolsOnly)
        {
            var newList = newVols[vol];
            var newRange = newList.Count > 0
                ? $"{newList.Min(r => r.BatesNumber)} - {newList.Max(r => r.BatesNumber)}"
                : string.Empty;

            volumeAnalysis.Add(new VolumeResult
            {
                ProductionId = newRecords.FirstOrDefault()?.ProductionId ?? "NewSet",
                VolumeName = vol,
                PriorBatesRange = string.Empty,
                NewBatesRange = newRange,
                Status = "added"
            });
        }
    }
}
