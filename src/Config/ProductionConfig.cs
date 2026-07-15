namespace Zipper.Config;

public enum RollingBatesMode
{
    Continuous,
    Restart,
}

public record ProductionConfig
{
    public bool ProductionSet { get; init; }

    public bool ProductionZip { get; init; }

    public int VolumeSize { get; init; } = 5000;
    public bool SupplementalProduction { get; init; }
    public IReadOnlyList<string> PriorManifests { get; init; } = Array.Empty<string>();
    public string SupplementalGapPolicy { get; init; } = "reject";

    public string? ProductionId { get; init; }

    public int RollingCount { get; init; } = 1;

    public RollingBatesMode RollingBatesMode { get; init; } = RollingBatesMode.Continuous;

    public bool RedactedProduction { get; init; }

    public string WithheldNativePolicy { get; init; } = "keep-native";
}


