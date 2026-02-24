namespace Zipper;

/// <summary>
/// Generates TIFF files for legal document testing
/// Note: Currently generates single-page TIFF files with page count metadata.
/// </summary>
internal static class TiffMultiPageGenerator
{
    private const int MinPageCount = 1;
    private const int MaxPageCount = 1000;

    /// <summary>
    /// Returns a pre-computed TIFF file.
    /// The pageCount is tracked for metadata purposes only.
    /// </summary>
    /// <param name="pageCount">Number of pages (for metadata tracking).</param>
    /// <param name="workItem">File work item for context.</param>
    /// <returns>Byte array containing a TIFF file.</returns>
    public static byte[] Generate(int pageCount, FileWorkItem workItem)
    {
        // O(1): return pre-computed TIFF from PlaceholderFiles
        return PlaceholderFiles.GetContent("tiff");
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

        if (seed.HasValue)
        {
            // For deterministic testing, allocate a Random based on seed + index.
            // This is only paid when a seed is explicitly requested (e.g. E2E tests).
            var random = new Random(seed.Value + (int)fileIndex);
            return random.Next(clampedMin, clampedMax + 1);
        }

        // Use Random.Shared to avoid allocating a new System.Random instance per file.
        // This trades strict determinism for performance when no seed is provided.
        return Random.Shared.Next(clampedMin, clampedMax + 1);
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

        if (!int.TryParse(parts[0], out var min) || !int.TryParse(parts[1], out var max))
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
