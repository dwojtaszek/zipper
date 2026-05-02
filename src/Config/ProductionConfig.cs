namespace Zipper.Config;

public record ProductionConfig
{
    public bool ProductionSet { get; init; }

    public bool ProductionZip { get; init; }

    public int VolumeSize { get; init; } = 5000;
}
