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
}
