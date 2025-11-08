using System;

namespace Zipper
{
    /// <summary>
    /// Configuration for EML generation operations
    /// </summary>
    public record EmlGenerationConfig
    {
        /// <summary>
        /// Index of the file being generated (used for template variation)
        /// </summary>
        public int FileIndex { get; init; }

        /// <summary>
        /// Attachment rate as percentage (0-100)
        /// </summary>
        public int AttachmentRate { get; init; }

        /// <summary>
        /// Optional email category constraint
        /// </summary>
        public EmailTemplateSystem.EmailCategory? Category { get; init; }
    }

    /// <summary>
    /// Result of EML generation containing content and attachment info
    /// </summary>
    public record EmlGenerationResult
    {
        /// <summary>
        /// The generated EML file content as byte array
        /// </summary>
        public byte[] Content { get; init; } = Array.Empty<byte>();

        /// <summary>
        /// Optional attachment information for the file data
        /// </summary>
        public (string filename, byte[] content)? Attachment { get; init; }
    }

    /// <summary>
    /// Service responsible for generating EML email content with proper separation of concerns
    /// </summary>
    public static class EmlGenerationService
    {
        /// <summary>
        /// Generates EML content based on configuration
        /// </summary>
        /// <param name="config">Configuration for EML generation</param>
        /// <returns>EML generation result with content and optional attachment</returns>
        public static EmlGenerationResult GenerateEmlContent(EmlGenerationConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));

            // Get a realistic email template
            var emailTemplate = EmailTemplateSystem.GetRandomTemplate(
                config.FileIndex,
                config.FileIndex,
                config.Category);

            // Determine if we should include an attachment
            (string filename, byte[] content)? attachment = null;
            if (ShouldIncludeAttachment(config.AttachmentRate))
            {
                attachment = PlaceholderFiles.GetRandomAttachment();
            }

            // Build the EML content
            var emlContent = EmailBuilder.BuildEmail(
                emailTemplate.To,
                emailTemplate.From,
                emailTemplate.Subject,
                emailTemplate.SentDate,
                emailTemplate.Body,
                attachment);

            return new EmlGenerationResult
            {
                Content = emlContent,
                Attachment = attachment
            };
        }

        /// <summary>
        /// Generates EML content using explicit parameters for backward compatibility
        /// </summary>
        /// <param name="fileIndex">Index of the file being generated</param>
        /// <param name="attachmentRate">Attachment rate as percentage (0-100)</param>
        /// <param name="category">Optional email category constraint</param>
        /// <returns>EML generation result with content and optional attachment</returns>
        public static EmlGenerationResult GenerateEmlContent(
            int fileIndex,
            int attachmentRate,
            EmailTemplateSystem.EmailCategory? category = null)
        {
            var config = new EmlGenerationConfig
            {
                FileIndex = fileIndex,
                AttachmentRate = attachmentRate,
                Category = category
            };

            return GenerateEmlContent(config);
        }

        /// <summary>
        /// Determines whether an attachment should be included based on the attachment rate
        /// </summary>
        /// <param name="attachmentRate">Attachment rate as percentage (0-100)</param>
        /// <returns>True if an attachment should be included</returns>
        private static bool ShouldIncludeAttachment(int attachmentRate)
        {
            // Ensure attachment rate is within valid bounds
            attachmentRate = Math.Max(0, Math.Min(100, attachmentRate));
            return Random.Shared.Next(100) < attachmentRate;
        }
    }
}