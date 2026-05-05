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
        return request.Output.WithText || request.LoadFile.AttachmentRate > 0;
    }

    public GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request)
    {
        var email = EmailFactory.Create(workItem, request, Random.Shared);
        var attachmentInfo = EmailAttachmentPicker.Default.Pick(
            workItem.Index, request.LoadFile.AttachmentRate, EmailAttachmentPicker.PlaceholderPool, Random.Shared);
        var bytes = EmailSerializer.ToEml(email, attachmentInfo);

        return new GeneratedFileContent
        {
            Content = bytes,
            Attachment = attachmentInfo != null ? (attachmentInfo.FileName, attachmentInfo.Content) : null,
            EmailTemplate = email,
        };
    }
}
