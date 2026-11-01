namespace Zipper;

/// <summary>
/// Generates TIFF files for legal Native File testing: the precomputed placeholder by default, or
/// realistic bitonal page images with chained IFDs when TIFF Realism is enabled (REQ-238).
/// </summary>
internal static class TiffMultiPageGenerator
{
    private const int MinPageCount = 1;
    private const int MaxPageCount = 1000;

    /// <summary>
    /// Generates TIFF file content, either using pre-computed placeholder or realistic emission.
    /// The placeholder path tracks the page count for metadata only.
    /// </summary>
    public static byte[] Generate(int pageCount, FileWorkItem workItem, Config.TiffConfig? tiffConfig, string? batesNumber = null)
    {
        if (tiffConfig?.IsRealistic != true)
        {
            return PlaceholderFiles.GetContent("tiff");
        }

        var bates = batesNumber ?? workItem.BatesNumberOverride ?? workItem.ControlNumberOverride ?? $"DOC{workItem.Index:D8}";
        return TiffRenderer.RenderMultiPage(pageCount, bates, tiffConfig);
    }

    /// <summary>
    /// Generates a single page image TIFF with an endorsement band.
    /// </summary>
    public static byte[] GeneratePage(string? batesNumber, int pageIndex = 1, int pageCount = 1, Config.TiffConfig? tiffConfig = null)
    {
        if (tiffConfig?.IsRealistic != true)
        {
            return PlaceholderFiles.GetContent("tiff");
        }

        return TiffRenderer.RenderPage(batesNumber, pageIndex, pageCount, tiffConfig);
    }

    /// <summary>
    /// Determines the page count for a given file based on configured range.
    /// </summary>
    /// <param name="range">Optional min-max page range.</param>
    /// <param name="seed">Optional seed for deterministic generation.</param>
    /// <param name="fileIndex">The file index for deterministic randomization.</param>
    /// <returns>The number of pages for this file.</returns>
    public static int GetPageCount((int Min, int Max)? range, int? seed, long fileIndex)
    {
        if (!range.HasValue)
        {
            return MinPageCount;
        }

        var (min, max) = range.Value;

        if (min == max)
        {
            return min;
        }

        // Clamp the range to valid bounds
        var clampedMin = Math.Max(min, MinPageCount);
        var clampedMax = Math.Min(max, MaxPageCount);

        // If after clamping we have a single value, return it
        if (clampedMin >= clampedMax)
        {
            return clampedMin;
        }

        // Use a Random instance seeded by fileIndex (and optional seed)
        // to ensure deterministic page counts for testing, while avoiding
        // shared state contention.
#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        var random = new Random(unchecked((int)((seed ?? 0) + fileIndex)));
#pragma warning restore S2245
        return random.Next(clampedMin, clampedMax + 1);
    }

    /// <summary>
    /// Validates a TIFF page range specification.
    /// </summary>
    /// <param name="range">The range string (e.g., "1-20").</param>
    /// <returns>A tuple of (min, max) or null if invalid.</returns>
    public static (int Min, int Max)? ParsePageRange(string range)
    {
        var parts = range.Split('-');
        if (parts.Length != 2)
        {
            return null;
        }

        if (!int.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var min) || !int.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var max))
        {
            return null;
        }

        if (min < MinPageCount || max < min || max > MaxPageCount)
        {
            return null;
        }

        return (min, max);
    }
}
