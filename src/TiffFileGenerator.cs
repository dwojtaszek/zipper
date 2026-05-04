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

    public bool RequiresSequentialProcessing(FileGenerationRequest request) => false;

    public GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request)
    {
        if (!this.hasPageRange)
        {
            return new GeneratedFileContent
            {
                Content = PlaceholderFiles.GetContent("tiff"),
            };
        }

        var pageCount = TiffMultiPageGenerator.GetPageCount(
            request.Tiff.PageRange!.Value, request.Metadata.Seed, workItem.Index);

        return new GeneratedFileContent
        {
            Content = TiffMultiPageGenerator.Generate(pageCount, workItem),
            PageCount = pageCount,
        };
    }
}
