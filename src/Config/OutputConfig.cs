namespace Zipper.Config;

public record OutputConfig
{
    public string OutputPath { get; init; } = string.Empty;

    public long FileCount { get; init; }

    public string FileType { get; init; } = "pdf";

    public int Folders { get; init; } = 1;

    public int Concurrency { get; init; } = PerformanceConstants.DefaultConcurrency;

    public bool WithText { get; init; }

    public long? TargetZipSize { get; init; }

    public bool IncludeLoadFile { get; init; }
}
