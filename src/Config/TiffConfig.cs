namespace Zipper.Config;

public record TiffConfig
{
    public (int Min, int Max)? PageRange { get; init; }

    /// <summary>
    /// Opt-in realism mode for TIFF emission: "none" (default), "standard", "group4", or "uncompressed".
    /// </summary>
    public string? RealismMode { get; init; }

    /// <summary>
    /// Compression scheme for realistic TIFF: "group4" (CCITT Group 4, default) or "uncompressed".
    /// </summary>
    public string Compression { get; init; } = "group4";

    /// <summary>
    /// True when realistic per-page TIFF emission is enabled.
    /// </summary>
    public bool IsRealistic => !string.IsNullOrEmpty(RealismMode) && !string.Equals(RealismMode, "none", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Alias for <see cref="IsRealistic"/> for configuration parity.
    /// </summary>
    public bool RealisticPageImages => IsRealistic;

    internal bool ShouldIncludePageCount(OutputConfig output) => output.HasFileType("tiff") && this.PageRange.HasValue;
}
