namespace Zipper.LoadFiles;

/// <summary>
/// Derives the extracted-text path for a Native File path. The real trailing extension is
/// replaced (case-insensitive, last path segment only), which stays byte-identical for
/// synthetic names and handles source-driven paths such as <c>photo.jpeg</c>,
/// <c>scan.tif</c>, <c>REPORT.PDF</c>, or folder segments that contain a File Type
/// extension.
/// </summary>
internal static class TextPathHelper
{
    internal static string GetTextPath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var extension = Path.GetExtension(relativePath);
        return extension.Length > 0
            ? string.Concat(relativePath.AsSpan(0, relativePath.Length - extension.Length), ".txt")
            : relativePath + ".txt";
    }
}
