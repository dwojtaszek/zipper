using System.Text;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;
using Zipper.Emails;

namespace Zipper.Tests;

public class EmailSerializerTests
{
    [Fact]
    public void ToEml_ProducesRfc2822ValidHeaders()
    {
        var email = new Email
        {
            To = "recipient@example.com",
            From = "sender@example.com",
            Subject = "RFC 2822 Test",
            SentDate = new DateTime(2024, 3, 15, 10, 30, 0),
            Body = "Test body.",
        };

        var result = EmailSerializer.ToEml(email, null);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("From: sender@example.com", content, StringComparison.Ordinal);
        Assert.Contains("To: recipient@example.com", content, StringComparison.Ordinal);
        Assert.Contains("Subject: RFC 2822 Test", content, StringComparison.Ordinal);
        Assert.Contains("Date: Fri, 15 Mar 2024 10:30:00", content, StringComparison.Ordinal);
        Assert.Contains("MIME-Version: 1.0", content, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/plain; charset=utf-8", content, StringComparison.Ordinal);
        Assert.Contains("Content-Transfer-Encoding: 8bit", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_PreservesUnicodeBody()
    {
        var unicodeBody = "Unicode: café, naïve, 你好, Привет";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Unicode Subject: ñoño",
            SentDate = new DateTime(2024, 1, 1),
            Body = unicodeBody,
        };

        var result = EmailSerializer.ToEml(email, null);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains(unicodeBody, content, StringComparison.Ordinal);
        Assert.Contains("Unicode Subject: ñoño", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_HandlesBinaryAttachment()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Attachment Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "See attached.",
        };
        var attachment = new EmailAttachment
        {
            FileName = "data.pdf",
            Content = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D },
        };

        var result = EmailSerializer.ToEml(email, attachment);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("multipart/mixed", content, StringComparison.Ordinal);
        Assert.Contains("Content-Transfer-Encoding: base64", content, StringComparison.Ordinal);
        Assert.Contains("Content-Disposition: attachment; filename=\"data.pdf\"", content, StringComparison.Ordinal);
        Assert.Contains("application/pdf", content, StringComparison.Ordinal);
        Assert.True(result.Length > 0);

        AssertAttachmentDecodesCorrectly(content, attachment.Content);
    }

    [Fact]
    public void ToEml_IncludesHighPriorityHeader_WhenSet()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Urgent",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Act now.",
            IsHighPriority = true,
        };

        var result = EmailSerializer.ToEml(email, null);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("X-Priority: 1", content, StringComparison.Ordinal);
        Assert.Contains("Priority: Urgent", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_IncludesReadReceiptHeader_WhenSet()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "sender@example.com",
            Subject = "Please Confirm",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Let me know you got this.",
            RequestReadReceipt = true,
        };

        var result = EmailSerializer.ToEml(email, null);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("Disposition-Notification-To: sender@example.com", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_IncludesCcHeader_WhenSet()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "CC Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "CC included.",
            Cc = "cc@example.com",
        };

        var result = EmailSerializer.ToEml(email, null);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("Cc: cc@example.com", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_IncludesReplyToHeader_WhenSet()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Reply-To Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Reply elsewhere.",
            ReplyTo = "reply@example.com",
        };

