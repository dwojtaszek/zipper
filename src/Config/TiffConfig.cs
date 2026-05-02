namespace Zipper.Config;

public record TiffConfig
{
    public (int Min, int Max)? PageRange { get; init; }
}
