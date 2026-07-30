namespace Zipper.SourceInput;

/// <summary>
/// Maps file extensions (lowercased, with leading dot) to supported File Types for
/// Source-Driven Generation. Shared by the directory-template walker (inference) and the
/// CSV reader (extension/File Type consistency check).
/// </summary>
internal static class SourceFileTypeMap
{
    internal const string SupportedExtensionsDisplay = ".pdf, .jpg, .jpeg, .tif, .tiff, .eml, .docx, .xlsx";

    internal static bool TryFromExtension(string extensionLower, out string fileType)
    {
        var mapped = extensionLower switch
        {
            ".pdf" => "pdf",
            ".jpg" or ".jpeg" => "jpg",
            ".tif" or ".tiff" => "tiff",
            ".eml" => "eml",
            ".docx" => "docx",
            ".xlsx" => "xlsx",
            _ => (string?)null,
        };

        fileType = mapped ?? string.Empty;
        return mapped is not null;
    }
}
