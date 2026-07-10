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

    public string? ProductionId { get; init; }

    public int RollingCount { get; init; } = 1;

    public RollingBatesMode RollingBatesMode { get; init; } = RollingBatesMode.Continuous;
}


