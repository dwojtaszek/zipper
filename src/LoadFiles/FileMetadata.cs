using Zipper;

namespace Zipper.LoadFiles;

/// <summary>
/// Lightweight metadata container for load file generation, allowing early disposal of heavy file content.
/// </summary>
internal record FileMetadata
{
    public FileWorkItem WorkItem { get; init; } = new FileWorkItem();

    public long FileSize { get; init; }

    public string? AttachmentFilename { get; init; }

    public int PageCount { get; init; }
}
