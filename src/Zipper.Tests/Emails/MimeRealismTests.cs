using System.Security.Cryptography;
using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.Emails;

namespace Zipper.Tests;

public class MimeRealismTests
{
    [Fact]
    public void MimeRealismOptions_Parse_HandlesValidTokensAndPresets()
    {
        var disabled = MimeRealismOptions.Parse(null);
        Assert.False(disabled.IsEnabled);

        var disabledExplicit = MimeRealismOptions.Parse("none");
        Assert.False(disabledExplicit.IsEnabled);

        var standard = MimeRealismOptions.Parse("standard");
        Assert.True(standard.IsEnabled);
        Assert.True(standard.EncodedWords);
        Assert.True(standard.HeaderFolding);
        Assert.True(standard.QuotedPrintableBody);
        Assert.True(standard.MultipartAlternative);
        Assert.True(standard.MultiAttachment);
        Assert.True(standard.NestedRfc822);
        Assert.False(standard.DeliberatelyNonRfc);

        var full = MimeRealismOptions.Parse("full");
        Assert.True(full.IsEnabled);
        Assert.True(full.EncodedWords);
        Assert.True(full.HeaderFolding);
        Assert.True(full.QuotedPrintableBody);
        Assert.True(full.MultipartAlternative);
        Assert.True(full.MultiAttachment);
        Assert.True(full.NestedRfc822);

        var custom = MimeRealismOptions.Parse("encoded-words,nested-rfc822,non-rfc");
        Assert.True(custom.IsEnabled);
        Assert.True(custom.EncodedWords);
        Assert.False(custom.QuotedPrintableBody);
        Assert.True(custom.NestedRfc822);
        Assert.True(custom.DeliberatelyNonRfc);
    }

    [Fact]
    public void ToEml_WithEncodedWords_EncodesNonAsciiSubjectAndDecodesRoundTrip()
    {
        const string originalSubject = "Projektprüfung & Résumé: ñoño";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = originalSubject,
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Test message body with accents.",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            EncodedWords = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Subject: =?utf-8?", emlText, StringComparison.Ordinal);

        // Extract the Subject line
        var lines = emlText.Split(new[] { "\r\n" }, StringSplitOptions.None);
        var subjectLine = lines.First(l => l.StartsWith("Subject: ", StringComparison.Ordinal))["Subject: ".Length..];

