namespace Zipper.ManifestComparison;

/// <summary>
/// Pure comparison engine: matches new records against prior records by Bates
/// Number then Control Number, classifying each as added, removed, unchanged,
/// changed, replaced, or duplicated (supplemental overlap).
/// Extracted from <see cref="ProductionManifestComparer"/> (#601 PR3).
/// </summary>
internal static class ComparisonEngine
{
    public static List<DuplicateDetail> FindDuplicates(List<ComparisonRecord> records, string set)
    {
        var duplicates = new List<DuplicateDetail>();
        var batesSeen = new Dictionary<string, ComparisonRecord>(StringComparer.OrdinalIgnoreCase);
        var controlSeen = new Dictionary<string, ComparisonRecord>(StringComparer.OrdinalIgnoreCase);

        foreach (var r in records)
        {
            if (!string.IsNullOrEmpty(r.BatesNumber))
            {
                if (batesSeen.TryGetValue(r.BatesNumber, out var first))
                {
                    duplicates.Add(new DuplicateDetail
                    {
                        Set = set,
                        BatesNumber = r.BatesNumber,
                        ControlNumber = r.ControlNumber,
                        Message = $"Duplicate Bates Number '{r.BatesNumber}' on line {r.SourceLine} (first seen on line {first.SourceLine})"
                    });
                }
                else
                {
                    batesSeen[r.BatesNumber] = r;
                }
            }

            if (!string.IsNullOrEmpty(r.ControlNumber))
            {
                if (controlSeen.TryGetValue(r.ControlNumber, out var first))
                {
                    duplicates.Add(new DuplicateDetail
                    {
                        Set = set,
                        BatesNumber = r.BatesNumber,
                        ControlNumber = r.ControlNumber,
                        Message = $"Duplicate Control Number '{r.ControlNumber}' on line {r.SourceLine} (first seen on line {first.SourceLine})"
                    });
                }
                else
                {
                    controlSeen[r.ControlNumber] = r;
                }
            }
        }

        return duplicates;
    }

