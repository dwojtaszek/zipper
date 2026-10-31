using System.Buffers;
using System.Globalization;
using System.Text;

namespace Zipper.Emails;

/// <summary>
/// Serializes an <see cref="Email"/> and optional attachment(s) to EML bytes.
/// </summary>
internal static class EmailSerializer
{
    /// <summary>
    /// Serializes an email template and optional attachment to EML bytes.
    /// </summary>
    /// <param name="email">Email metadata and body content.</param>
    /// <param name="attachment">Optional attachment.</param>
    /// <param name="seeded">Optional random for deterministic boundary generation.</param>
    /// <param name="realism">Optional MIME realism emission options.</param>
    /// <returns>Byte array representing the EML file content.</returns>
    public static byte[] ToEml(
        Email email,
        EmailAttachment? attachment,
        Random? seeded = null,
        MimeRealismOptions? realism = null)
    {
        return ToEmlWithAttachments(email, attachment is not null ? new[] { attachment } : null, seeded, realism);
    }

    /// <summary>
    /// Serializes an email template and optional attachments to EML bytes.
    /// </summary>
    /// <param name="email">Email metadata and body content.</param>
    /// <param name="attachments">Optional attachments collection.</param>
    /// <param name="seeded">Optional random for deterministic boundary generation.</param>
    /// <param name="realism">Optional MIME realism emission options.</param>
    /// <returns>Byte array representing the EML file content.</returns>
    public static byte[] ToEmlWithAttachments(
        Email email,
        IReadOnlyList<EmailAttachment>? attachments,
        Random? seeded = null,
        MimeRealismOptions? realism = null)
    {
        ArgumentNullException.ThrowIfNull(email);
        using var ms = new MemoryStream();
        using var writer = new StreamWriter(ms, new UTF8Encoding(false))
        {
            NewLine = "\r\n",
        };
        WriteToWriter(writer, email, attachments, seeded, realism);
        writer.Flush();
        return ms.ToArray();
    }

    internal static void WriteToWriter(
        TextWriter writer,
        Email email,
        EmailAttachment? attachment,
        Random? seeded = null)
    {
        WriteToWriter(writer, email, attachment is not null ? new[] { attachment } : null, seeded, realism: null);
    }

    internal static void WriteToWriter(
        TextWriter writer,
        Email email,
        IReadOnlyList<EmailAttachment>? attachments,
        Random? seeded = null,
        MimeRealismOptions? realism = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(email);

        if (realism is null || !realism.IsEnabled)
        {
            // Legacy path: byte-identical output parity
            var attachment = attachments is not null && attachments.Count > 0 ? attachments[0] : null;
            var boundary = attachment is not null ? GenerateBoundary(seeded) : string.Empty;

            BuildHeaders(writer, email, attachment, boundary);

            if (attachment is not null)
            {
                BuildMultipartContent(writer, email, attachment, boundary);
            }
            else
            {
                BuildSimpleContent(writer, email);
            }

            return;
        }

        // Realism-enabled path
        WriteRealismToWriter(writer, email, attachments, seeded, realism);
    }

    private static void WriteRealismToWriter(
        TextWriter writer,
        Email email,
        IReadOnlyList<EmailAttachment>? attachments,
        Random? seeded,
        MimeRealismOptions realism)
    {
        var effectiveAttachments = GetEffectiveAttachments(email, attachments, realism);
        bool hasAttachments = effectiveAttachments.Count > 0;
        bool hasAlternative = realism.MultipartAlternative && (!string.IsNullOrEmpty(email.HtmlBody) || email.Body.Length > 0);

        string transferEncoding = SelectTransferEncoding(realism, seeded);

        string topBoundary = (hasAttachments || hasAlternative) ? GenerateBoundary(seeded) : string.Empty;

        BuildRealismHeaders(writer, email, hasAttachments, hasAlternative, topBoundary, transferEncoding, realism, seeded);

        if (hasAttachments)
        {
            // Top-level is multipart/mixed
            writer.WriteLine($"--{topBoundary}");
            if (hasAlternative)
            {
                WriteAlternativeBody(writer, email, transferEncoding, seeded);
            }
            else
            {
                WriteBodyContent(writer, email.Body, "text/plain; charset=utf-8", transferEncoding);
            }

            writer.WriteLine();

            foreach (var attach in effectiveAttachments)
            {
                WriteSingleAttachmentPart(writer, attach, topBoundary);
            }

            writer.WriteLine($"--{topBoundary}--");
        }
        else if (hasAlternative)
        {
            // Top-level is multipart/alternative
            WriteAlternativeParts(writer, email, transferEncoding, topBoundary);
        }
        else
        {
            // Single-part body
            WriteBodyTextOnly(writer, email.Body, transferEncoding);
        }
    }

