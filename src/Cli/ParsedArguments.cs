namespace Zipper.Cli;

public class ParsedArguments
{
    public string? FileType { get; set; }

    public long? Count { get; set; }

    public DirectoryInfo? OutputDirectory { get; set; }

    public int Folders { get; set; } = 1;

    public string? Encoding { get; set; } = "UTF-8";

    public string? Distribution { get; set; } = "proportional";

    public bool WithMetadata { get; set; }

    public bool WithText { get; set; }

    public int AttachmentRate { get; set; }

    public string? TargetZipSize { get; set; }

    public bool IncludeLoadFile { get; set; }

    public string? LoadFileFormat { get; set; } = "dat";

    public string? LoadFileFormats { get; set; }

    public string? DatDelimiters { get; set; }

    public string? DelimiterColumn { get; set; }

    public string? DelimiterQuote { get; set; }

    public string? DelimiterNewline { get; set; }

    public string? BatesPrefix { get; set; }

    public long? BatesStart { get; set; }

    public int? BatesDigits { get; set; }

    public string? TiffPagesRange { get; set; }

    public string? ColumnProfile { get; set; }

    public int? Seed { get; set; }

    public string? DateFormat { get; set; }

    public int? EmptyPercentage { get; set; }

    public int? CustodianCount { get; set; }

    public bool WithFamilies { get; set; }

    public bool LoadfileOnly { get; set; }

    public string? Eol { get; set; }

    public string? ColDelim { get; set; }

    public string? QuoteDelim { get; set; }

    public string? NewlineDelim { get; set; }

    public string? MultiDelim { get; set; }

    public string? NestedDelim { get; set; }

    public bool ChaosMode { get; set; }

    public string? ChaosAmount { get; set; }

    public string? ChaosTypes { get; set; }

    public string? ChaosScenario { get; set; }

    public bool ChaosList { get; set; }

    public bool ProductionSet { get; set; }

    public bool ProductionZip { get; set; }

    public int? VolumeSize { get; set; }
}