    public static ComparisonResult PerformComparison(
        List<ComparisonRecord> priorRecords,
        List<ComparisonRecord> newRecords,
        string mode,
        List<DuplicateDetail> priorDuplicates,
        List<DuplicateDetail> newDuplicates,
        List<string> priorPaths,
        string newPath)
    {
        var result = new ComparisonResult
        {
            ComparisonMode = mode,
            Manifests = priorPaths.Concat(new[] { newPath }).ToList()
        };

        result.Details.Duplicates.AddRange(priorDuplicates);
        result.Details.Duplicates.AddRange(newDuplicates);

        var priorByBates = priorRecords.Where(r => !string.IsNullOrEmpty(r.BatesNumber))
            .ToLookup(r => r.BatesNumber, StringComparer.OrdinalIgnoreCase);
        var priorByControl = priorRecords.Where(r => !string.IsNullOrEmpty(r.ControlNumber))
            .ToLookup(r => r.ControlNumber, StringComparer.OrdinalIgnoreCase);

        var matchedPriorBates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matchedPriorControls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var nr in newRecords)
        {
            bool isOverlap = false;
            if (string.Equals(mode, "supplemental", StringComparison.OrdinalIgnoreCase))
            {
                var priorBatesOverlap = priorByBates.Contains(nr.BatesNumber);
                var priorControlOverlap = priorByControl.Contains(nr.ControlNumber);

                if (priorBatesOverlap || priorControlOverlap)
                {
                    result.Details.Duplicates.Add(new DuplicateDetail
                    {
                        Set = "new",
                        BatesNumber = nr.BatesNumber,
                        ControlNumber = nr.ControlNumber,
                        Message = $"Supplemental overlap: Bates '{nr.BatesNumber}' or Control '{nr.ControlNumber}' matches a prior production set."
                    });
                    isOverlap = true;
                }
            }

            if (isOverlap)
            {
                result.Details.Added.Add(new RecordDetail
                {
                    BatesNumber = nr.BatesNumber,
                    ControlNumber = nr.ControlNumber,
                    FilePath = nr.FilePath,
                    Hash = nr.Hash,
                    Volume = nr.Volume
                });
                continue;
            }

            // 1. Try to match by Bates Number
            var priorBatesMatch = priorByBates[nr.BatesNumber].FirstOrDefault();
            if (priorBatesMatch is not null)
            {
                matchedPriorBates.Add(priorBatesMatch.BatesNumber);
                matchedPriorControls.Add(priorBatesMatch.ControlNumber);

                bool isHashMatch = true;
                if (!string.IsNullOrEmpty(nr.Hash) && !string.IsNullOrEmpty(priorBatesMatch.Hash))
                {
                    isHashMatch = string.Equals(nr.Hash, priorBatesMatch.Hash, StringComparison.OrdinalIgnoreCase);
                }

                bool isPathMatch = string.Equals(nr.FilePath, priorBatesMatch.FilePath, StringComparison.OrdinalIgnoreCase);
                bool isControlMatch = string.Equals(nr.ControlNumber, priorBatesMatch.ControlNumber, StringComparison.OrdinalIgnoreCase);

                if (isHashMatch && isPathMatch && isControlMatch)
                {
                    result.Details.Unchanged.Add(new RecordDetail
                    {
                        BatesNumber = nr.BatesNumber,
                        ControlNumber = nr.ControlNumber,
                        FilePath = nr.FilePath,
                        Hash = nr.Hash,
                        Volume = nr.Volume
                    });
                }
                else
                {
                    result.Details.Changed.Add(new ChangedDetail
                    {
                        BatesNumber = nr.BatesNumber,
                        ControlNumber = nr.ControlNumber,
                        PriorPath = priorBatesMatch.FilePath,
                        NewPath = nr.FilePath,
                        PriorHash = priorBatesMatch.Hash,
                        NewHash = nr.Hash
                    });
                }
                continue;
            }

            // 2. Try to match by Control Number
            var priorControlMatch = priorByControl[nr.ControlNumber].FirstOrDefault();
            if (priorControlMatch is not null)
            {
                matchedPriorBates.Add(priorControlMatch.BatesNumber);
                matchedPriorControls.Add(priorControlMatch.ControlNumber);

                result.Details.Replaced.Add(new ReplacedDetail
                {
                    PriorBatesNumber = priorControlMatch.BatesNumber,
                    NewBatesNumber = nr.BatesNumber,
                    ControlNumber = nr.ControlNumber
                });
                continue;
            }

            // 3. No match -> Added
            result.Details.Added.Add(new RecordDetail
            {
                BatesNumber = nr.BatesNumber,
                ControlNumber = nr.ControlNumber,
                FilePath = nr.FilePath,
                Hash = nr.Hash,
                Volume = nr.Volume
            });
        }

        // A supplemental manifest is additive and cannot establish removals.
        if (!string.Equals(mode, "supplemental", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var pr in priorRecords)
            {
                if (!matchedPriorBates.Contains(pr.BatesNumber) && !matchedPriorControls.Contains(pr.ControlNumber))
                {
                    result.Details.Removed.Add(new RecordDetail
                    {
                        BatesNumber = pr.BatesNumber,
                        ControlNumber = pr.ControlNumber,
                        FilePath = pr.FilePath,
                        Hash = pr.Hash,
                        Volume = pr.Volume
                    });
                }
            }
        }

        // Populate summary counts
        result.Summary.TotalPriorRecords = priorRecords.Count;
        result.Summary.TotalNewRecords = newRecords.Count;
        result.Summary.AddedCount = result.Details.Added.Count;
        result.Summary.RemovedCount = result.Details.Removed.Count;
        result.Summary.UnchangedCount = result.Details.Unchanged.Count;
        result.Summary.ChangedCount = result.Details.Changed.Count;
        result.Summary.ReplacedCount = result.Details.Replaced.Count;
        result.Summary.DuplicateCount = result.Details.Duplicates.Count;

        return result;
    }
}
