using System.Buffers;
using System.Text;

namespace Zipper;

/// <summary>
/// Serializes an <see cref="EmailTemplate"/> and optional attachment to EML bytes.
/// </summary>
internal static class EmailSerializer
{
    /// <summary>
    /// Serializes an email template and optional attachment to EML bytes.
    /// </summary>
    /// <param name="email">Email metadata and body content.</param>
    /// <param name="attachment">Optional attachment.</param>
    /// <returns>Byte array representing the EML file content.</returns>
    public static byte[] ToEml(EmailTemplate email, AttachmentInfo? attachment)
    {
        ArgumentNullException.ThrowIfNull(email);
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false))
        {
            NewLine = "\r\n",
        };
        WriteToWriter(writer, email, attachment);
        writer.Flush();
        return ms.ToArray();
    }

    internal static void WriteToWriter(TextWriter writer, EmailTemplate email, AttachmentInfo? attachment)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(email);

        var boundary = attachment != null ? GenerateBoundary() : string.Empty;

        BuildHeaders(writer, email, attachment, boundary);

        if (attachment != null)
        {
            BuildMultipartContent(writer, email, attachment, boundary);
        }
        else
        {
            BuildSimpleContent(writer, email);
        }
    }

    private static void BuildHeaders(TextWriter writer, EmailTemplate email, AttachmentInfo? attachment, string boundary)
    {
        writer.WriteLine($"From: {email.From}");
        writer.WriteLine($"To: {email.To}");

        if (!string.IsNullOrEmpty(email.Cc))
        {
            writer.WriteLine($"Cc: {email.Cc}");
        }

        if (!string.IsNullOrEmpty(email.Bcc))
        {
            writer.WriteLine($"Bcc: {email.Bcc}");
        }

        writer.WriteLine($"Subject: {email.Subject}");
        writer.WriteLine($"Date: {email.SentDate:ddd, dd MMM yyyy HH:mm:ss zzz}");
        writer.WriteLine("MIME-Version: 1.0");

        if (email.IsHighPriority)
        {
            writer.WriteLine("X-Priority: 1");
            writer.WriteLine("Priority: Urgent");
        }

        if (email.RequestReadReceipt)
        {
            writer.WriteLine($"Disposition-Notification-To: {email.From}");
        }

        if (!string.IsNullOrEmpty(email.ReplyTo))
        {
            writer.WriteLine($"Reply-To: {email.ReplyTo}");
        }

        if (attachment != null)
        {
            writer.WriteLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
            writer.WriteLine();
        }
    }

    private static void BuildMultipartContent(TextWriter writer, EmailTemplate email, AttachmentInfo attachment, string boundary)
    {
        writer.WriteLine($"--{boundary}");
        writer.WriteLine("Content-Type: text/plain; charset=utf-8");
        writer.WriteLine("Content-Transfer-Encoding: 8bit");
        writer.WriteLine();
        writer.WriteLine(email.Body);
        writer.WriteLine();

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

    private static void BuildSimpleContent(TextWriter writer, EmailTemplate email)
    {
        writer.WriteLine("Content-Type: text/plain; charset=utf-8");
        writer.WriteLine("Content-Transfer-Encoding: 8bit");
        writer.WriteLine();
        writer.WriteLine(email.Body);
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
