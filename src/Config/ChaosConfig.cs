namespace Zipper.Config;

public record ChaosConfig
{
    public bool ChaosMode { get; init; }

    public string? ChaosAmount { get; init; }

    public string? ChaosTypes { get; init; }

    public string? ChaosScenario { get; init; }
}
