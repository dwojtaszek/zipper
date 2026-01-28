// <copyright file="EmailBuilder.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

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
            if (template == null)
            {
                throw new ArgumentNullException(nameof(template));
            }

            var boundary = attachment != null ? GenerateBoundary() : string.Empty;
            var sb = new StringBuilder();

            // Build headers
            BuildHeaders(sb, template, attachment, boundary);

            // Build body and attachments
            if (attachment != null)
            {
                BuildMultipartContent(sb, template, attachment, boundary);
            }
            else
            {
                BuildSimpleContent(sb, template);
            }

            return Encoding.UTF8.GetBytes(sb.ToString());
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

        private static void BuildHeaders(StringBuilder sb, EmailTemplate template, AttachmentInfo? attachment, string boundary)
        {
            // Standard headers
            sb.AppendLine($"From: {template.From}");
            sb.AppendLine($"To: {template.To}");

            if (!string.IsNullOrEmpty(template.Cc))
            {
                sb.AppendLine($"Cc: {template.Cc}");
            }

            if (!string.IsNullOrEmpty(template.Bcc))
            {
                sb.AppendLine($"Bcc: {template.Bcc}");
            }

            sb.AppendLine($"Subject: {template.Subject}");
            sb.AppendLine($"Date: {template.SentDate:ddd, dd MMM yyyy HH:mm:ss zzz}");
            sb.AppendLine("MIME-Version: 1.0");

            // Priority headers
            if (template.IsHighPriority)
            {
                sb.AppendLine("X-Priority: 1");
                sb.AppendLine("Priority: Urgent");
            }

            if (template.RequestReadReceipt)
            {
                sb.AppendLine($"Disposition-Notification-To: {template.From}");
            }

            if (!string.IsNullOrEmpty(template.ReplyTo))
            {
                sb.AppendLine($"Reply-To: {template.ReplyTo}");
            }

            // Content type based on attachment presence
            if (attachment != null)
            {
                sb.AppendLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
                sb.AppendLine();
            }
        }

        private static void BuildMultipartContent(StringBuilder sb, EmailTemplate template, AttachmentInfo attachment, string boundary)
        {
            // Text body part
            sb.AppendLine($"--{boundary}");
            sb.AppendLine("Content-Type: text/plain; charset=utf-8");
            sb.AppendLine("Content-Transfer-Encoding: 8bit");
            sb.AppendLine();
            sb.AppendLine(template.Body);
            sb.AppendLine();

            // Attachment part
            sb.AppendLine($"--{boundary}");
            sb.AppendLine($"Content-Type: {GetContentType(attachment)}; name=\"{attachment.FileName}\"");
            sb.AppendLine("Content-Transfer-Encoding: base64");

            if (attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
            {
                sb.AppendLine($"Content-ID: <{attachment.ContentId}>");
                sb.AppendLine($"Content-Disposition: inline; filename=\"{attachment.FileName}\"");
            }
            else
            {
                sb.AppendLine($"Content-Disposition: attachment; filename=\"{attachment.FileName}\"");
            }

            sb.AppendLine();

            // Optimization: Process base64 conversion in chunks to avoid allocating a massive string.
            // We use a chunk size that is a multiple of 57 bytes (which produces 76 chars)
            // so that we can maintain proper line breaking behavior (76 chars per line).
            const int ChunkSize = 57 * 1024;
            int offset = 0;
            int totalLength = attachment.Content.Length;

            // Buffer for base64 chars + newlines.
            // Max expansion: (ChunkSize / 3) * 4 chars + 2 chars per 76 chars (newlines).
            // (58368 / 3) * 4 = 77824 chars.
            // 77824 / 76 * 2 = 2048 chars for newlines.
            // Total ~ 80000.
            char[] charBuffer = new char[80000];

            while (offset < totalLength)
            {
                int count = Math.Min(ChunkSize, totalLength - offset);

                // Use TryToBase64Chars to avoid allocating intermediate strings
                if (Convert.TryToBase64Chars(
                    new ReadOnlySpan<byte>(attachment.Content, offset, count),
                    charBuffer,
                    out int charsWritten,
                    Base64FormattingOptions.InsertLineBreaks))
                {
                    sb.Append(charBuffer, 0, charsWritten);
                }
                else
                {
                    // Fallback should never happen with sufficient buffer
                    sb.Append(Convert.ToBase64String(attachment.Content, offset, count, Base64FormattingOptions.InsertLineBreaks));
                }

                offset += count;

                // If there are more chunks, we need a newline separator because
                // InsertLineBreaks only inserts breaks inside the chunk.
                if (offset < totalLength)
                {
                    sb.Append("\r\n");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"--{boundary}--");
        }

        private static void BuildSimpleContent(StringBuilder sb, EmailTemplate template)
        {
            sb.AppendLine("Content-Type: text/plain; charset=utf-8");
            sb.AppendLine("Content-Transfer-Encoding: 8bit");
            sb.AppendLine();
            sb.AppendLine(template.Body);
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
