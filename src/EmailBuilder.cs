using System.Buffers;
using System.Text;

namespace Zipper
{
    /// <summary>
    /// Represents email metadata and content for building EML files.
    /// </summary>
    public record EmailTemplate
    {
        public string To { get; init; } = string.Empty;

        public string From { get; init; } = string.Empty;

        public string Subject { get; init; } = string.Empty;

        public DateTime SentDate { get; init; } = DateTime.Now;

        public string Body { get; init; } = string.Empty;

        public string? Cc { get; init; }

        public string? Bcc { get; init; }

        public string? ReplyTo { get; init; }

        public bool IsHighPriority { get; init; } = false;

        public bool RequestReadReceipt { get; init; } = false;
    }

    /// <summary>
    /// Represents attachment information for emails.
    /// </summary>
    public record AttachmentInfo
    {
        public string FileName { get; init; } = string.Empty;

        public byte[] Content { get; init; } = Array.Empty<byte>();

        public string? ContentType { get; init; }

        public string? ContentId { get; init; }

        public bool IsInline { get; init; } = false;
    }

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
        {
            using var ms = new MemoryStream();
            using var writer = new StreamWriter(ms, new UTF8Encoding(false));

            BuildEmailToWriter(writer, template, attachment);
            writer.Flush();
            return ms.ToArray();
        }

        /// <summary>
        /// Writes EML content directly to a TextWriter to minimize allocations.
        /// </summary>
        public static void BuildEmailToWriter(TextWriter writer, EmailTemplate template, AttachmentInfo? attachment = null)
        {
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            var boundary = attachment != null ? GenerateBoundary() : string.Empty;

            // Build headers
            BuildHeaders(writer, template, attachment, boundary);

            // Build body and attachments
            if (attachment != null)
            {
                BuildMultipartContent(writer, template, attachment, boundary);
            }
            else
            {
                BuildSimpleContent(writer, template);
            }
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

        private static void BuildHeaders(TextWriter writer, EmailTemplate template, AttachmentInfo? attachment, string boundary)
        {
            // Standard headers
            writer.WriteLine($"From: {template.From}");
            writer.WriteLine($"To: {template.To}");

            if (!string.IsNullOrEmpty(template.Cc))
            {
                writer.WriteLine($"Cc: {template.Cc}");
            }

            if (!string.IsNullOrEmpty(template.Bcc))
            {
                writer.WriteLine($"Bcc: {template.Bcc}");
            }

            writer.WriteLine($"Subject: {template.Subject}");
            writer.WriteLine($"Date: {template.SentDate:ddd, dd MMM yyyy HH:mm:ss zzz}");
            writer.WriteLine("MIME-Version: 1.0");

            // Priority headers
            if (template.IsHighPriority)
            {
                writer.WriteLine("X-Priority: 1");
                writer.WriteLine("Priority: Urgent");
            }

            if (template.RequestReadReceipt)
            {
                writer.WriteLine($"Disposition-Notification-To: {template.From}");
            }

            if (!string.IsNullOrEmpty(template.ReplyTo))
            {
                writer.WriteLine($"Reply-To: {template.ReplyTo}");
            }

            // Content type based on attachment presence
            if (attachment != null)
            {
                writer.WriteLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
                writer.WriteLine();
            }
        }

        private static void BuildMultipartContent(TextWriter writer, EmailTemplate template, AttachmentInfo attachment, string boundary)
        {
            // Text body part
            writer.WriteLine($"--{boundary}");
            writer.WriteLine("Content-Type: text/plain; charset=utf-8");
            writer.WriteLine("Content-Transfer-Encoding: 8bit");
            writer.WriteLine();
            writer.WriteLine(template.Body);
            writer.WriteLine();

            // Attachment part
            writer.WriteLine($"--{boundary}");
            writer.WriteLine($"Content-Type: {GetContentType(attachment)}; name=\"{attachment.FileName}\"");
            writer.WriteLine("Content-Transfer-Encoding: base64");

            if (attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
            {
                writer.WriteLine($"Content-ID: <{attachment.ContentId}>");
                writer.WriteLine($"Content-Disposition: inline; filename=\"{attachment.FileName}\"");
            }
            else
            {
                writer.WriteLine($"Content-Disposition: attachment; filename=\"{attachment.FileName}\"");
            }

            writer.WriteLine();

            // Optimization: Process base64 conversion in chunks and write directly
            const int ChunkSize = 57 * 1024;
            int offset = 0;
            int totalLength = attachment.Content.Length;

            int maxBase64Chars = ((ChunkSize + 2) / 3) * 4;
            int maxLineBreaks = maxBase64Chars / 76;
            int bufferSize = maxBase64Chars + (maxLineBreaks * 2);
            char[] charBuffer = ArrayPool<char>.Shared.Rent(bufferSize);

            try
            {
                while (offset < totalLength)
                {
                    int count = Math.Min(ChunkSize, totalLength - offset);
                    bool endsWithCrlf;

                    if (Convert.TryToBase64Chars(
                        new ReadOnlySpan<byte>(attachment.Content, offset, count),
                        charBuffer,
                        out int charsWritten,
                        Base64FormattingOptions.InsertLineBreaks))
                    {
                        writer.Write(charBuffer, 0, charsWritten);
                        endsWithCrlf = charsWritten >= 2 &&
                            charBuffer[charsWritten - 2] == '\r' &&
                            charBuffer[charsWritten - 1] == '\n';
                    }
                    else
                    {
                        System.Diagnostics.Debug.Fail(
                            $"TryToBase64Chars failed unexpectedly for chunk of {count} bytes; falling back to string allocation.");
                        var base64 = Convert.ToBase64String(
                            attachment.Content, offset, count, Base64FormattingOptions.InsertLineBreaks);
                        writer.Write(base64);
                        endsWithCrlf = base64.EndsWith("\r\n", StringComparison.Ordinal);
                    }

                    offset += count;

                    if (offset < totalLength && !endsWithCrlf)
                    {
                        writer.Write("\r\n");
                    }
                }
            }
            finally
            {
                ArrayPool<char>.Shared.Return(charBuffer, clearArray: true);
            }

            writer.WriteLine();
            writer.WriteLine($"--{boundary}--");
        }

        private static void BuildSimpleContent(TextWriter writer, EmailTemplate template)
        {
            writer.WriteLine("Content-Type: text/plain; charset=utf-8");
            writer.WriteLine("Content-Transfer-Encoding: 8bit");
            writer.WriteLine();
            writer.WriteLine(template.Body);
        }

        private static string GenerateBoundary()
        {
            return "----=" + Guid.NewGuid().ToString("N");
        }

        private static string GetContentType(AttachmentInfo attachment)
        {
            if (!string.IsNullOrEmpty(attachment.ContentType))
            {
                return attachment.ContentType;
            }

            var extension = System.IO.Path.GetExtension(attachment.FileName);
            return ContentTypeHelper.GetContentTypeForExtension(extension);
        }
    }
}
