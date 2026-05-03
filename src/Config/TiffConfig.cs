namespace Zipper.Config;

public record TiffConfig
{
    public (int Min, int Max)? PageRange { get; init; }

    internal bool ShouldIncludePageCount(OutputConfig output) => output.IsTiff && this.PageRange.HasValue;
}
