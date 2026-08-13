namespace Zipper.Cli;

public class ParsedArguments
{
    public string? FileType { get; set; }

    public string? FileTypes { get; set; }

    public string? InputCsv { get; set; }

    public string? DirectoryTemplate { get; set; }

    public string? SourcePathMode { get; set; }

    public long? Count { get; set; }

    public DirectoryInfo? OutputDirectory { get; set; }

    public string? OutputPathStr { get; set; }


    public int Folders { get; set; } = 1;

    public string? Encoding { get; set; } = "UTF-8";

    public bool IsEncodingExplicit { get; set; }

    public string? Distribution { get; set; } = "proportional";

    public bool WithText { get; set; }

    public string? TargetZipSize { get; set; }

    public bool IncludeLoadFile { get; set; }

    public bool ProductionSet { get; set; }

    public bool ProductionZip { get; set; }

    public int? VolumeSize { get; set; }

    public bool SupplementalProduction { get; set; }

    public string? PriorManifests { get; set; }

    public string? SupplementalGapPolicy { get; set; }

    public string? ProductionId { get; set; }

    public int RollingCount { get; set; } = 1;

    public string RollingBatesMode { get; set; } = "continuous";

    public string? CompareProductionManifests { get; set; }

    public string? ComparisonMode { get; set; }

    public string? ComparisonOutput { get; set; }

    public bool RedactedProduction { get; set; }

    public string? WithheldNativePolicy { get; set; }
}
