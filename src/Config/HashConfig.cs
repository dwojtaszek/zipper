namespace Zipper.Config;

public enum HashMode
{
    None,
    Actual,
    Simulated,
}

public enum HashAlgorithm
{
    MD5,
    SHA1,
    SHA256,
}

public record HashConfig
{
    public HashMode Mode { get; init; } = HashMode.None;

    public IReadOnlySet<HashAlgorithm> Algorithms { get; init; } = new HashSet<HashAlgorithm>();

    public bool IsEnabled => this.Mode != HashMode.None && this.Algorithms.Count > 0;
}
