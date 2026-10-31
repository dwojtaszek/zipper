using Zipper.Profiles;

namespace Zipper.Config;

public record MetadataConfig
{
    public bool WithMetadata { get; init; }

    public ColumnProfile? ColumnProfile { get; init; }

    public int? Seed { get; init; }

    public string? DateFormatOverride { get; init; }

    public int? EmptyPercentageOverride { get; init; }

    public int? CustodianCountOverride { get; init; }

    public bool WithFamilies { get; init; }

    public bool WithCollectionMetadata { get; init; }

    public bool WithThreading { get; init; }

    public string? MimeRealism { get; init; }

    public Emails.MimeRealismOptions GetMimeRealismOptions() => Emails.MimeRealismOptions.Parse(this.MimeRealism);

    internal bool ShouldIncludeEmlColumns(OutputConfig output) => output.HasFileType("eml");

    internal bool ShouldIncludeMetadataColumns(OutputConfig output) => this.WithMetadata || output.HasFileType("eml");

    internal bool ShouldIncludeCompressionColumn(OutputConfig output) => this.ShouldIncludeMetadataColumns(output);

    internal bool ShouldIncludeCollectionMetadataColumns() => this.WithCollectionMetadata;
}
