namespace Zipper;

/// <summary>
/// Immutable plan for a single Native File in a Production Set.
/// Separates path planning from file writing.
/// </summary>
internal sealed record ProductionNativeFilePlan
{
    public required long Index { get; init; }

    public required int VolumeIndex { get; init; }

    public required string VolumeName { get; init; }

    public required string BatesNumber { get; init; }

    /// <summary>The per-record File Type (lowercased) assigned by the File Type mix or request-level type.</summary>
    public required string FileType { get; init; }

    public required string NativeRelPath { get; init; }

    public required string TextRelPath { get; init; }

    public required string ImageRelPath { get; init; }

    public string? RedactedImageRelPath { get; init; }

    public string? RedactedTextRelPath { get; init; }
}

/// <summary>
/// Plans the Native File layout for a Production Set without performing I/O.
/// </summary>
internal static class ProductionSetPlanner
{
    public static IReadOnlyList<ProductionNativeFilePlan> Plan(FileGenerationRequest request, int rollingIndex = 0, long? overrideBatesStart = null)
    {
        var batesConfig = request.Bates
            ?? throw new InvalidOperationException("Production set requires Bates configuration.");

        if (request.Output.FileCount <= 0)
        {
            throw new InvalidOperationException("Production set requires FileCount > 0.");
        }

        if (request.Production.VolumeSize <= 0)
        {
            throw new InvalidOperationException("Production set requires VolumeSize > 0.");
        }

        var plans = new List<ProductionNativeFilePlan>((int)request.Output.FileCount);

        // Resolve bates prefix for this rolling set
        string prefix = batesConfig.Prefixes is not null && batesConfig.Prefixes.Count > rollingIndex
            ? batesConfig.Prefixes[rollingIndex]
            : batesConfig.Prefix;

        // Resolve bates start for this rolling set
        long start;
        if (overrideBatesStart.HasValue)
        {
            start = overrideBatesStart.Value;
        }
        else if (request.Production.RollingBatesMode == Config.RollingBatesMode.Restart)
        {
            start = batesConfig.Starts is not null && batesConfig.Starts.Count > rollingIndex
                ? batesConfig.Starts[rollingIndex]
                : batesConfig.Start;
        }
        else // continuous
        {
            if (batesConfig.Starts is not null && batesConfig.Starts.Count > rollingIndex)
            {
                start = batesConfig.Starts[rollingIndex];
            }
            else
            {
                // Calculate continuous start: configured start + index * FileCount * Increment
                start = batesConfig.Start + (rollingIndex * request.Output.FileCount * batesConfig.Increment);
            }
        }

        var setBatesConfig = new Config.BatesNumberConfig
        {
            Prefix = prefix,
            Start = start,
            Digits = batesConfig.Digits,
            Increment = batesConfig.Increment,
        };

        var batesSequence = BatesSequence.FromConfig(setBatesConfig);

        bool isRedacted = request.Production.RedactedProduction;
        var sourceRecords = request.SourceRecords;
        if (sourceRecords is not null && sourceRecords.Count != request.Output.FileCount)
        {
            throw new InvalidOperationException($"Source Record count ({sourceRecords.Count}) must equal FileCount ({request.Output.FileCount}).");
        }

        for (long i = 0; i < request.Output.FileCount; i++)
        {
            int volumeIndex = (int)(i / request.Production.VolumeSize) + 1;
            var volName = $"VOL{volumeIndex:D3}";
            var batesNumber = batesSequence.Next().ToString();
            var sourceRecord = sourceRecords?[(int)i];
            var nativeExt = sourceRecord?.FileType ?? request.Output.ResolveFileType(i + 1);

            plans.Add(new ProductionNativeFilePlan
            {
                Index = i,
                VolumeIndex = volumeIndex,
                VolumeName = volName,
                BatesNumber = batesNumber,
                FileType = nativeExt,
                NativeRelPath = BuildNativeRelPath(request.Production.SourcePathMode, volName, batesNumber, nativeExt, sourceRecord),
                TextRelPath = Path.Combine("TEXT", volName, $"{batesNumber}.txt"),
                ImageRelPath = Path.Combine("IMAGES", volName, $"{batesNumber}.tif"),
                RedactedImageRelPath = isRedacted ? Path.Combine("REDACTED", "IMAGES", volName, $"{batesNumber}.tif") : null,
                RedactedTextRelPath = isRedacted ? Path.Combine("REDACTED", "TEXT", volName, $"{batesNumber}.txt") : null,
            });
        }

        return plans;
    }

    private static string BuildNativeRelPath(Config.SourcePathMode mode, string volName, string batesNumber, string nativeExt, SourceInput.SourceRecord? sourceRecord)
    {
        if (sourceRecord is null || mode == Config.SourcePathMode.Bates)
        {
            return Path.Combine("NATIVES", volName, $"{batesNumber}.{nativeExt}");
        }

        // Source paths are '/'-normalized with no leading/trailing separator (SourcePathSanitizer).
        var parts = sourceRecord.RelativePath.Split('/');
        if (mode == Config.SourcePathMode.Originals)
        {
            return Path.Combine(["ORIGINALS", .. parts]);
        }

        // PreserveSubdirs: keep the source folder structure under the volume, Bates-name the file.
        return parts.Length > 1
            ? Path.Combine(["NATIVES", volName, .. parts[..^1], $"{batesNumber}.{nativeExt}"])
            : Path.Combine("NATIVES", volName, $"{batesNumber}.{nativeExt}");
    }
}
