namespace Zipper;

/// <summary>
/// Generates Microsoft Office format documents (DOCX, XLSX) via IFileGenerator interface.
/// Delegates to the static <see cref="OfficeFileGenerator"/> for actual content creation.
/// </summary>
internal sealed class OfficeFileGeneratorAdapter : IFileGenerator
{
    public string FileType { get; }

    public bool IsPlaceholderBased => false;

    public OfficeFileGeneratorAdapter(string fileType)
    {
        this.FileType = fileType;
    }

    public bool RequiresSequentialProcessing(FileGenerationRequest request) => false;

    public GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request)
    {
        return new GeneratedFileContent
        {
            Content = OfficeFileGenerator.GenerateContent(this.FileType, workItem),
        };
    }
}
