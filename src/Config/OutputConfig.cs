namespace Zipper.Config;

public record OutputConfig
{
    public string OutputPath { get; init; } = string.Empty;

    public long FileCount { get; init; }

    public string FileType { get; init; } = "pdf";

    /// <summary>
    /// The declared File Type mix (from <c>--types</c>), or null for a single File Type.
    /// When set, <see cref="FileType"/> holds the first declared type.
    /// </summary>
    public IReadOnlyList<FileTypeRatio>? FileTypeRatios { get; init; }

    /// <summary>The exact per-index File Type assignment for a mixed run, or null for a single File Type.</summary>
    public FileTypePlan? FileTypePlan { get; init; }

    public int Folders { get; init; } = 1;

    public int Concurrency { get; init; } = PerformanceConstants.DefaultConcurrency;

    public bool WithText { get; init; }

    public long? TargetZipSize { get; init; }

    public bool IncludeLoadFile { get; init; }

    public string FileTypeLower => this.FileType.ToLowerInvariant();

    public bool IsEml => string.Equals(this.FileTypeLower, "eml", StringComparison.Ordinal);

    public bool IsTiff => string.Equals(this.FileTypeLower, "tiff", StringComparison.Ordinal);

    /// <summary>Gets whether more than one File Type participates in this run.</summary>
    public bool IsMixedFileTypes => this.FileTypePlan is not null;

    /// <summary>Returns whether the given File Type participates in this run (single type or mix).</summary>
    public bool HasFileType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return this.FileTypePlan?.ContainsType(type) ?? string.Equals(this.FileTypeLower, type, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the File Type assigned to the given 1-based file index.</summary>
    public string ResolveFileType(long index) => this.FileTypePlan?.GetFileType(index) ?? this.FileTypeLower;

    /// <summary>Display string for banners: the single type, or the declared mix (type:weight pairs).</summary>
    public string FileTypeDisplay => this.FileTypeRatios is { Count: > 0 }
        ? string.Join(",", this.FileTypeRatios.Select(r => $"{r.Type}:{r.Weight}"))
        : this.FileType;
}
