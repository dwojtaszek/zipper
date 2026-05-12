namespace Zipper;

/// <summary>
/// Factory for creating <see cref="IFileGenerator"/> instances keyed by file type string.
/// Eliminates string-based dispatch across the codebase.
/// </summary>
internal static class FileGeneratorFactory
{
    /// <summary>
    /// Creates an <see cref="IFileGenerator"/> for the specified file type.
    /// </summary>
    /// <param name="fileType">The file type: "pdf", "jpg", "tiff", "eml", "docx", "xlsx".</param>
    /// <param name="request">The generation request for context-dependent generators.</param>
    /// <returns>A file generator for the specified type, or null if the type is unknown.</returns>
    internal static IFileGenerator? Create(string fileType, FileGenerationRequest request)
    {
        var lower = fileType.ToLowerInvariant();

        return lower switch
        {
            "pdf" or "jpg" => new PlaceholderFileGenerator(lower),
            "eml" => new EmlFileGenerator(),
            "docx" or "xlsx" => new OfficeFileGenerator(lower),
            "tiff" => new TiffFileGenerator(request),
            _ => null,
        };
    }

    /// <summary>
    /// Determines if the specified file type is a known and supported format.
    /// </summary>
    internal static bool IsKnownType(string fileType)
    {
        var lower = fileType.ToLowerInvariant();
        return lower is "pdf" or "jpg" or "eml" or "docx" or "xlsx" or "tiff";
    }
}
