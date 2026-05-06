using System.Text;
using Zipper.Emails;
using Xunit;

namespace Zipper
{
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
            Assert.Contains("From: sender@example.com", content);
            Assert.Contains("To: recipient@example.com", content);
            Assert.Contains("Subject: RFC 2822 Test", content);
            Assert.Contains("Date:", content);
            Assert.Contains("MIME-Version: 1.0", content);
            Assert.Contains("Content-Type: text/plain; charset=utf-8", content);
            Assert.Contains("Content-Transfer-Encoding: 8bit", content);
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
            Assert.Contains(unicodeBody, content);
            Assert.Contains("Unicode Subject: ñoño", content);
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
            Assert.Contains("multipart/mixed", content);
            Assert.Contains("Content-Transfer-Encoding: base64", content);
            Assert.Contains("Content-Disposition: attachment; filename=\"data.pdf\"", content);
            Assert.Contains("application/pdf", content);
            Assert.True(result.Length > 0);
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
            Assert.Contains("X-Priority: 1", content);
            Assert.Contains("Priority: Urgent", content);
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
            Assert.Contains("Disposition-Notification-To: sender@example.com", content);
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
            Assert.Contains("Cc: cc@example.com", content);
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
            Assert.Contains("Reply-To: reply@example.com", content);
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
            Assert.DoesNotContain("X-Priority:", content);
            Assert.DoesNotContain("Priority: Urgent", content);
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
            Assert.Contains("Content-ID: <img-001>", content);
            Assert.Contains("Content-Disposition: inline; filename=\"image.png\"", content);
        }
    }
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
        Assert.Contains("Cc: cc@example.com", content);
        Assert.Contains("Bcc: bcc@example.com", content);
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
        Assert.Contains(expectedContentType, content);
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
        Assert.Contains("application/custom-type", content);
    }

    [Fact]
    public void ToEml_LargeAttachment_HandlesCorrectly()
    {
        var email = new Email
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Large Attachment",
            SentDate = new DateTime(2024, 1, 1),
            Body = "Email with large attachment",
        };
        var largeContent = new byte[1024 * 1024]; // 1MB
        Random.Shared.NextBytes(largeContent);
        var attachment = new EmailAttachment
        {
            FileName = "large.dat",
            Content = largeContent,
        };

        var result = EmailSerializer.ToEml(email, attachment);

        Assert.True(result.Length > largeContent.Length);
        var content = Encoding.UTF8.GetString(result);
        Assert.Contains("base64", content.ToLowerInvariant());
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
        Assert.Contains("Subject:", content);
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
        Assert.Contains("Date:", content);
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
        Assert.Contains("multipart/mixed", content);
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
        }
}
