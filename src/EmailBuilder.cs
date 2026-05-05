namespace Zipper
{
    /// <summary>
    /// Builds EML email content with proper MIME formatting.
    /// </summary>
    public static class EmailBuilder
    {
        /// <summary>
        /// Creates EML content from an email template and optional attachment.
        /// </summary>
        /// <param name="template">Email template with metadata and content.</param>
        /// <param name="attachment">Optional attachment information.</param>
        /// <returns>Byte array representing the EML file content.</returns>
        public static byte[] BuildEmail(EmailTemplate template, AttachmentInfo? attachment = null)
            => EmailSerializer.ToEml(template, attachment);

        /// <summary>
        /// Writes EML content directly to a TextWriter to minimize allocations.
        /// </summary>
        public static void BuildEmailToWriter(TextWriter writer, EmailTemplate template, AttachmentInfo? attachment = null)
        {
            ArgumentNullException.ThrowIfNull(writer);
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            EmailSerializer.WriteToWriter(writer, template, attachment);
        }

        /// <summary>
        /// Creates EML content using legacy parameters for backward compatibility.
        /// </summary>
        /// <returns></returns>
        public static byte[] BuildEmail(string to, string from, string subject, DateTime sentDate, string body,
            (string filename, byte[] content)? attachment = null)
        {
            var template = new EmailTemplate
            {
                To = to,
                From = from,
                Subject = subject,
                SentDate = sentDate,
                Body = body,
            };

            var attachmentInfo = attachment.HasValue ? new AttachmentInfo
            {
                FileName = attachment.Value.filename,
                Content = attachment.Value.content,
            }
            : null;

            return BuildEmail(template, attachmentInfo);
        }
    }
}
