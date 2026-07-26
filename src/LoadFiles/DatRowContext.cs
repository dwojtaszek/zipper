namespace Zipper.LoadFiles;

/// <summary>
/// Context passed through row-value builders to carry parent/child overrides
/// and family-attachment metadata across Standard and Production modes.
/// </summary>
internal sealed record DatRowContext
{
    public string? IdOverride { get; init; }

    public string? ControlOverride { get; init; }

    public string? FilePathOverride { get; init; }

    public string? FileSizeOverride { get; init; }

    public string? NativePathOverride { get; init; }

    public string? TextPathOverride { get; init; }

    public string? ImagePathOverride { get; init; }

    public bool IsChild { get; init; }

    public string BegAttach { get; init; } = string.Empty;

    public string EndAttach { get; init; } = string.Empty;

    public string ParentDocId { get; init; } = string.Empty;

    public string? RedactedImageRelPathOverride { get; init; }

    public string? RedactedTextRelPathOverride { get; init; }

    public string? NativeWithheldOverride { get; init; }

    public string? RedactionReasonOverride { get; init; }
}
