namespace Zipper.LoadFiles;

using System;
using System.IO;

internal static class ImagePathHelper
{
    internal static string GetImagePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        var extension = Path.GetExtension(relativePath);
        return extension.Length > 0
            ? string.Concat(relativePath.AsSpan(0, relativePath.Length - extension.Length), ".tif")
            : relativePath + ".tif";
    }
}
