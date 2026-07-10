namespace Zipper.Config;

public record ProductionConfig
{
    public bool ProductionSet { get; init; }

    public bool ProductionZip { get; init; }

    public int VolumeSize { get; init; } = 5000;
    public bool SupplementalProduction { get; init; }
    public IReadOnlyList<string> PriorManifests { get; init; } = Array.Empty<string>();
    public string SupplementalGapPolicy { get; init; } = "reject";
}
