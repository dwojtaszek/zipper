namespace Zipper;

/// <summary>
/// Generates multipage TIFF file content with configurable page counts.
/// </summary>
internal sealed class TiffFileGenerator : IFileGenerator
{
    public string FileType => "tiff";

    public bool IsPlaceholderBased => false;

    private readonly bool hasPageRange;

    public TiffFileGenerator(FileGenerationRequest request)
    {
        this.hasPageRange = request.Tiff.PageRange.HasValue;
    }

    public GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request)
    {
        if (!this.hasPageRange && !request.Tiff.IsRealistic)
        {
            return new GeneratedFileContent
            {
                Content = PlaceholderFiles.GetContent("tiff"),
            };
        }

        var pageCount = this.hasPageRange
            ? TiffMultiPageGenerator.GetPageCount(
                request.Tiff.PageRange!.Value, request.Metadata.Seed, workItem.Index)
            : 1;

        // The endorsement label only feeds realistic emission, so default output never evaluates it.
        var batesNumber = !request.Tiff.IsRealistic ? null : ResolveEndorsementLabel(workItem, request);

        return new GeneratedFileContent
        {
            Content = TiffMultiPageGenerator.Generate(pageCount, workItem, request.Tiff, batesNumber),
            PageCount = pageCount,
        };
    }

    /// <summary>
    /// Prefers the work item's Bates override, then the configured Bates sequence, then the Control Number.
    /// </summary>
    private static string? ResolveEndorsementLabel(FileWorkItem workItem, FileGenerationRequest request)
    {
        if (workItem.BatesNumberOverride is not null)
        {
            return workItem.BatesNumberOverride;
        }

        return request.Bates is not null
            ? BatesSequence.FromConfig(request.Bates).Format(workItem.Index - 1).ToString()
            : workItem.ControlNumberOverride;
    }
}
