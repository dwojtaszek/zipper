using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zipper.Validation;

public class SupplementalValidationReport
{
    [JsonPropertyName("duplicateRanges")]
    public List<BatesRangeReport> DuplicateRanges { get; set; } = new();

    [JsonPropertyName("skippedRanges")]
    public List<BatesRangeReport> SkippedRanges { get; set; } = new();

    [JsonPropertyName("expectedNextBates")]
    public string? ExpectedNextBates { get; set; }

    [JsonPropertyName("actualStartingBates")]
    public string? ActualStartingBates { get; set; }
}

public class BatesRangeReport
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;
}

public static class SupplementalValidator
{
    private static readonly JsonSerializerOptions ParserOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<SupplementalValidationReport> ValidateAsync(
        FileGenerationRequest request,
        string actualStartBates,
        string actualEndBates)
    {
        ArgumentNullException.ThrowIfNull(request);

        var currentPrefix = request.Bates?.Prefix ?? string.Empty;
        var currentDigits = request.Bates?.Digits ?? 8;

        var priorManifestPaths = request.Production.PriorManifests;
        if (priorManifestPaths is null || priorManifestPaths.Count == 0)
        {
            throw new ValidationFailedException("Supplemental production requires at least one prior manifest path.");
        }

        var priorRanges = new List<(long Start, long End)>();

        foreach (var path in priorManifestPaths)
        {
            var resolvedPath = path;
            if (Directory.Exists(path))
            {
                resolvedPath = Path.Combine(path, "_manifest.json");
            }

            if (!File.Exists(resolvedPath))
            {
                throw new ValidationFailedException($"Prior Production Manifest file does not exist: {resolvedPath}");
            }

            PriorProductionManifest? manifest;
            try
            {
                var content = await File.ReadAllTextAsync(resolvedPath).ConfigureAwait(false);
                manifest = JsonSerializer.Deserialize<PriorProductionManifest>(content, ParserOptions);
            }
            catch (Exception ex)
            {
                throw new ValidationFailedException($"Prior Production Manifest is malformed: {resolvedPath}", ex);
            }

            if (manifest?.BatesRange is null ||
                string.IsNullOrEmpty(manifest.BatesRange.Start) ||
                string.IsNullOrEmpty(manifest.BatesRange.End) ||
                string.IsNullOrEmpty(manifest.BatesRange.Prefix) ||
                manifest.BatesRange.Digits is null)
            {
                throw new ValidationFailedException($"Prior Production Manifest is malformed (missing required batesRange fields): {resolvedPath}");
            }

            if (!string.Equals(manifest.BatesRange.Prefix, currentPrefix, StringComparison.Ordinal))
            {
                throw new ValidationFailedException($"Prior Production Manifest is incompatible with current Bates Prefix: {resolvedPath}. Expected prefix '{currentPrefix}', got '{manifest.BatesRange.Prefix}'.");
            }

            if (manifest.BatesRange.Digits != currentDigits)
            {
                throw new ValidationFailedException($"Prior Production Manifest is incompatible with current Bates Digits: {resolvedPath}. Expected digits {currentDigits}, got {manifest.BatesRange.Digits}.");
            }

            if (!TryParseBatesNumber(manifest.BatesRange.Start, currentPrefix, currentDigits, out long priorStart) ||
                !TryParseBatesNumber(manifest.BatesRange.End, currentPrefix, currentDigits, out long priorEnd))
            {
                throw new ValidationFailedException($"Prior Production Manifest contains unparseable start/end Bates numbers: {resolvedPath}. Start: '{manifest.BatesRange.Start}', End: '{manifest.BatesRange.End}'");
            }

            if (priorStart > priorEnd)
            {
                throw new ValidationFailedException($"Prior Production Manifest contains invalid Bates range where start '{manifest.BatesRange.Start}' is greater than end '{manifest.BatesRange.End}': {resolvedPath}");
            }

            priorRanges.Add((priorStart, priorEnd));
        }

        if (!TryParseBatesNumber(actualStartBates, currentPrefix, currentDigits, out long planStart) ||
            !TryParseBatesNumber(actualEndBates, currentPrefix, currentDigits, out long planEnd))
        {
            throw new ValidationFailedException($"Planned range contains unparseable start/end Bates numbers: '{actualStartBates}' - '{actualEndBates}'");
        }

        var report = new SupplementalValidationReport
        {
            ActualStartingBates = actualStartBates
        };

        // Determine duplicate/overlapping ranges
        foreach (var prior in priorRanges)
        {
            var overlapStart = Math.Max(prior.Start, planStart);
            var overlapEnd = Math.Min(prior.End, planEnd);

            if (overlapStart <= overlapEnd)
            {
                report.DuplicateRanges.Add(new BatesRangeReport
                {
                    Start = FormatBates(overlapStart, currentPrefix, currentDigits),
                    End = FormatBates(overlapEnd, currentPrefix, currentDigits)
                });
            }
        }

        // Determine max prior end
        long maxPriorEnd = 0;
        foreach (var prior in priorRanges)
        {
            if (prior.End > maxPriorEnd)
            {
                maxPriorEnd = prior.End;
            }
        }

        long expectedNextVal = maxPriorEnd + 1;
        report.ExpectedNextBates = FormatBates(expectedNextVal, currentPrefix, currentDigits);

        // Determine skipped ranges
        if (planStart > expectedNextVal)
        {
            report.SkippedRanges.Add(new BatesRangeReport
            {
                Start = FormatBates(expectedNextVal, currentPrefix, currentDigits),
                End = FormatBates(planStart - 1, currentPrefix, currentDigits)
            });
        }

        // Validate duplicates
        if (report.DuplicateRanges.Count > 0)
        {
            var dupStr = string.Join(", ", report.DuplicateRanges.Select(r => $"'{r.Start}-{r.End}'"));
            throw new ValidationFailedException($"Supplemental validation failed: Duplicate Bates Numbers detected in ranges: {dupStr}");
        }

        // Validate skipped ranges
        if (report.SkippedRanges.Count > 0 &&
            string.Equals(request.Production.SupplementalGapPolicy, "reject", StringComparison.OrdinalIgnoreCase))
        {
            var skipStr = string.Join(", ", report.SkippedRanges.Select(r => $"'{r.Start}-{r.End}'"));
            throw new ValidationFailedException($"Supplemental validation failed: Skipped Bates Numbers detected in ranges: {skipStr}");
        }

        return report;
    }

    private static string FormatBates(long value, string prefix, int digits)
    {
        return $"{prefix}{value.ToString($"D{digits}", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static bool TryParseBatesNumber(string batesNumber, string prefix, int digits, out long numericValue)
    {
        numericValue = 0;
        if (string.IsNullOrEmpty(batesNumber))
            return false;

        if (!batesNumber.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var numPart = batesNumber.Substring(prefix.Length);
        if (numPart.Length < digits)
            return false;

        foreach (var c in numPart)
        {
            if (c < '0' || c > '9')
                return false;
        }

        if (!long.TryParse(numPart, System.Globalization.CultureInfo.InvariantCulture, out numericValue))
            return false;

        return true;
    }

    internal class PriorProductionManifest
    {
        [JsonPropertyName("batesRange")]
        public PriorBatesRange? BatesRange { get; set; }
    }

    internal class PriorBatesRange
    {
        [JsonPropertyName("start")]
        public string? Start { get; set; }

        [JsonPropertyName("end")]
        public string? End { get; set; }

        [JsonPropertyName("prefix")]
        public string? Prefix { get; set; }

        [JsonPropertyName("digits")]
        public int? Digits { get; set; }
    }
}
