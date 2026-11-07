namespace Zipper.Config;

public record TiffConfig
{
    public (int Min, int Max)? PageRange { get; init; }

    /// <summary>
    /// Opt-in realism mode for TIFF emission: null (default, same as "none"), "none", "standard", "group4", or "uncompressed".
    /// Selects whether realistic emission is on; the renderer reads <see cref="Compression"/> for strip encoding.
    /// </summary>
    public string? RealismMode { get; init; }

    /// <summary>
    /// Strip compression for realistic TIFF: "group4" (CCITT Group 4, default) or "uncompressed".
    /// This is the only compression source the renderer reads.
    /// </summary>
    public string Compression { get; init; } = "group4";

    /// <summary>
    /// Byte order of realistic TIFF emission: "ii" (little-endian, default) or "mm" (big-endian).
    /// Only the renderer reads it; the default keeps TIFF output byte-identical to earlier releases.
    /// </summary>
    public string ByteOrder { get; init; } = "ii";

    /// <summary>
    /// True when realistic per-page TIFF emission is enabled.
    /// </summary>
    public bool IsRealistic => !string.IsNullOrEmpty(RealismMode) && !string.Equals(RealismMode, "none", StringComparison.OrdinalIgnoreCase);

    internal bool ShouldIncludePageCount(OutputConfig output) => output.HasFileType("tiff") && this.PageRange.HasValue;
}
