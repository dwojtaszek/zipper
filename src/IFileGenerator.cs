namespace Zipper;

/// <summary>
/// Result of file content generation, including content bytes and attachment metadata.
/// </summary>
internal record GeneratedFileContent
{
    /// <summary>
    /// The generated file content as a byte array.
    /// </summary>
    public byte[] Content { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// Optional attachment filename and content (EML only).
    /// </summary>
    public (string filename, byte[] content)? Attachment { get; init; }

    /// <summary>
    /// Email template used to generate EML content, propagated to FileData for load file metadata consistency.
    /// </summary>
    public EmailTemplate? EmailTemplate { get; init; }

    /// <summary>
    /// Page count (populated for TIFF with page range).
    /// </summary>
    public int PageCount { get; init; } = 1;
}

/// <summary>
/// Interface for file content generators. Each implementation handles a specific
/// file type, eliminating string-based dispatch across the codebase.
/// </summary>
internal interface IFileGenerator
{
    /// <summary>
    /// The file type this generator handles (lowercase, e.g. "pdf", "eml", "docx").
    /// </summary>
    string FileType { get; }

    /// <summary>
    /// Whether this generator produces placeholder-based (pre-computed static) content.
    /// Placeholder generators use zero-length byte arrays as valid content,
    /// while non-placeholder generators return content that varies per work item.
    /// </summary>
    bool IsPlaceholderBased { get; }

    /// <summary>
    /// Whether this generator requires sequential processing (non-parallel).
    /// EML generators with attachments/text require this for ZIP entry safety.
    /// </summary>
    bool RequiresSequentialProcessing(FileGenerationRequest request);

    /// <summary>
    /// Generates file content for the given work item and request configuration.
    /// </summary>
    GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request);
}