        var decoded = Rfc2047Helper.Decode(subjectLine);
        Assert.Equal(originalSubject, decoded);
    }

    [Fact]
    public void ToEml_WithHeaderFolding_FoldsLongSubject()
    {
        var longSubject = "This is an extremely long email subject intended to test RFC 5322 and RFC 2047 header folding behavior across multiple continuation lines in MIME serializers";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = longSubject,
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Test message body.",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            HeaderFolding = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        // Folded headers use CRLF followed by a space or tab
        Assert.Contains("\r\n ", emlText, StringComparison.Ordinal);
        var unfolded = Rfc2047Helper.UnfoldHeader(emlText);
        Assert.Contains($"Subject: {longSubject}", unfolded, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithDeliberatelyNonRfc_EmitsNonRfcEncodedWordVariant()
    {
        var nonAsciiSubject = "Aviso urgente sobre la facturación electrónica del periodo";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = nonAsciiSubject,
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Test message body.",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            EncodedWords = true,
            DeliberatelyNonRfc = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        // Non-RFC variant has unspaced adjacent encoded-words without linear whitespace
        Assert.Contains("?==?", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithQuotedPrintableBody_EncodesBodyAndSetsHeader()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "QP Test",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Line with accents: café, résumé, naïve & special chars = 100%!",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            QuotedPrintableBody = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Transfer-Encoding: quoted-printable", emlText, StringComparison.Ordinal);
        Assert.Contains("=3D", emlText, StringComparison.Ordinal); // '=' encoded as =3D
    }

    [Fact]
    public void ToEml_WithBase64Body_EncodesBodyAndSetsHeader()
    {
        const string bodyText = "Secret message content to be encoded in base64 format.";
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Base64 Body Test",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = bodyText,
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            Base64Body = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Transfer-Encoding: base64", emlText, StringComparison.Ordinal);
        var expectedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(bodyText));
        Assert.Contains(expectedBase64, emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithMultipartAlternative_EmitsTextAndHtmlParts()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Multipart Alternative Test",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Plain text version of the message.",
            HtmlBody = "<html><body><p>HTML version of the message.</p></body></html>",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            MultipartAlternative = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, null, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Type: multipart/alternative; boundary=", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/plain; charset=utf-8", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/html; charset=utf-8", emlText, StringComparison.Ordinal);
        Assert.Contains("Plain text version of the message.", emlText, StringComparison.Ordinal);
        Assert.Contains("<html><body><p>HTML version of the message.</p></body></html>", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithMultipartAlternativeAndAttachment_EmitsNestedMultipartMixedAndAlternative()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Mixed with Alternative and Attachment",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Plain text version.",
            HtmlBody = "<html><body><p>HTML version.</p></body></html>",
        };

        var attachment = new EmailAttachment
        {
            FileName = "report.pdf",
            Content = new byte[] { 0x25, 0x50, 0x44, 0x46 },
            ContentType = "application/pdf",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            MultipartAlternative = true,
        };

        var emlBytes = EmailSerializer.ToEml(email, attachment, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Type: multipart/mixed; boundary=", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: multipart/alternative; boundary=", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/plain; charset=utf-8", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: text/html; charset=utf-8", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Type: application/pdf; name=\"report.pdf\"", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithMultiAttachment_EmitsMultipleAttachments()
    {
        var email = new Email
        {
            To = "to@example.com",
            From = "from@example.com",
            Subject = "Multiple Attachments Test",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Here are two attachments.",
        };

        var attachments = new[]
        {
            new EmailAttachment
            {
                FileName = "doc1.pdf",
                Content = new byte[] { 1, 2, 3 },
                ContentType = "application/pdf",
            },
            new EmailAttachment
            {
                FileName = "image2.jpg",
                Content = new byte[] { 4, 5, 6 },
                ContentType = "image/jpeg",
            },
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            MultiAttachment = true,
        };

        var emlBytes = EmailSerializer.ToEmlWithAttachments(email, attachments, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Type: multipart/mixed; boundary=", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Disposition: attachment; filename=\"doc1.pdf\"", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Disposition: attachment; filename=\"image2.jpg\"", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void ToEml_WithNestedRfc822_EmitsAttachedEmailPart()
    {
        var childEmail = new Email
        {
            To = "sub-recipient@example.com",
            From = "sub-sender@example.com",
            Subject = "Forwarded Inner Email",
            SentDate = new DateTime(2024, 1, 14, 9, 0, 0, DateTimeKind.Utc),
            Body = "Original inner discussion.",
        };
        var childBytes = EmailSerializer.ToEml(childEmail, null, new Random(111));

        var parentEmail = new Email
        {
            To = "parent-recipient@example.com",
            From = "parent-sender@example.com",
            Subject = "Fwd: Forwarded Inner Email",
            SentDate = new DateTime(2024, 1, 15, 12, 0, 0, DateTimeKind.Utc),
            Body = "Please find the attached forwarded email.",
        };

        var nestedAttachment = new EmailAttachment
        {
            FileName = "forwarded.eml",
            Content = childBytes,
            ContentType = "message/rfc822",
        };

        var options = new MimeRealismOptions
        {
            IsEnabled = true,
            NestedRfc822 = true,
        };

        var emlBytes = EmailSerializer.ToEml(parentEmail, nestedAttachment, new Random(42), options);
        var emlText = Encoding.UTF8.GetString(emlBytes);

        Assert.Contains("Content-Type: message/rfc822; name=\"forwarded.eml\"", emlText, StringComparison.Ordinal);
        Assert.Contains("Content-Transfer-Encoding: 8bit", emlText, StringComparison.Ordinal);
        Assert.Contains("Subject: Forwarded Inner Email", emlText, StringComparison.Ordinal);
        Assert.Contains("To: sub-recipient@example.com", emlText, StringComparison.Ordinal);
    }

    [Fact]
    public void EmlFileGenerator_WithMimeRealism_ProducesDeterministicOutputForSameSeed()
    {
        var generator = new EmlFileGenerator();
        var request1 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 3, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 555, MimeRealism = "standard" },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };
        var request2 = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 3, Concurrency = 1 },
            Metadata = new MetadataConfig { Seed = 555, MimeRealism = "standard" },
            LoadFile = new LoadFileConfig { AttachmentRate = 100 },
        };

        for (long i = 1; i <= 3; i++)
        {
            var workItem = new FileWorkItem { Index = i, FileName = $"{i:D8}.eml", FolderName = "f", FolderNumber = 1 };
            var result1 = generator.Generate(workItem, request1);
            var result2 = generator.Generate(workItem, request2);

            Assert.Equal(result1.Content, result2.Content);
        }
    }

    [Fact]
    public void EmlFileGenerator_WithoutMimeRealism_MaintainsLegacyGoldenBytes()
    {
        var generator = new EmlFileGenerator();
        var request = new FileGenerationRequest
        {
            Output = new OutputConfig { FileType = "eml", FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithThreading = false },
        };
        var workItem = new FileWorkItem { Index = 1, FileName = "test.eml", FolderName = "f", FolderNumber = 1 };
        var result = generator.Generate(workItem, request);

        // Golden SHA-256 baseline
        Assert.Equal(
            "2577c72e5c61570d9e8e638b109068bed4087e3533bfbc67086421cf899f7c91",
            Convert.ToHexString(SHA256.HashData(result.Content)).ToLowerInvariant());
    }
}
