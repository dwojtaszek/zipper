using Zipper.Emails;

namespace Zipper
{
    /// <summary>
    /// Configuration for EML generation operations.
    /// </summary>
    public record EmlGenerationConfig
    {
        /// <summary>
        /// Gets index of the file being generated (used for template variation).</summary>
        public int FileIndex { get; init; }

        /// <summary>
        /// Gets attachment rate as percentage (0-100).
        /// </summary>
        public int AttachmentRate { get; init; }

        /// <summary>
        /// Gets optional email category constraint.
        /// </summary>
        public EmailCategory? Category { get; init; }
    }

    /// <summary>
    /// Result of EML generation containing content and attachment info.
    /// </summary>
    public record EmlGenerationResult
    {
        /// <summary>
        /// Gets the generated EML file content as byte array.
        /// </summary>
        public byte[] Content { get; init; } = Array.Empty<byte>();

        /// <summary>
        /// Gets optional attachment information for the file data.
        /// </summary>
        public (string filename, byte[] content)? Attachment { get; init; }

        /// <summary>
        /// Gets the email template used to generate this EML content.
        /// Propagated to FileData for load file metadata consistency.
        /// </summary>
        public Email? Template { get; init; }
    }

    /// <summary>
    /// Service responsible for generating EML email content with proper separation of concerns.
    /// </summary>
    public static class EmlGenerationService
    {
        /// <summary>
        /// Generates EML content based on configuration.
        /// </summary>
        /// <param name="config">Configuration for EML generation.</param>
        /// <returns>EML generation result with content and optional attachment.</returns>
        public static EmlGenerationResult GenerateEmlContent(EmlGenerationConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);

            var email = EmailFactory.Create(config.FileIndex, config.FileIndex, config.Category, Random.Shared);
            var attachmentInfo = EmailAttachmentPicker.Default.Pick(
                config.FileIndex, config.AttachmentRate, EmailAttachmentPicker.PlaceholderPool, Random.Shared);
            var emlContent = EmailSerializer.ToEml(email, attachmentInfo);

            (string filename, byte[] content)? attachment = attachmentInfo != null
                ? (attachmentInfo.FileName, attachmentInfo.Content)
                : null;

            return new EmlGenerationResult
            {
                Content = emlContent,
                Attachment = attachment,
                Template = email,
            };
        }

        /// <summary>
        /// Generates EML content using explicit parameters for backward compatibility.
        /// </summary>
        /// <param name="fileIndex">Index of the file being generated.</param>
        /// <param name="attachmentRate">Attachment rate as percentage (0-100).</param>
        /// <param name="category">Optional email category constraint.</param>
        /// <returns>EML generation result with content and optional attachment.</returns>
        public static EmlGenerationResult GenerateEmlContent(
            int fileIndex,
            int attachmentRate,
            EmailCategory? category = null)
        {
            var config = new EmlGenerationConfig
            {
                FileIndex = fileIndex,
                AttachmentRate = attachmentRate,
                Category = category,
            };

            return GenerateEmlContent(config);
        }
    }
}
