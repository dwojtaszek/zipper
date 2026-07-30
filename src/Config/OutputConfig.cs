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

    /// <summary>
    /// The distinct File Types (lowercased, sorted) supplied by Source-Driven Generation rows
    /// (<c>--input-csv</c>/<c>--directory-template</c>), or null when no source input is used.
    /// </summary>
    public IReadOnlyList<string>? SourceFileTypes { get; init; }

    public int Folders { get; init; } = 1;

    public int Concurrency { get; init; } = PerformanceConstants.DefaultConcurrency;

    public bool WithText { get; init; }

    public long? TargetZipSize { get; init; }

    public bool IncludeLoadFile { get; init; }

    public string FileTypeLower => this.FileType.ToLowerInvariant();

    public bool IsEml => string.Equals(this.FileTypeLower, "eml", StringComparison.Ordinal);

    public bool IsTiff => string.Equals(this.FileTypeLower, "tiff", StringComparison.Ordinal);

    /// <summary>Gets whether more than one File Type participates in this run.</summary>
    public bool IsMixedFileTypes => this.FileTypePlan is not null || this.SourceFileTypes is { Count: > 1 };

    /// <summary>Returns whether the given File Type participates in this run (single type, mix, or source-driven).</summary>
    public bool HasFileType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (this.FileTypePlan is not null)
        {
            return this.FileTypePlan.ContainsType(type);
        }

        if (this.SourceFileTypes is not null)
        {
            return this.SourceFileTypes.Contains(type, StringComparer.OrdinalIgnoreCase);
        }

        return string.Equals(this.FileTypeLower, type, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Returns the File Type assigned to the given 1-based file index.</summary>
    public string ResolveFileType(long index) => this.FileTypePlan?.GetFileType(index) ?? this.FileTypeLower;

    /// <summary>Display string for banners: the single type, the declared mix (type:weight pairs), or the source-driven types.</summary>
    public string FileTypeDisplay => this.FileTypeRatios is { Count: > 0 }
        ? string.Join(",", this.FileTypeRatios.Select(r => $"{r.Type}:{r.Weight}"))
        : this.SourceFileTypes is { Count: > 1 }
            ? string.Join(",", this.SourceFileTypes)
            : this.FileType;
}
