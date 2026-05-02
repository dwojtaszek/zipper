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
}
