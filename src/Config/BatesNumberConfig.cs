namespace Zipper.Config;

public record BatesNumberConfig
{
    public string Prefix { get; init; } = "DOC";
    public long Start { get; init; } = 1;
    public int Digits { get; init; } = 8;
    public long Increment { get; init; } = 1;
}
