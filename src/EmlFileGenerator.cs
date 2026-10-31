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
            ? new Random(unchecked((int)(request.Metadata.Seed.Value + workItem.Index)))
            : Random.Shared;

        var realism = request.Metadata.GetMimeRealismOptions();
        if (!realism.IsEnabled)
        {
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

        return GenerateWithRealism(workItem, request, random, realism);
    }

    private static GeneratedFileContent GenerateWithRealism(
        FileWorkItem workItem,
        FileGenerationRequest request,
        Random random,
        MimeRealismOptions realism)
    {
        var email = EmailFactory.Create(workItem, request, random);
        var picked = new List<EmailAttachment>(EmailAttachmentPicker.Default.PickMultiple(
            workItem.Index, request.LoadFile.AttachmentRate, EmailAttachmentPicker.PlaceholderPool, random, realism.MultiAttachment));

        if (realism.NestedRfc822)
        {
            bool shouldAddNested = (picked.Count > 0 && random.Next(2) == 0) ||
                                  (picked.Count == 0 && request.LoadFile.AttachmentRate > 0 && random.Next(100) < request.LoadFile.AttachmentRate / 2);

            if (shouldAddNested)
            {
                var childEmail = EmailFactory.Create((int)workItem.Index + 10000, (int)workItem.Index + 20000, null, random);
                var childBytes = EmailSerializer.ToEml(childEmail, (EmailAttachment?)null, random, MimeRealismOptions.Disabled);
                picked.Add(new EmailAttachment
                {
                    FileName = $"forwarded_{workItem.Index}.eml",
                    Content = childBytes,
                    ContentType = "message/rfc822",
                });
            }
        }

        var bytes = EmailSerializer.ToEmlWithAttachments(email, picked, request.Metadata.Seed.HasValue ? random : null, realism);
        EmailAttachment? primary = picked.Count > 0 ? picked[0] : null;

        return new GeneratedFileContent
        {
            Content = bytes,
            Attachment = primary is not null ? (primary.FileName, primary.Content) : null,
            Email = email,
        };
    }
}