    private static IReadOnlyList<EmailAttachment> GetEffectiveAttachments(
        Email email,
        IReadOnlyList<EmailAttachment>? attachments,
        MimeRealismOptions realism)
    {
        var list = new List<EmailAttachment>();
        if (attachments is not null && attachments.Count > 0)
        {
            if (realism.MultiAttachment)
            {
                list.AddRange(attachments);
            }
            else
            {
                list.Add(attachments[0]);
            }
        }
        else if (email.Attachments is not null && email.Attachments.Count > 0)
        {
            if (realism.MultiAttachment)
            {
                list.AddRange(email.Attachments);
            }
            else
            {
                list.Add(email.Attachments[0]);
            }
        }

        return list;
    }

    private static void BuildRealismHeaders(
        TextWriter writer,
        Email email,
        bool hasAttachments,
        bool hasAlternative,
        string boundary,
        string transferEncoding,
        MimeRealismOptions realism,
        Random? seeded)
    {
        WriteRealismHeaderLine(writer, $"From: {email.From}", realism);
        WriteRealismHeaderLine(writer, $"To: {email.To}", realism);

        if (!string.IsNullOrEmpty(email.Cc))
        {
            WriteRealismHeaderLine(writer, $"Cc: {email.Cc}", realism);
        }

        if (!string.IsNullOrEmpty(email.Bcc))
        {
            WriteRealismHeaderLine(writer, $"Bcc: {email.Bcc}", realism);
        }

        string subjectToEmit = email.Subject;
        if (realism.EncodedWords)
        {
            char enc = (seeded ?? Random.Shared).Next(10) < 3 ? 'Q' : 'B';
            subjectToEmit = Rfc2047Helper.EncodeSubject(
                email.Subject,
                fold: false,
                nonRfc: realism.DeliberatelyNonRfc,
                preferredEncoding: enc);
        }

        string subjectHeader = $"Subject: {subjectToEmit}";
        WriteRealismHeaderLine(writer, subjectHeader, realism);
        WriteRealismHeaderLine(
            writer,
            $"Date: {email.SentDate.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture)}",
            realism);

        if (!string.IsNullOrEmpty(email.MessageId))
        {
            WriteRealismHeaderLine(writer, $"Message-ID: {EnsureAngleBrackets(email.MessageId)}", realism);
        }

        if (!string.IsNullOrEmpty(email.InReplyTo))
        {
            WriteRealismHeaderLine(writer, $"In-Reply-To: {EnsureAngleBrackets(email.InReplyTo)}", realism);
        }

        if (!string.IsNullOrEmpty(email.References))
        {
            WriteRealismHeaderLine(writer, $"References: {email.References}", realism);
        }

        writer.WriteLine("MIME-Version: 1.0");

        if (email.IsHighPriority)
        {
            writer.WriteLine("X-Priority: 1");
            writer.WriteLine("Priority: Urgent");
        }

        if (email.RequestReadReceipt)
        {
            WriteRealismHeaderLine(writer, $"Disposition-Notification-To: {email.From}", realism);
        }

        if (!string.IsNullOrEmpty(email.ReplyTo))
        {
            WriteRealismHeaderLine(writer, $"Reply-To: {email.ReplyTo}", realism);
        }

        if (hasAttachments)
        {
            writer.WriteLine($"Content-Type: multipart/mixed; boundary=\"{boundary}\"");
            writer.WriteLine();
        }
        else if (hasAlternative)
        {
            writer.WriteLine($"Content-Type: multipart/alternative; boundary=\"{boundary}\"");
            writer.WriteLine();
        }
        else
        {
            writer.WriteLine("Content-Type: text/plain; charset=utf-8");
            writer.WriteLine($"Content-Transfer-Encoding: {transferEncoding}");
            writer.WriteLine();
        }
    }

    private static void WriteRealismHeaderLine(TextWriter writer, string headerLine, MimeRealismOptions realism)
    {
        writer.WriteLine(realism.HeaderFolding
            ? Rfc2047Helper.FoldHeaderLine(headerLine)
            : headerLine);
    }

    private static void WriteAlternativeBody(
        TextWriter writer,
        Email email,
        string transferEncoding,
        Random? seeded)
    {
        var altBoundary = GenerateBoundary(seeded);
        writer.WriteLine($"Content-Type: multipart/alternative; boundary=\"{altBoundary}\"");
        writer.WriteLine();
        WriteAlternativeParts(writer, email, transferEncoding, altBoundary);
    }

    private static void WriteAlternativeParts(
        TextWriter writer,
        Email email,
        string transferEncoding,
        string altBoundary)
    {
        writer.WriteLine($"--{altBoundary}");
        WriteBodyContent(writer, email.Body, "text/plain; charset=utf-8", transferEncoding);
        writer.WriteLine();

        var html = email.HtmlBody ?? $"<html><body><p>{System.Net.WebUtility.HtmlEncode(email.Body).Replace("\r\n", "\n").Replace("\n", "<br/>\n")}</p></body></html>";
        writer.WriteLine($"--{altBoundary}");
        WriteBodyContent(writer, html, "text/html; charset=utf-8", transferEncoding);
        writer.WriteLine();

        writer.WriteLine($"--{altBoundary}--");
    }

