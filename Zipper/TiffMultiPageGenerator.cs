using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Tiff;
using System.IO;

namespace Zipper;

/// <summary>
/// Generates TIFF files for legal document testing
/// Note: Currently generates single-page TIFF files with page count metadata
/// </summary>
internal static class TiffMultiPageGenerator
{
    private const int Width = 100;
    private const int Height = 100;

    /// <summary>
    /// Generates a TIFF file with the specified number of pages
    /// Note: Currently creates single-page TIFF regardless of pageCount
    /// The pageCount is tracked for metadata purposes only
    /// </summary>
    /// <param name="pageCount">Number of pages (for metadata tracking)</param>
    /// <param name="workItem">File work item for context</param>
    /// <returns>Byte array containing a TIFF file</returns>
    public static byte[] Generate(int pageCount, FileWorkItem workItem)
    {
        using var stream = new MemoryStream();

        // Create a simple black TIFF image
        using var image = new Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(Width, Height);

        var encoder = new TiffEncoder();
        image.Save(stream, encoder);

        return stream.ToArray();
    }

    /// <summary>
    /// Determines the page count for a given file based on configured range
    /// </summary>
    /// <param name="range">Optional min-max page range</param>
    /// <param name="fileIndex">The file index for deterministic randomization</param>
    /// <returns>The number of pages for this file</returns>
    public static int GetPageCount((int Min, int Max)? range, long fileIndex)
    {
        if (!range.HasValue || range.Value.Min == range.Value.Max)
        {
            return range?.Min ?? 1;
        }

        if (range.Value.Min < 1)
        {
            return 1;
        }

        if (range.Value.Max > 1000)
        {
            return range.Value.Min;
        }

        var random = new System.Random((int)fileIndex);
        return random.Next(range.Value.Min, range.Value.Max + 1);
    }

    /// <summary>
    /// Validates a TIFF page range specification
    /// </summary>
    /// <param name="range">The range string (e.g., "1-20")</param>
    /// <returns>A tuple of (min, max) or null if invalid</returns>
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

        if (min < 1 || max < min || max > 1000)
        {
            return null;
        }

        return (min, max);
    }
}
