using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zipper.ManifestComparison;

public static class ProductionManifestComparer
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ManifestParserOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static async Task<bool> CompareAndReportAsync(
        string manifestPaths,
        string mode,
        string outputPath)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(outputPath);

        if (string.IsNullOrEmpty(manifestPaths))
        {
            throw new ArgumentException("Manifest paths are required.", nameof(manifestPaths));
        }

        var paths = manifestPaths.Split(',')
            .Select(p => p.Trim())
            .Where(p => !string.IsNullOrEmpty(p))
            .ToList();

        if (paths.Count < 2)
        {
            throw new ArgumentException("At least two Production Manifest paths must be provided for comparison.", nameof(manifestPaths));
        }

        // Normalize paths and load manifests
        var loadedManifests = new List<(string ResolvedPath, LoadedManifest Manifest)>();
        foreach (var path in paths)
        {
            var resolvedPath = path;
            if (Directory.Exists(path))
            {
                resolvedPath = Path.Combine(path, "_manifest.json");
            }

            if (!File.Exists(resolvedPath))
            {
                throw new FileNotFoundException($"Production Manifest file not found: {resolvedPath}", resolvedPath);
            }

            var json = await File.ReadAllTextAsync(resolvedPath).ConfigureAwait(false);
            var manifest = JsonSerializer.Deserialize<LoadedManifest>(json, ManifestParserOptions)
                ?? throw new InvalidDataException($"Failed to deserialize manifest at {resolvedPath}");

            loadedManifests.Add((resolvedPath, manifest));
        }

        // Last is New, others are Prior
        var priorManifests = loadedManifests.Take(loadedManifests.Count - 1).ToList();
        var newManifest = loadedManifests[^1];

        // Load records
        var priorRecords = new List<ComparisonRecord>();
        foreach (var pm in priorManifests)
        {
            var recs = await LoadRecordsAsync(pm.ResolvedPath, pm.Manifest).ConfigureAwait(false);
            foreach (var r in recs)
            {
                r.ProductionId = pm.Manifest.ProductionId;
            }
            priorRecords.AddRange(recs);
        }
        var priorDuplicates = FindDuplicates(priorRecords, "prior");

        var newRecords = await LoadRecordsAsync(newManifest.ResolvedPath, newManifest.Manifest).ConfigureAwait(false);
        foreach (var r in newRecords)
        {
            r.ProductionId = newManifest.Manifest.ProductionId;
        }
        var newDuplicates = FindDuplicates(newRecords, "new");

        // Normalize comparison and run matching logic
        var result = PerformComparison(priorRecords, newRecords, mode, priorDuplicates, newDuplicates, priorManifests.Select(pm => pm.ResolvedPath).ToList(), newManifest.ResolvedPath);

        // Populate ranges
        result.BatesAnalysis.PriorRange = priorRecords.Count > 0
            ? $"{priorRecords.Min(r => r.BatesNumber)} - {priorRecords.Max(r => r.BatesNumber)}"
            : string.Empty;
        result.BatesAnalysis.NewRange = newRecords.Count > 0
            ? $"{newRecords.Min(r => r.BatesNumber)} - {newRecords.Max(r => r.BatesNumber)}"
            : string.Empty;

        // Populate per-production set prior ranges
        foreach (var grp in priorRecords.GroupBy(r => r.ProductionId, StringComparer.OrdinalIgnoreCase))
        {
            if (grp.Any() && !string.IsNullOrEmpty(grp.Key))
            {
                result.BatesAnalysis.PriorRangesByProductionSet[grp.Key] = $"{grp.Min(r => r.BatesNumber)} - {grp.Max(r => r.BatesNumber)}";
            }
        }

        // Perform Bates range analysis and gap/overlap detection
        AnalyzeBatesRanges(priorRecords, newRecords, mode, result.BatesAnalysis, result.Details);

        // Perform Volume analysis
        AnalyzeVolumes(priorRecords, newRecords, result.VolumeAnalysis);

        // Write report
        var jsonReport = JsonSerializer.Serialize(result, SerializerOptions);
        await File.WriteAllTextAsync(outputPath, jsonReport).ConfigureAwait(false);

        // Write human-readable summary
        var summaryPath = Path.ChangeExtension(outputPath, ".summary.md");
        var summaryMarkdown = GenerateMarkdownSummary(result, mode);
        await File.WriteAllTextAsync(summaryPath, summaryMarkdown).ConfigureAwait(false);

        // Print human-readable summary to console
        Console.WriteLine(GenerateConsoleSummary(result, mode));

        return true;
    }

    private static async Task<List<ComparisonRecord>> LoadRecordsAsync(string manifestPath, LoadedManifest manifest)
    {
        var records = new List<ComparisonRecord>();
        var manifestDir = Path.GetDirectoryName(manifestPath) ?? string.Empty;

        var datRelPath = manifest.LoadFiles?.Dat ?? "DATA/loadfile.dat";

        // Prevent path traversal and rooted paths
        if (Path.IsPathRooted(datRelPath) || datRelPath.Contains("..", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid DAT load file path: {datRelPath}");
        }

        var datPath = Path.Combine(manifestDir, datRelPath);
        var fullDatPath = Path.GetFullPath(datPath);
        var fullManifestDir = Path.GetFullPath(manifestDir);

        if (!fullDatPath.StartsWith(fullManifestDir, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"DAT load file path '{datRelPath}' escapes the manifest directory '{manifestDir}'.");
        }

        if (!File.Exists(datPath))
        {
            throw new FileNotFoundException($"DAT load file not found: {datPath}", datPath);
        }

        var encodingStr = manifest.Settings?.Encoding ?? "UTF-8";
        System.Text.Encoding encoding;
        try
        {
            encoding = System.Text.Encoding.GetEncoding(encodingStr);
        }
        catch
        {
            encoding = System.Text.Encoding.UTF8;
        }

        var colDelim = ParseDelimiter(manifest.Settings?.ColumnDelimiter, '\x14');
        var quoteDelim = ParseDelimiter(manifest.Settings?.QuoteDelimiter, '\xfe');

        var lines = File.ReadLines(datPath, encoding);
        using var enumerator = lines.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return records;
        }

        var headerLine = enumerator.Current;
        var headers = ParseDatLine(headerLine, colDelim, quoteDelim);

        int batesIdx = headers.FindIndex(h => string.Equals(h, "BATES_NUMBER", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "BATES", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "BEGDOC", StringComparison.OrdinalIgnoreCase));
        if (batesIdx < 0)
        {
            throw new InvalidDataException("DAT load file is missing a required Bates number column (BATES_NUMBER, BATES, or BEGDOC).");
        }

        int docIdIdx = headers.FindIndex(h => string.Equals(h, "DOCID", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "CONTROL", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "CONTROL_NUMBER", StringComparison.OrdinalIgnoreCase));
        int pathIdx = headers.FindIndex(h => string.Equals(h, "NATIVE_PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "FILE_PATH", StringComparison.OrdinalIgnoreCase));
        int volumeIdx = headers.FindIndex(h => string.Equals(h, "VOLUME", StringComparison.OrdinalIgnoreCase));

        int md5Idx = headers.FindIndex(h => string.Equals(h, "MD5HASH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "MD5", StringComparison.OrdinalIgnoreCase));
        int sha1Idx = headers.FindIndex(h => string.Equals(h, "SHA1HASH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "SHA1", StringComparison.OrdinalIgnoreCase));
        int sha256Idx = headers.FindIndex(h => string.Equals(h, "SHA256HASH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "SHA256", StringComparison.OrdinalIgnoreCase));

        int lineNum = 1;
        while (enumerator.MoveNext())
        {
            lineNum++;
            var line = enumerator.Current;
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            var fields = ParseDatLine(line, colDelim, quoteDelim);
            if (fields.Count == 0)
            {
                continue;
            }

            var bates = batesIdx >= 0 && batesIdx < fields.Count ? fields[batesIdx].Trim() : string.Empty;
            var docId = docIdIdx >= 0 && docIdIdx < fields.Count ? fields[docIdIdx].Trim() : string.Empty;
            var filePath = pathIdx >= 0 && pathIdx < fields.Count ? fields[pathIdx].Trim() : string.Empty;
            var volume = volumeIdx >= 0 && volumeIdx < fields.Count ? fields[volumeIdx].Trim() : string.Empty;

            var hash = string.Empty;
            if (md5Idx >= 0 && md5Idx < fields.Count && !string.IsNullOrEmpty(fields[md5Idx]))
                hash = fields[md5Idx].Trim();
            else if (sha256Idx >= 0 && sha256Idx < fields.Count && !string.IsNullOrEmpty(fields[sha256Idx]))
                hash = fields[sha256Idx].Trim();
            else if (sha1Idx >= 0 && sha1Idx < fields.Count && !string.IsNullOrEmpty(fields[sha1Idx]))
                hash = fields[sha1Idx].Trim();

            if (string.IsNullOrEmpty(volume) && !string.IsNullOrEmpty(filePath))
            {
                // Infer volume from file path segment
                var normalizedPath = filePath.Replace('\\', '/');
                var parts = normalizedPath.Split('/');
                var volPart = parts.FirstOrDefault(p => p.StartsWith("VOL", StringComparison.OrdinalIgnoreCase) || p.Contains("volume", StringComparison.OrdinalIgnoreCase));
                volume = volPart ?? (parts.Length > 1 ? parts[parts.Length - 2] : "VOL001");
            }

            records.Add(new ComparisonRecord
            {
                BatesNumber = bates,
                ControlNumber = string.IsNullOrEmpty(docId) ? bates : docId, // fallback to bates number as control key
                FilePath = filePath,
                Hash = hash,
                Volume = volume,
                ManifestPath = manifestPath,
                SourceLine = lineNum
            });
        }

        return records;
    }

    private static char ParseDelimiter(string? formatted, char fallback)
    {
        if (string.IsNullOrEmpty(formatted)) return fallback;
        if (string.Equals(formatted, "none", StringComparison.OrdinalIgnoreCase)) return '\x00';
        if (formatted.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase))
        {
            if (int.TryParse(formatted.Substring(6), System.Globalization.CultureInfo.InvariantCulture, out int asciiVal))
            {
                return (char)asciiVal;
            }
        }
        else if (formatted.StartsWith("char:", StringComparison.OrdinalIgnoreCase))
        {
            var chars = formatted.Substring(5);
            if (chars.Length > 0) return chars[0];
        }
        return fallback;
    }

    private static List<string> ParseDatLine(string line, char colDelim, char quoteDelim)
    {
        var fields = new List<string>();
        var currentField = new StringBuilder();
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (quoteDelim != '\x00' && c == quoteDelim)
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == quoteDelim)
                {
                    currentField.Append(quoteDelim);
                    i++; // Skip the second quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == colDelim && !inQuotes)
            {
                fields.Add(currentField.ToString());
                currentField.Clear();
            }
            else
            {
                currentField.Append(c);
            }
        }
        fields.Add(currentField.ToString());
        return fields;
    }

    private static List<DuplicateDetail> FindDuplicates(List<ComparisonRecord> records, string set)
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

    private static ComparisonResult PerformComparison(
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

    private static void AnalyzeBatesRanges(
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

    private static void ResultSummaryBatesAnalysis(BatesAnalysis batesAnalysis)
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

    private static List<BatesRangeReport> FindGapsInSequence(List<string> batesList)
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

    private static Dictionary<string, List<(long Value, int Digits)>> GroupBatesByPrefix(List<string> batesList)
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

    private static List<BatesRangeReport> MergeConsecutiveBates(List<string> batesList)
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

    private static bool TryParseBates(string bates, out string prefix, out long num, out int digits)
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

    private static string FormatBates(long value, string prefix, int digits)
    {
        return $"{prefix}{value.ToString($"D{digits}", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static void AnalyzeVolumes(
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

    private static string GenerateMarkdownSummary(ComparisonResult result, string mode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Production Set Comparison Report");
        sb.AppendLine();
        sb.Append("**Mode:** ").Append(mode.ToUpperInvariant()).AppendLine();
        sb.Append("**Date Generated:** ").Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)).Append(" UTC").AppendLine();
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Metric | Value |");
        sb.AppendLine("|--------|-------|");
        sb.Append("| Total Prior Records | ").Append(result.Summary.TotalPriorRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Total New Records | ").Append(result.Summary.TotalNewRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Added Records | ").Append(result.Summary.AddedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Removed Records | ").Append(result.Summary.RemovedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Unchanged Records | ").Append(result.Summary.UnchangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Changed Records (Metadata/Hash) | ").Append(result.Summary.ChangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Replaced Records (Different Bates) | ").Append(result.Summary.ReplacedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.Append("| Duplicates/Overlaps | ").Append(result.Summary.DuplicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(" |").AppendLine();
        sb.AppendLine();
        sb.AppendLine("## Bates Number Analysis");
        sb.AppendLine();
        sb.Append("* **Prior Bates Range:** ").Append(result.BatesAnalysis.PriorRange).AppendLine();
        sb.Append("* **New Bates Range:** ").Append(result.BatesAnalysis.NewRange).AppendLine();
        sb.AppendLine();

        if (result.BatesAnalysis.PriorRangesByProductionSet.Count > 0)
        {
            sb.AppendLine("### Prior Bates Ranges by Production Set");
            sb.AppendLine();
            foreach (var kvp in result.BatesAnalysis.PriorRangesByProductionSet.OrderBy(k => k.Key))
            {
                sb.Append("* **").Append(kvp.Key).Append(":** ").Append(kvp.Value).AppendLine();
            }
            sb.AppendLine();
        }

        if (result.BatesAnalysis.Gaps.Count > 0)
        {
            sb.AppendLine("### Skipped Bates Ranges (Gaps)");
            sb.AppendLine();
            foreach (var gap in result.BatesAnalysis.Gaps)
            {
                sb.Append("- `").Append(gap.Start).Append("` to `").Append(gap.End).Append('`').AppendLine();
            }
            sb.AppendLine();
        }

        if (result.BatesAnalysis.Overlaps.Count > 0)
        {
            sb.AppendLine("### Overlapping Bates Ranges");
            sb.AppendLine();
            foreach (var overlap in result.BatesAnalysis.Overlaps)
            {
                sb.Append("- `").Append(overlap.Start).Append("` to `").Append(overlap.End).Append('`').AppendLine();
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Volume Analysis");
        sb.AppendLine();
        sb.AppendLine("| Production Set | Volume | Prior Bates Range | New Bates Range | Status |");
        sb.AppendLine("|----------------|--------|-------------------|-----------------|--------|");
        foreach (var vol in result.VolumeAnalysis)
        {
            sb.Append("| ").Append(vol.ProductionId).Append(" | ").Append(vol.VolumeName).Append(" | ").Append(vol.PriorBatesRange).Append(" | ").Append(vol.NewBatesRange).Append(" | ").Append(vol.Status).Append(" |").AppendLine();
        }
        sb.AppendLine();

        return sb.ToString();
    }

    private static string GenerateConsoleSummary(ComparisonResult result, string mode)
    {
        var sb = new StringBuilder();
        sb.AppendLine("======================================================================");
        sb.AppendLine("                   PRODUCTION MANIFEST COMPARISON SUMMARY             ");
        sb.AppendLine("======================================================================");
        sb.Append("Mode: ").Append(mode.ToUpperInvariant()).AppendLine();
        sb.AppendLine();
        sb.Append("Total Prior Records: ").Append(result.Summary.TotalPriorRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Total New Records:   ").Append(result.Summary.TotalNewRecords.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Added Records:       ").Append(result.Summary.AddedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Removed Records:     ").Append(result.Summary.RemovedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Unchanged Records:   ").Append(result.Summary.UnchangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Changed Records:     ").Append(result.Summary.ChangedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Replaced Records:    ").Append(result.Summary.ReplacedCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.Append("Duplicate Warnings:  ").Append(result.Summary.DuplicateCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine();
        sb.AppendLine();
        sb.Append("Prior Bates Range:   ").Append(result.BatesAnalysis.PriorRange).AppendLine();
        sb.Append("New Bates Range:     ").Append(result.BatesAnalysis.NewRange).AppendLine();
        if (result.BatesAnalysis.Gaps.Count > 0)
        {
            sb.Append("Gaps Detected:       ").Append(result.BatesAnalysis.Gaps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" range(s) skipped.");
        }
        sb.AppendLine("======================================================================");
        return sb.ToString();
    }
}

public class LoadedManifest
{
    public string ProductionId { get; set; } = string.Empty;
    public string BatesNumberStart { get; set; } = string.Empty;
    public string BatesNumberEnd { get; set; } = string.Empty;
    public LoadedBatesRange? BatesRange { get; set; }
    public LoadedLoadFiles? LoadFiles { get; set; }
    public LoadedSettings? Settings { get; set; }
}

public class LoadedBatesRange
{
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public int Digits { get; set; }
}

public class LoadedLoadFiles
{
    public string Dat { get; set; } = string.Empty;
    public string Opt { get; set; } = string.Empty;
}

public class LoadedSettings
{
    public string Encoding { get; set; } = string.Empty;
    public string ColumnDelimiter { get; set; } = string.Empty;
    public string QuoteDelimiter { get; set; } = string.Empty;
}

public class ComparisonRecord
{
    public string BatesNumber { get; set; } = string.Empty;
    public string ControlNumber { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public string Volume { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public string ProductionId { get; set; } = string.Empty;
    public int SourceLine { get; set; }
}

public class ComparisonResult
{
    [JsonPropertyName("comparisonMode")]
    public string ComparisonMode { get; set; } = string.Empty;

    [JsonPropertyName("manifests")]
    public List<string> Manifests { get; set; } = new();

    [JsonPropertyName("summary")]
    public SummaryResult Summary { get; set; } = new();

    [JsonPropertyName("batesAnalysis")]
    public BatesAnalysis BatesAnalysis { get; set; } = new();

    [JsonPropertyName("volumeAnalysis")]
    public List<VolumeResult> VolumeAnalysis { get; set; } = new();

    [JsonPropertyName("details")]
    public ResultDetails Details { get; set; } = new();
}

public class SummaryResult
{
    [JsonPropertyName("totalPriorRecords")]
    public int TotalPriorRecords { get; set; }

    [JsonPropertyName("totalNewRecords")]
    public int TotalNewRecords { get; set; }

    [JsonPropertyName("addedCount")]
    public int AddedCount { get; set; }

    [JsonPropertyName("removedCount")]
    public int RemovedCount { get; set; }

    [JsonPropertyName("unchangedCount")]
    public int UnchangedCount { get; set; }

    [JsonPropertyName("changedCount")]
    public int ChangedCount { get; set; }

    [JsonPropertyName("replacedCount")]
    public int ReplacedCount { get; set; }

    [JsonPropertyName("duplicateCount")]
    public int DuplicateCount { get; set; }
}

public class BatesAnalysis
{
    [JsonPropertyName("priorRange")]
    public string PriorRange { get; set; } = string.Empty;

    [JsonPropertyName("newRange")]
    public string NewRange { get; set; } = string.Empty;

    [JsonPropertyName("gaps")]
    public List<BatesRangeReport> Gaps { get; set; } = new();

    [JsonPropertyName("overlaps")]
    public List<BatesRangeReport> Overlaps { get; set; } = new();

    [JsonPropertyName("totalSkippedBates")]
    public int TotalSkippedBates { get; set; }

    [JsonPropertyName("priorRangesByProductionSet")]
    public Dictionary<string, string> PriorRangesByProductionSet { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class BatesRangeReport
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;
}

public class VolumeResult
{
    [JsonPropertyName("productionId")]
    public string ProductionId { get; set; } = string.Empty;

    [JsonPropertyName("volumeName")]
    public string VolumeName { get; set; } = string.Empty;

    [JsonPropertyName("priorBatesRange")]
    public string PriorBatesRange { get; set; } = string.Empty;

    [JsonPropertyName("newBatesRange")]
    public string NewBatesRange { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty; // unchanged, added, removed, changed
}

public class ResultDetails
{
    [JsonPropertyName("added")]
    public List<RecordDetail> Added { get; set; } = new();

    [JsonPropertyName("removed")]
    public List<RecordDetail> Removed { get; set; } = new();

    [JsonPropertyName("unchanged")]
    public List<RecordDetail> Unchanged { get; set; } = new();

    [JsonPropertyName("changed")]
    public List<ChangedDetail> Changed { get; set; } = new();

    [JsonPropertyName("replaced")]
    public List<ReplacedDetail> Replaced { get; set; } = new();

    [JsonPropertyName("duplicates")]
    public List<DuplicateDetail> Duplicates { get; set; } = new();

    [JsonPropertyName("skipped")]
    public List<BatesRangeReport> Skipped { get; set; } = new();
}

public class RecordDetail
{
    [JsonPropertyName("batesNumber")]
    public string BatesNumber { get; set; } = string.Empty;

    [JsonPropertyName("controlNumber")]
    public string ControlNumber { get; set; } = string.Empty;

    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("hash")]
    public string Hash { get; set; } = string.Empty;

    [JsonPropertyName("volume")]
    public string Volume { get; set; } = string.Empty;
}

public class ChangedDetail
{
    [JsonPropertyName("batesNumber")]
    public string BatesNumber { get; set; } = string.Empty;

    [JsonPropertyName("controlNumber")]
    public string ControlNumber { get; set; } = string.Empty;

    [JsonPropertyName("priorPath")]
    public string PriorPath { get; set; } = string.Empty;

    [JsonPropertyName("newPath")]
    public string NewPath { get; set; } = string.Empty;

    [JsonPropertyName("priorHash")]
    public string PriorHash { get; set; } = string.Empty;

    [JsonPropertyName("newHash")]
    public string NewHash { get; set; } = string.Empty;
}

public class ReplacedDetail
{
    [JsonPropertyName("priorBatesNumber")]
    public string PriorBatesNumber { get; set; } = string.Empty;

    [JsonPropertyName("newBatesNumber")]
    public string NewBatesNumber { get; set; } = string.Empty;

    [JsonPropertyName("controlNumber")]
    public string ControlNumber { get; set; } = string.Empty;
}

public class DuplicateDetail
{
    [JsonPropertyName("set")]
    public string Set { get; set; } = string.Empty; // prior, new

    [JsonPropertyName("batesNumber")]
    public string BatesNumber { get; set; } = string.Empty;

    [JsonPropertyName("controlNumber")]
    public string ControlNumber { get; set; } = string.Empty;

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}
