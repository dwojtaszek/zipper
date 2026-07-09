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

    public required string NativeRelPath { get; init; }

    public required string TextRelPath { get; init; }

    public required string ImageRelPath { get; init; }
}

/// <summary>
/// Plans the Native File layout for a Production Set without performing I/O.
/// </summary>
internal static class ProductionSetPlanner
{
    public static IReadOnlyList<ProductionNativeFilePlan> Plan(FileGenerationRequest request, int rollingIndex = 0)
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
        var nativeExt = request.Output.FileTypeLower;

        // Resolve bates prefix for this rolling set
        string prefix = batesConfig.Prefixes is not null && batesConfig.Prefixes.Count > rollingIndex
            ? batesConfig.Prefixes[rollingIndex]
            : batesConfig.Prefix;

        // Resolve bates start for this rolling set
        long start;
        if (request.Production.RollingBatesMode == Config.RollingBatesMode.Restart)
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

        for (long i = 0; i < request.Output.FileCount; i++)
        {
            int volumeIndex = (int)(i / request.Production.VolumeSize) + 1;
            var volName = $"VOL{volumeIndex:D3}";
            var batesNumber = batesSequence.Next().ToString();

            plans.Add(new ProductionNativeFilePlan
            {
                Index = i,
                VolumeIndex = volumeIndex,
                VolumeName = volName,
                BatesNumber = batesNumber,
                NativeRelPath = Path.Combine("NATIVES", volName, $"{batesNumber}.{nativeExt}"),
                TextRelPath = Path.Combine("TEXT", volName, $"{batesNumber}.txt"),
                ImageRelPath = Path.Combine("IMAGES", volName, $"{batesNumber}.tif"),
            });
        }

        return plans;
    }
}
