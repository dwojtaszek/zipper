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
                Attachments = attachmentInfo is not null
                    ? new[] { (attachmentInfo.FileName, attachmentInfo.Content) }
                    : Array.Empty<(string filename, byte[] content)>(),
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

        if (realism.NestedRfc822 && realism.MaxNestingDepth > 0)
        {
            bool shouldAddNested = random.Next(2) == 0;

            if (shouldAddNested && (realism.MultiAttachment || picked.Count == 0))
            {
                var nestedAttachment = CreateNestedRfc822Attachment(workItem.Index, 1, random, realism, email.SentDate);
                if (realism.MultiAttachment)
                {
                    picked.Add(nestedAttachment);
                }
                else
                {
                    picked.Insert(0, nestedAttachment);
                }
            }
        }

        var bytes = EmailSerializer.ToEmlWithAttachments(email, picked, request.Metadata.Seed.HasValue ? random : null, realism);
        EmailAttachment? primary = picked.Count > 0 ? picked[0] : null;

        return new GeneratedFileContent
        {
            Content = bytes,
            Attachment = primary is not null ? (primary.FileName, primary.Content) : null,
            Attachments = picked.Select(attachment => (attachment.FileName, attachment.Content)).ToArray(),
            Email = email,
        };
    }

    private static EmailAttachment CreateNestedRfc822Attachment(
        long workItemIndex,
        int depth,
        Random random,
        MimeRealismOptions realism,
        DateTime referenceDate)
    {
        int recipientIndex = (int)workItemIndex + (depth * 10_000);
        int senderIndex = recipientIndex + 10_000;
        var nestedEmail = EmailFactory.Create(recipientIndex, senderIndex, null, random, referenceDate);
        IReadOnlyList<EmailAttachment> nestedAttachments = Array.Empty<EmailAttachment>();

        if (depth < realism.MaxNestingDepth && random.Next(2) == 0)
        {
            nestedAttachments = new[]
            {
                CreateNestedRfc822Attachment(workItemIndex, depth + 1, random, realism, referenceDate),
            };
        }

        var nestedBytes = EmailSerializer.ToEmlWithAttachments(nestedEmail, nestedAttachments, random, realism);
        return new EmailAttachment
        {
            FileName = depth == 1
                ? $"forwarded_{workItemIndex}.eml"
                : $"forwarded_{workItemIndex}_{depth}.eml",
            Content = nestedBytes,
            ContentType = "message/rfc822",
        };
    }
}
