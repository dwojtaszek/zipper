namespace Zipper;

/// <summary>
/// Generates placeholder-based file content for static types (PDF, JPG, basic TIFF).
/// Content is pre-computed and identical for every file.
/// </summary>
internal sealed class PlaceholderFileGenerator : IFileGenerator
{
    private readonly byte[] content;

    public string FileType { get; }

    public bool IsPlaceholderBased => true;

    public PlaceholderFileGenerator(string fileType)
    {
        this.FileType = fileType;
        this.content = PlaceholderFiles.GetContent(fileType);
    }

    public bool RequiresSequentialProcessing(FileGenerationRequest request) => false;

    public GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request)
    {
        return new GeneratedFileContent { Content = this.content };
    }
}
