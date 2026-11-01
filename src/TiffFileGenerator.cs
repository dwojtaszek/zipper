namespace Zipper;

/// <summary>
/// Generates multipage TIFF file content with configurable page counts.
/// </summary>
internal sealed class TiffFileGenerator : IFileGenerator
{
    public string FileType => "tiff";

    public bool IsPlaceholderBased => false;

    private readonly bool hasPageRange;
    private readonly BatesSequence? batesSequence;

    public TiffFileGenerator(FileGenerationRequest request)
    {
        this.hasPageRange = request.Tiff.PageRange.HasValue;
        this.batesSequence = request.Tiff.IsRealistic && request.Bates is not null
            ? BatesSequence.FromConfig(request.Bates)
            : null;
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
        var batesNumber = !request.Tiff.IsRealistic ? null : this.ResolveEndorsementLabel(workItem);
        if (batesNumber is not null && pageCount == 1 && request.Bates?.PageSuffix == true)
        {
            // Multi-page renders label each page with _NNN; a one-page document needs its page labeled too (REQ-239).
            batesNumber = BatesSequence.FormatPageBates(batesNumber, 1);
        }

        return new GeneratedFileContent
        {
            Content = TiffMultiPageGenerator.Generate(pageCount, workItem, request.Tiff, batesNumber),
            PageCount = pageCount,
        };
    }

    /// <summary>
    /// Prefers the work item's Bates override, then the configured Bates sequence, then the Control Number.
    /// </summary>
    private string? ResolveEndorsementLabel(FileWorkItem workItem)
    {
        if (workItem.BatesNumberOverride is not null)
        {
            return workItem.BatesNumberOverride;
        }

        return this.batesSequence is not null
            ? this.batesSequence.Format(workItem.Index - 1).ToString()
            : workItem.ControlNumberOverride;
    }
}
