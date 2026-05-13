using Zipper.Emails;

namespace Zipper;

/// <summary>
/// Generates EML (email) file content with optional attachments.
/// </summary>
internal sealed class EmlFileGenerator : IFileGenerator
{
    public string FileType => "eml";

    public bool IsPlaceholderBased => false;

    public GeneratedFileContent Generate(FileWorkItem workItem, FileGenerationRequest request)
    {
        var random = request.Metadata.Seed.HasValue
            ? new Random(request.Metadata.Seed.Value + (int)workItem.Index)
            : Random.Shared;

        var email = EmailFactory.Create(workItem, request, random);
        var attachmentInfo = EmailAttachmentPicker.Default.Pick(
            workItem.Index, request.LoadFile.AttachmentRate, EmailAttachmentPicker.PlaceholderPool, random);
        var bytes = EmailSerializer.ToEml(email, attachmentInfo, request.Metadata.Seed.HasValue ? random : null);

        return new GeneratedFileContent
        {
            Content = bytes,
            Attachment = attachmentInfo != null ? (attachmentInfo.FileName, attachmentInfo.Content) : null,
            Email = email,
        };
    }
}
