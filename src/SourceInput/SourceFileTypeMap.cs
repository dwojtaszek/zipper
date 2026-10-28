namespace Zipper.SourceInput;

/// <summary>
/// Maps file extensions (lowercased, with leading dot) to supported File Types for
/// Source-Driven Generation. Shared by the directory-template walker (inference) and the
/// CSV reader (extension/File Type consistency check).
/// </summary>
internal static class SourceFileTypeMap
{
    private static readonly IReadOnlyDictionary<string, string> FileTypesByExtension =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".pdf"] = "pdf",
            [".jpg"] = "jpg",
            [".jpeg"] = "jpg",
            [".tif"] = "tiff",
            [".tiff"] = "tiff",
            [".eml"] = "eml",
            [".docx"] = "docx",
            [".xlsx"] = "xlsx",
        };

    internal static readonly string[] SupportedExtensions = FileTypesByExtension.Keys.ToArray();

    internal static readonly string SupportedExtensionsDisplay = string.Join(", ", SupportedExtensions);

    internal static bool TryFromExtension(string extensionLower, out string fileType)
    {
        if (FileTypesByExtension.TryGetValue(extensionLower, out var mapped))
        {
            fileType = mapped;
            return true;
        }

        fileType = string.Empty;
        return false;
    }
}
