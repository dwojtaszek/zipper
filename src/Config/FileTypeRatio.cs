namespace Zipper.Config;

/// <summary>
/// One entry of a File Type mix: a supported File Type and its relative allocation weight.
/// </summary>
public record FileTypeRatio
{
    /// <summary>Gets the File Type, normalized to lowercase (pdf, jpg, tiff, eml, docx, xlsx).</summary>
    public required string Type { get; init; }

    /// <summary>Gets the relative allocation weight (positive).</summary>
    public required long Weight { get; init; }
}
