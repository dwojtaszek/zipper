namespace Zipper.Profiles.Generation;

internal sealed record ColumnGenerationContext
{
    public required long NativeFileIndex { get; init; }

    public required int FolderNumber { get; init; }

    public required int DocumentIndex { get; init; }

    public required Random Seeded { get; init; }

    public required DateTime Now { get; init; }

    public FileData? FileData { get; init; }

    public ColumnDefinition? ProfileColumn { get; init; }

    /// <summary>
    /// Set by the DAT Standard composer's profile path to carry the row-level file context
    /// to the standard-row value generators; null for ordinary profile generation.
    /// </summary>
    public StandardRowResolution? StandardRow { get; init; }

    /// <summary>
    /// The Column Profile value for the current column, used as the fallback for columns whose
    /// file-context value is not applicable; null for ordinary profile generation.
    /// </summary>
    public string? ProfileValue { get; init; }
}
