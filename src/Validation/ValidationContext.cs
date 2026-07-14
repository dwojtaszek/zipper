namespace Zipper.Validation;

public sealed record ValidationContext
{
    public string? ArchiveFilePath { get; init; }

    public string? ProductionSetPath { get; init; }

    public IReadOnlyDictionary<string, string> LoadFiles { get; init; } = new Dictionary<string, string>();

    public IReadOnlyList<string>? ArchiveEntryPaths { get; init; }

    public FileGenerationRequest Request { get; init; } = new();

    public bool SkipEolValidation { get; init; }

    public bool IsChaosMode { get; init; }
}