    private static void WriteBodyContent(
        TextWriter writer,
        string body,
        string contentType,
        string transferEncoding)
    {
        writer.WriteLine($"Content-Type: {contentType}");
        writer.WriteLine($"Content-Transfer-Encoding: {transferEncoding}");
        writer.WriteLine();
        WriteBodyTextOnly(writer, body, transferEncoding);
    }

    private static void WriteBodyTextOnly(TextWriter writer, string body, string transferEncoding)
    {
        if (transferEncoding == "quoted-printable")
        {
            writer.WriteLine(QuotedPrintableHelper.Encode(body));
        }
        else if (transferEncoding == "base64")
        {
            writer.WriteLine(Convert.ToBase64String(Encoding.UTF8.GetBytes(body), Base64FormattingOptions.InsertLineBreaks));
        }
        else
        {
            writer.WriteLine(body);
        }
    }

    private static string SelectTransferEncoding(MimeRealismOptions realism, Random? seeded)
    {
        if (realism.Base64Body && realism.QuotedPrintableBody)
        {
            var random = seeded ?? Random.Shared;
            return random.Next(2) == 0 ? "quoted-printable" : "base64";
        }

        if (realism.Base64Body)
        {
            return "base64";
        }

        return realism.QuotedPrintableBody ? "quoted-printable" : "8bit";
    }

    private static void WriteSingleAttachmentPart(TextWriter writer, EmailAttachment attachment, string boundary)
    {
        writer.WriteLine($"--{boundary}");

        if (string.Equals(attachment.ContentType, "message/rfc822", StringComparison.OrdinalIgnoreCase))
        {
            writer.WriteLine($"Content-Type: message/rfc822; name=\"{attachment.FileName}\"");
            writer.WriteLine("Content-Transfer-Encoding: 8bit");
            writer.WriteLine($"Content-Disposition: attachment; filename=\"{attachment.FileName}\"");
            writer.WriteLine();
            writer.Write(Encoding.UTF8.GetString(attachment.Content));
            writer.WriteLine();
            return;
        }

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
        WriteBase64Content(writer, attachment.Content);
        writer.WriteLine();
    }

    private static void BuildHeaders(TextWriter writer, Email email, EmailAttachment? attachment, string boundary)
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
        writer.WriteLine($"Date: {email.SentDate.ToString("ddd, dd MMM yyyy HH:mm:ss zzz", CultureInfo.InvariantCulture)}");

        if (!string.IsNullOrEmpty(email.MessageId))
        {
            writer.WriteLine($"Message-ID: {EnsureAngleBrackets(email.MessageId)}");
        }

        if (!string.IsNullOrEmpty(email.InReplyTo))
        {
            writer.WriteLine($"In-Reply-To: {EnsureAngleBrackets(email.InReplyTo)}");
        }

        if (!string.IsNullOrEmpty(email.References))
        {
            writer.WriteLine($"References: {email.References}");
        }

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

    private static void BuildMultipartContent(TextWriter writer, Email email, EmailAttachment attachment, string boundary)
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
        WriteBase64Content(writer, attachment.Content);
        writer.WriteLine();
        writer.WriteLine($"--{boundary}--");
    }

    private static void WriteBase64Content(TextWriter writer, byte[] content)
    {
        const int ChunkSize = 57 * 1024;
        int offset = 0;
        int totalLength = content.Length;

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
                    new ReadOnlySpan<byte>(content, offset, count),
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
                        content, offset, count, Base64FormattingOptions.InsertLineBreaks);
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
    }

    private static void BuildSimpleContent(TextWriter writer, Email email)
    {
        writer.WriteLine("Content-Type: text/plain; charset=utf-8");
        writer.WriteLine("Content-Transfer-Encoding: 8bit");
        writer.WriteLine();
        writer.WriteLine(email.Body);
    }

    private static string GenerateBoundary(Random? seeded)
    {
        if (seeded is not null)
        {
            return $"----={seeded.Next():x8}{seeded.Next():x8}{seeded.Next():x8}{seeded.Next():x8}";
        }

        return "----=" + Guid.NewGuid().ToString("N");
    }

    private static string GetContentType(EmailAttachment attachment)
    {
        if (!string.IsNullOrEmpty(attachment.ContentType))
        {
            return attachment.ContentType;
        }

        var extension = System.IO.Path.GetExtension(attachment.FileName);
        return ContentTypeHelper.GetContentTypeForExtension(extension);
    }

    private static string EnsureAngleBrackets(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return id;
        }

        id = id.Trim();
        if (id.StartsWith('<') && id.EndsWith('>'))
        {
            return id;
        }

        return $"<{id}>";
    }
}