        var result = EmailSerializer.ToEml(email, null);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("Reply-To: reply@example.com", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_NullEmail_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => EmailSerializer.ToEml(null!, null));
    }

    [Fact]
    public void ToEml_NoHighPriorityHeaders_WhenNotSet()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Normal",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Nothing special.",
            IsHighPriority = false,
        };

        var result = EmailSerializer.ToEml(email, null);

        var content = Encoding.UTF8.GetString(result);
        Assert.DoesNotContain("X-Priority:", content, StringComparison.Ordinal);
        Assert.DoesNotContain("Priority: Urgent", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithInlineAttachment_IncludesContentId()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Inline",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Inline image below.",
        };
        var attachment = new EmailAttachment
        {
            FileName = "image.png",
            Content = new byte[] { 1, 2, 3 },
            ContentId = "img-001",
            IsInline = true,
        };

        var result = EmailSerializer.ToEml(email, attachment);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("Content-ID: <img-001>", content, StringComparison.Ordinal);
        Assert.Contains("Content-Disposition: inline; filename=\"image.png\"", content, StringComparison.Ordinal);
    }

    // Tests redistributed from EmailBuilderTests
    [Fact]
    public void ToEml_IncludesBccHeader_WhenSet()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "BCC Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "BCC included.",
            Cc = "cc@example.com",
            Bcc = "bcc@example.com",
        };

        var result = EmailSerializer.ToEml(email, null);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("Cc: cc@example.com", content, StringComparison.Ordinal);
        Assert.Contains("Bcc: bcc@example.com", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("test.pdf", "application/pdf")]
    [InlineData("document.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("spreadsheet.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("image.jpg", "image/jpeg")]
    [InlineData("image.png", "image/png")]
    [InlineData("text.txt", "text/plain")]
    [InlineData("unknown.xyz", "application/octet-stream")]
    public void ToEml_DifferentFileTypes_CorrectContentType(string fileName, string expectedContentType)
    {
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "File Type Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Test file type detection",
        };
        var attachment = new EmailAttachment
        {
            FileName = fileName,
            Content = new byte[] { 1, 2, 3, 4, 5 },
        };

        var result = EmailSerializer.ToEml(email, attachment);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains(expectedContentType, content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithCustomContentType_UsesCustomType()
    {
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Custom Content Type",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Test custom content type",
        };
        var attachment = new EmailAttachment
        {
            FileName = "custom.test",
            Content = new byte[] { 1, 2, 3 },
            ContentType = "application/custom-type",
        };

        var result = EmailSerializer.ToEml(email, attachment);

        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("application/custom-type", content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(56)]
    [InlineData(57)]
    [InlineData(58)]
    [InlineData(58367)]
    [InlineData(58368)]
    [InlineData(58369)]
    [InlineData(116736)]
    [InlineData(116737)]
    public void ToEml_Attachment_DecodesPayloadAndCoversChunkBoundaries(int payloadLength)
    {
        var expectedBytes = CreateDeterministicPayload(payloadLength);
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Attachment Boundary Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Email with attachment",
        };
        var attachment = new EmailAttachment
        {
            FileName = "payload.bin",
            Content = expectedBytes,
        };

        var result = EmailSerializer.ToEml(email, attachment);

        Assert.NotNull(result);
        var content = Encoding.UTF8.GetString(result);

        Assert.Contains("multipart/mixed", content, StringComparison.Ordinal);
        Assert.Contains("Content-Transfer-Encoding: base64", content, StringComparison.Ordinal);
        Assert.Contains("Content-Disposition: attachment; filename=\"payload.bin\"", content, StringComparison.Ordinal);
        Assert.Contains("application/octet-stream", content, StringComparison.Ordinal);

        AssertAttachmentDecodesCorrectly(content, expectedBytes);
    }

    private static byte[] CreateDeterministicPayload(int length)
    {
        var buffer = new byte[length];
        for (int i = 0; i < length; i++)
        {
            buffer[i] = (byte)(((i * 31 + 7) ^ (i >> 8) ^ (i >> 16)) & 0xFF);
        }
        return buffer;
    }

    private static void AssertAttachmentDecodesCorrectly(string content, byte[] expectedBytes)
    {
        // 1. Extract MIME boundary from root Content-Type header
        var boundaryMatch = Regex.Match(
            content,
            @"^Content-Type:\s*multipart/mixed;\s*boundary=""([^""]+)""",
            RegexOptions.Multiline | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(2));
        Assert.True(boundaryMatch.Success, "Expected root Content-Type header with multipart/mixed and boundary parameter.");
        string boundary = boundaryMatch.Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(boundary), "Extracted MIME boundary must not be empty or whitespace.");

        // 2. Assert closing boundary is present and at the end of the multipart content
        string closingBoundary = $"--{boundary}--";
        Assert.Contains(closingBoundary, content, StringComparison.Ordinal);
        Assert.True(content.TrimEnd().EndsWith(closingBoundary, StringComparison.Ordinal),
            $"EML content must end with closing boundary '{closingBoundary}'.");

        // 3. Independently identify and extract MIME parts
        string delimiter = $"--{boundary}";
        var parts = new List<string>();
        int searchPos = 0;

        while (true)
        {
            int delimPos = content.IndexOf(delimiter, searchPos, StringComparison.Ordinal);
            if (delimPos < 0)
            {
                break;
            }

            int afterDelim = delimPos + delimiter.Length;
            if (afterDelim + 1 < content.Length && content[afterDelim] == '-' && content[afterDelim + 1] == '-')
            {
                // Reached closing boundary: --boundary--
                break;
            }

            // Consume line break immediately following boundary delimiter
            if (afterDelim + 1 < content.Length && content[afterDelim] == '\r' && content[afterDelim + 1] == '\n')
            {
                afterDelim += 2;
            }
            else if (afterDelim < content.Length && content[afterDelim] == '\n')
            {
                afterDelim += 1;
            }

            int nextDelim = content.IndexOf(delimiter, afterDelim, StringComparison.Ordinal);
            Assert.True(nextDelim >= 0, "MIME part must be terminated by a subsequent boundary delimiter.");

            string partContent = content.Substring(afterDelim, nextDelim - afterDelim);
            parts.Add(partContent);

            searchPos = nextDelim;
        }

        // 4. Independently identify Attachment part by Content-Transfer-Encoding: base64
        string? attachmentPart = null;
        foreach (var part in parts)
        {
            int headerSep = part.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerSep < 0)
            {
                headerSep = part.IndexOf("\n\n", StringComparison.Ordinal);
            }

            string partHeaders = headerSep >= 0 ? part.Substring(0, headerSep) : part;
            if (partHeaders.Contains("Content-Transfer-Encoding: base64", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(attachmentPart is null, "Expected exactly one attachment part with base64 encoding.");
                attachmentPart = part;
            }
        }

        Assert.NotNull(attachmentPart);

        // 5. Separate attachment headers from payload
        int separatorIndex = attachmentPart.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        int separatorLen = 4;
        if (separatorIndex < 0)
        {
            separatorIndex = attachmentPart.IndexOf("\n\n", StringComparison.Ordinal);
            separatorLen = 2;
        }

        Assert.True(separatorIndex >= 0, "Attachment part must contain header/body separator.");
        string rawPayload = attachmentPart.Substring(separatorIndex + separatorLen);
        string trimmedPayload = rawPayload.TrimEnd('\r', '\n');

        // 6. Assert base64 lines <= 76 characters, including multi-chunk concatenation
        string[] base64Lines = trimmedPayload.Length == 0
            ? Array.Empty<string>()
            : trimmedPayload.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        Assert.All(base64Lines, line =>
        {
            Assert.NotEmpty(line);
            Assert.True(line.Length <= 76,
                $"Base64 line length must not exceed 76 characters, but was {line.Length}: '{line}'");
        });

        // 7. Decode with Convert.FromBase64String and compare every input byte
        string combinedBase64 = string.Concat(base64Lines);
        byte[] decodedBytes = Convert.FromBase64String(combinedBase64);

        Assert.Equal(expectedBytes, decodedBytes);
    }

    [Fact]
    public void AssertAttachmentDecodesCorrectly_Fails_WhenPayloadIsZeroFilled()
    {
        var expectedBytes = CreateDeterministicPayload(100);
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Corruption Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Test",
        };
        var corruptedAttachment = new EmailAttachment
        {
            FileName = "corrupted.bin",
            Content = new byte[100],
        };
        var corruptedEml = Encoding.UTF8.GetString(EmailSerializer.ToEml(email, corruptedAttachment));

        Assert.ThrowsAny<XunitException>(() =>
            AssertAttachmentDecodesCorrectly(corruptedEml, expectedBytes));
    }

    [Fact]
    public void AssertAttachmentDecodesCorrectly_Fails_WhenChunkIsDropped()
    {
        var expectedBytes = CreateDeterministicPayload(58368 * 2);
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Dropped Chunk Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Test",
        };
        var corruptedAttachment = new EmailAttachment
        {
            FileName = "dropped.bin",
            Content = CreateDeterministicPayload(58368),
        };
        var corruptedEml = Encoding.UTF8.GetString(EmailSerializer.ToEml(email, corruptedAttachment));

        Assert.ThrowsAny<XunitException>(() =>
            AssertAttachmentDecodesCorrectly(corruptedEml, expectedBytes));
    }

    [Fact]
    public void AssertAttachmentDecodesCorrectly_Fails_WhenChunkIsRepeated()
    {
        const int chunkSize = 58368;
        var expectedBytes = CreateDeterministicPayload(chunkSize * 2);
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Repeated Chunk Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Test",
        };
        // Corrupted attachment duplicates chunk 1 into chunk 2, keeping the same 116,736-byte total length
        var corruptedBytes = new byte[chunkSize * 2];
        Array.Copy(expectedBytes, 0, corruptedBytes, 0, chunkSize);
        Array.Copy(expectedBytes, 0, corruptedBytes, chunkSize, chunkSize);

        var corruptedAttachment = new EmailAttachment
        {
            FileName = "repeated.bin",
            Content = corruptedBytes,
        };
        var corruptedEml = Encoding.UTF8.GetString(EmailSerializer.ToEml(email, corruptedAttachment));

        Assert.ThrowsAny<XunitException>(() =>
            AssertAttachmentDecodesCorrectly(corruptedEml, expectedBytes));
    }

    [Fact]
    public void AssertAttachmentDecodesCorrectly_Fails_WhenClosingBoundaryIsMissing()
    {
        var expectedBytes = CreateDeterministicPayload(10);
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Missing Boundary Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Test",
        };
        var attachment = new EmailAttachment
        {
            FileName = "data.bin",
            Content = expectedBytes,
        };
        var eml = Encoding.UTF8.GetString(EmailSerializer.ToEml(email, attachment));

        var boundaryMatch = Regex.Match(eml, @"boundary=""([^""]+)""", RegexOptions.None, TimeSpan.FromSeconds(2));
        Assert.True(boundaryMatch.Success);
        string closingBoundary = $"--{boundaryMatch.Groups[1].Value}--";
        int lastBoundaryIndex = eml.LastIndexOf(closingBoundary, StringComparison.Ordinal);
        Assert.True(lastBoundaryIndex >= 0);
        var corruptedEml = eml.Remove(lastBoundaryIndex, closingBoundary.Length);

        Assert.ThrowsAny<XunitException>(() =>
            AssertAttachmentDecodesCorrectly(corruptedEml, expectedBytes));
    }

    [Fact]
    public void AssertAttachmentDecodesCorrectly_Fails_WhenBase64LineExceeds76Characters()
    {
        var expectedBytes = CreateDeterministicPayload(100);
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Line Length Test",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Test",
        };
        var attachment = new EmailAttachment
        {
            FileName = "data.bin",
            Content = expectedBytes,
        };
        var eml = Encoding.UTF8.GetString(EmailSerializer.ToEml(email, attachment));

        var lineMatch = Regex.Match(
            eml,
            @"([A-Za-z0-9+/]{76})\r\n([A-Za-z0-9+/=]+)",
            RegexOptions.None,
            TimeSpan.FromSeconds(2));
        Assert.True(lineMatch.Success, "Expected multi-line base64 payload with a 76-character line followed by CRLF.");
        var corruptedEml = eml.Remove(lineMatch.Groups[1].Index + 76, 2);

        Assert.ThrowsAny<XunitException>(() =>
            AssertAttachmentDecodesCorrectly(corruptedEml, expectedBytes));
    }

    [Fact]
    public void ToEml_EmptyBodyAndSubject_CreatesValidEml()
    {
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = string.Empty,
            SentDate = new DateTime(2023, 1, 1),
            Body = string.Empty,
        };

        var result = EmailSerializer.ToEml(email, null);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("Subject:", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_DateTimeMinValue_HandlesGracefully()
    {
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Date Test",
            SentDate = DateTime.MinValue,
            Body = "Test body",
        };

        var result = EmailSerializer.ToEml(email, null);

        Assert.NotNull(result);
        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("Date:", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_ZeroLengthAttachment_HandlesGracefully()
    {
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Empty Attachment",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Test",
        };
        var attachment = new EmailAttachment
        {
            FileName = "empty.txt",
            Content = Array.Empty<byte>(),
        };

        var result = EmailSerializer.ToEml(email, attachment);

        Assert.NotNull(result);
        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("multipart/mixed", content, StringComparison.Ordinal);

        AssertAttachmentDecodesCorrectly(content, attachment.Content);
    }

    [Fact]
    public void ToEml_NullAttachmentFileName_HandlesGracefully()
    {
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Null FileName",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Test",
        };
        var attachment = new EmailAttachment
        {
            FileName = null!,
            Content = new byte[] { 1, 2, 3 },
        };

        var result = EmailSerializer.ToEml(email, attachment);

        Assert.NotNull(result);
        Assert.True(result.Length > 0);
        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("Subject: Null FileName", content, StringComparison.Ordinal);
        Assert.Contains("From: sender@example.com", content, StringComparison.Ordinal);
        Assert.Contains("To: test@example.com", content, StringComparison.Ordinal);
        Assert.Contains("multipart/mixed", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithoutThreadingHeaders_PreservesParityWithoutMessageIdOrThreadHeaders()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "No Threading",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Test message.",
        };

        var result = EmailSerializer.ToEml(email, null);
        var content = Encoding.UTF8.GetString(result);

        Assert.DoesNotContain("Message-ID:", content, StringComparison.Ordinal);
        Assert.DoesNotContain("In-Reply-To:", content, StringComparison.Ordinal);
        Assert.DoesNotContain("References:", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithMessageId_EmitsMessageIdHeader()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Root Thread Email",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Initial thread message.",
            MessageId = "<msg.00000001@example.com>",
        };

        var result = EmailSerializer.ToEml(email, null);
        var content = Encoding.UTF8.GetString(result);

        Assert.Contains("Message-ID: <msg.00000001@example.com>\r\n", content, StringComparison.Ordinal);
        Assert.DoesNotContain("In-Reply-To:", content, StringComparison.Ordinal);
        Assert.DoesNotContain("References:", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithMessageIdMissingAngleBrackets_AddsAngleBrackets()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Missing Brackets",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Message ID without brackets.",
            MessageId = "msg.00000001@example.com",
            InReplyTo = "parent.00000001@example.com",
        };

        var result = EmailSerializer.ToEml(email, null);
        var content = Encoding.UTF8.GetString(result);

        Assert.Contains("Message-ID: <msg.00000001@example.com>\r\n", content, StringComparison.Ordinal);
        Assert.Contains("In-Reply-To: <parent.00000001@example.com>\r\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithMessageIdContainingWhitespace_TrimsAndWrapsAngleBrackets()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Whitespace ID",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Message ID with surrounding whitespace.",
            MessageId = "  <msg.00000001@example.com>  ",
            InReplyTo = "  parent.00000001@example.com  ",
        };

        var result = EmailSerializer.ToEml(email, null);
        var content = Encoding.UTF8.GetString(result);

        Assert.Contains("Message-ID: <msg.00000001@example.com>\r\n", content, StringComparison.Ordinal);
        Assert.Contains("In-Reply-To: <parent.00000001@example.com>\r\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithInReplyToAndReferences_EmitsThreadHeaders()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Re: Root Thread Email",
            SentDate = new DateTime(2024, 1, 15, 14, 0, 0, DateTimeKind.Utc),
            Body = "Reply message.",
            MessageId = "<msg.00000002@example.com>",
            InReplyTo = "<msg.00000001@example.com>",
            References = "<msg.00000001@example.com>",
        };

        var result = EmailSerializer.ToEml(email, null);
        var content = Encoding.UTF8.GetString(result);

        Assert.Contains("Message-ID: <msg.00000002@example.com>\r\n", content, StringComparison.Ordinal);
        Assert.Contains("In-Reply-To: <msg.00000001@example.com>\r\n", content, StringComparison.Ordinal);
        Assert.Contains("References: <msg.00000001@example.com>\r\n", content, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_MultiLevelReferencesChain_EmitsAllAncestors()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Re: Root Thread Email",
            SentDate = new DateTime(2024, 1, 15, 16, 0, 0, DateTimeKind.Utc),
            Body = "Second reply message.",
            MessageId = "<msg.00000003@example.com>",
            InReplyTo = "<msg.00000002@example.com>",
            References = "<msg.00000001@example.com> <msg.00000002@example.com>",
        };

        var result = EmailSerializer.ToEml(email, null);
        var content = Encoding.UTF8.GetString(result);

        Assert.Contains("Message-ID: <msg.00000003@example.com>\r\n", content, StringComparison.Ordinal);
        Assert.Contains("In-Reply-To: <msg.00000002@example.com>\r\n", content, StringComparison.Ordinal);
        Assert.Contains("References: <msg.00000001@example.com> <msg.00000002@example.com>\r\n", content, StringComparison.Ordinal);
    }
}
