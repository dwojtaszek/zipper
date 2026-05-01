namespace Zipper;

/// <summary>
/// Generates EML (email) file content with optional attachments.
/// </summary>
internal sealed class EmlFileGenerator : IFileGenerator
{
    public string FileType => "eml";

    public bool IsPlaceholderBased => false;

    public bool RequiresSequentialProcessing(FileGenerationRequest request)
    {
        return request.WithText || request.AttachmentRate > 0;
    }

    public GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request)
    {
        var emlResult = EmlGenerationService.GenerateEmlContent(
            (int)workItem.Index,
            request.AttachmentRate);

        return new GeneratedFileContent
        {
            Content = emlResult.Content,
            Attachment = emlResult.Attachment,
            EmailTemplate = emlResult.Template,
        };
    }
}
