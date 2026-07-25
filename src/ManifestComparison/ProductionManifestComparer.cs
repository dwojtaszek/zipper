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
        var priorDuplicates = ComparisonEngine.FindDuplicates(priorRecords, "prior");

        var newRecords = await LoadRecordsAsync(newManifest.ResolvedPath, newManifest.Manifest).ConfigureAwait(false);
        foreach (var r in newRecords)
        {
            r.ProductionId = newManifest.Manifest.ProductionId;
        }
        var newDuplicates = ComparisonEngine.FindDuplicates(newRecords, "new");

        // Normalize comparison and run matching logic
        var result = ComparisonEngine.PerformComparison(priorRecords, newRecords, mode, priorDuplicates, newDuplicates, priorManifests.Select(pm => pm.ResolvedPath).ToList(), newManifest.ResolvedPath);

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
        BatesRangeAnalyzer.AnalyzeBatesRanges(priorRecords, newRecords, mode, result.BatesAnalysis, result.Details);

        // Perform Volume analysis
        VolumeAnalyzer.AnalyzeVolumes(priorRecords, newRecords, result.VolumeAnalysis);

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
        var resolvedEncoding = EncodingHelper.GetEncoding(encodingStr);
        if (resolvedEncoding is null)
        {
            Console.Error.WriteLine($"Warning: Encoding {JsonSerializer.Serialize(encodingStr)} not recognized, falling back to UTF-8.");
            encoding = System.Text.Encoding.UTF8;
        }
        else
        {
            encoding = resolvedEncoding;
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
