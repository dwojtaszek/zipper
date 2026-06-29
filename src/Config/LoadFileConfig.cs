namespace Zipper.Config;

public record LoadFileConfig
{
    public IReadOnlyList<LoadFileFormat> Formats { get; init; } = [LoadFileFormat.Dat];

    public string Encoding { get; init; } = "UTF-8";

    public bool IsEncodingExplicit { get; init; }

    public DistributionType Distribution { get; init; } = DistributionType.Proportional;

    public int AttachmentRate { get; init; }
}
