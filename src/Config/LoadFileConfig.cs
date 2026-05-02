namespace Zipper.Config;

public record LoadFileConfig
{
    public LoadFileFormat LoadFileFormat { get; init; } = LoadFileFormat.Dat;

    public List<LoadFileFormat>? LoadFileFormats { get; init; }

    public string Encoding { get; init; } = "UTF-8";

    public DistributionType Distribution { get; init; } = DistributionType.Proportional;

    public int AttachmentRate { get; init; }
}
