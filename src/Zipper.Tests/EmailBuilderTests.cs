// <copyright file="EmailBuilderTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Zipper
{
    public class EmailBuilderTests
    {
        private readonly ITestOutputHelper output;

        public EmailBuilderTests(ITestOutputHelper output)
        {
            this.output = output;
        }

        [Fact]
        public void BuildEmail_BasicTemplate_CreatesValidEml()
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "test@example.com",
                From = "sender@example.com",
                Subject = "Test Subject",
                SentDate = new DateTime(2023, 1, 1, 12, 0, 0),
                Body = "This is a test email body.",
            };

            // Act
            var result = EmailBuilder.BuildEmail(template);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);

            var content = Encoding.UTF8.GetString(result);
            Assert.Contains("From: sender@example.com", content);
            Assert.Contains("To: test@example.com", content);
            Assert.Contains("Subject: Test Subject", content);
            Assert.Contains("This is a test email body.", content);
            Assert.Contains("text/plain; charset=utf-8", content);
        }

        [Fact]
        public void BuildEmail_WithAttachment_CreatesMultipartContent()
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "test@example.com",
                From = "sender@example.com",
                Subject = "Test with Attachment",
                Body = "Email with attachment.",
            };
            var attachment = new AttachmentInfo
            {
                FileName = "test.txt",
                Content = Encoding.UTF8.GetBytes("Test attachment content"),
            };

            // Act
            var result = EmailBuilder.BuildEmail(template, attachment);

            // Assert
            var content = Encoding.UTF8.GetString(result);
            Assert.Contains("multipart/mixed", content);
            Assert.Contains("Content-Type: text/plain", content);
            Assert.Contains("Content-Type: text/plain", content); // .txt files are text/plain
            Assert.Contains("Content-Transfer-Encoding: base64", content);
            Assert.Contains("Content-Disposition: attachment", content);
            Assert.Contains("VGVzdCBhdHRhY2htZW50IGNvbnRlbnQ=", content); // Base64 encoded content
        }

        [Fact]
        public void BuildEmail_WithCcAndBcc_IncludesHeaders()
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "to@example.com",
                From = "from@example.com",
                Cc = "cc@example.com",
                Bcc = "bcc@example.com",
                Subject = "Test CC/BCC",
                Body = "Test body",
            };

            // Act
            var result = EmailBuilder.BuildEmail(template);

            // Assert
            var content = Encoding.UTF8.GetString(result);
            Assert.Contains("Cc: cc@example.com", content);
            Assert.Contains("Bcc: bcc@example.com", content);
        }

        [Fact]
        public void BuildEmail_HighPriority_IncludesPriorityHeaders()
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "test@example.com",
                From = "sender@example.com",
                Subject = "High Priority",
                Body = "Important email",
                IsHighPriority = true,
            };

            // Act
            var result = EmailBuilder.BuildEmail(template);

            // Assert
            var content = Encoding.UTF8.GetString(result);
            Assert.Contains("X-Priority: 1", content);
            Assert.Contains("Priority: Urgent", content);
        }

        [Fact]
        public void BuildEmail_WithReadReceipt_IncludesDispositionHeader()
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "test@example.com",
                From = "sender@example.com",
                Subject = "Read Receipt Test",
                Body = "Please confirm receipt",
                RequestReadReceipt = true,
            };

            // Act
            var result = EmailBuilder.BuildEmail(template);

            // Assert
            var content = Encoding.UTF8.GetString(result);
            Assert.Contains("Disposition-Notification-To: sender@example.com", content);
        }

        [Fact]
        public void BuildEmail_WithReplyTo_IncludesReplyToHeader()
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "test@example.com",
                From = "sender@example.com",
                ReplyTo = "replyto@example.com",
                Subject = "Reply To Test",
                Body = "Please reply to different address",
            };

            // Act
            var result = EmailBuilder.BuildEmail(template);

            // Assert
            var content = Encoding.UTF8.GetString(result);
            Assert.Contains("Reply-To: replyto@example.com", content);
        }

        [Theory]
        [InlineData("test.pdf", "application/pdf")]
        [InlineData("document.docx", "application/msword")]
        [InlineData("spreadsheet.xlsx", "application/vnd.ms-excel")]
        [InlineData("image.jpg", "image/jpeg")]
        [InlineData("image.png", "image/png")]
        [InlineData("text.txt", "text/plain")]
        [InlineData("unknown.xyz", "application/octet-stream")]
        public void BuildEmail_DifferentFileTypes_CorrectContentType(string fileName, string expectedContentType)
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "test@example.com",
                From = "sender@example.com",
                Subject = "File Type Test",
                Body = "Test file type detection",
            };
            var attachment = new AttachmentInfo
            {
                FileName = fileName,
                Content = new byte[] { 1, 2, 3, 4, 5 },
            };

            // Act
            var result = EmailBuilder.BuildEmail(template, attachment);

            // Assert
            var content = Encoding.UTF8.GetString(result);
            Assert.Contains(expectedContentType, content);
        }

        [Fact]
        public void BuildEmail_WithCustomContentType_UsesCustomType()
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "test@example.com",
                From = "sender@example.com",
                Subject = "Custom Content Type",
                Body = "Test custom content type",
            };
            var attachment = new AttachmentInfo
            {
                FileName = "custom.test",
                Content = new byte[] { 1, 2, 3 },
                ContentType = "application/custom-type",
            };

            // Act
            var result = EmailBuilder.BuildEmail(template, attachment);

            // Assert
            var content = Encoding.UTF8.GetString(result);
            Assert.Contains("application/custom-type", content);
        }

        [Fact]
        public void BuildEmail_InlineAttachment_IncludesContentId()
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "test@example.com",
                From = "sender@example.com",
                Subject = "Inline Attachment",
                Body = "Test inline attachment",
            };
            var attachment = new AttachmentInfo
            {
                FileName = "image.png",
                Content = new byte[] { 1, 2, 3 },
                ContentId = "test-image",
                IsInline = true,
            };

            // Act
            var result = EmailBuilder.BuildEmail(template, attachment);

            // Assert
            var content = Encoding.UTF8.GetString(result);
            Assert.Contains("Content-ID: <test-image>", content);
            Assert.Contains("Content-Disposition: inline", content);
        }

        [Fact]
        public void BuildEmail_LegacyInterface_CompatibleWithNew()
        {
            // Arrange
            var to = "test@example.com";
            var from = "sender@example.com";
            var subject = "Legacy Test";
            var sentDate = new DateTime(2023, 1, 1, 12, 0, 0);
            var body = "Legacy interface test";
            var attachment = ("test.txt", Encoding.UTF8.GetBytes("Test content"));

            // Act
            var legacyResult = EmailBuilder.BuildEmail(to, from, subject, sentDate, body, attachment);

            var template = new EmailTemplate
            {
                To = to,
                From = from,
                Subject = subject,
                SentDate = sentDate,
                Body = body,
            };
            var attachmentInfo = new AttachmentInfo
            {
                FileName = attachment.Item1,
                Content = attachment.Item2,
            };
            var newResult = EmailBuilder.BuildEmail(template, attachmentInfo);

            // Assert - Both should produce valid EML content with same structure
            Assert.NotNull(legacyResult);
            Assert.NotNull(newResult);
            Assert.True(legacyResult.Length > 0);
            Assert.True(newResult.Length > 0);

            // Both should contain the same key EML elements
            var legacyContent = Encoding.UTF8.GetString(legacyResult);
            var newContent = Encoding.UTF8.GetString(newResult);

            Assert.Contains("From: sender@example.com", legacyContent);
            Assert.Contains("To: test@example.com", legacyContent);
            Assert.Contains("Subject: Legacy Test", legacyContent);

            Assert.Contains("From: sender@example.com", newContent);
            Assert.Contains("To: test@example.com", newContent);
            Assert.Contains("Subject: Legacy Test", newContent);

            // Both should have multipart content
            Assert.Contains("multipart/mixed", legacyContent);
            Assert.Contains("multipart/mixed", newContent);
        }

        [Fact]
        public void BuildEmail_NullTemplate_ThrowsArgumentNullException()
        {
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => EmailBuilder.BuildEmail(null!));
        }

        [Fact]
        public void BuildEmail_LargeAttachment_HandlesCorrectly()
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "test@example.com",
                From = "sender@example.com",
                Subject = "Large Attachment",
                Body = "Email with large attachment",
            };
            var largeContent = new byte[1024 * 1024]; // 1MB
            new Random().NextBytes(largeContent);

            var attachment = new AttachmentInfo
            {
                FileName = "large.dat",
                Content = largeContent,
            };

            // Act
            var result = EmailBuilder.BuildEmail(template, attachment);

            // Assert
            Assert.True(result.Length > largeContent.Length);
            var content = Encoding.UTF8.GetString(result);
            Assert.Contains("base64", content.ToLowerInvariant());
        }

        [Fact]
        public void BuildEmail_SpecialCharactersInHeaders_EncodesCorrectly()
        {
            // Arrange
            var template = new EmailTemplate
            {
                To = "tëst@éxample.com",
                From = "sênder@examplé.com",
                Subject = "Tëst with spëcial chars: 你好",
                Body = "Body with special characters: café, naïve",
            };

            // Act
            var result = EmailBuilder.BuildEmail(template);

            // Assert
            Assert.NotNull(result);
            var content = Encoding.UTF8.GetString(result);
            Assert.Contains("tëst@éxample.com", content);
            Assert.Contains("Tëst with spëcial chars: 你好", content);
        }

        [Fact]
        public void EmailBuilder_BackwardCompatibility_WithEmlGenerator()
        {
            // Arrange
            var to = "test@example.com";
            var from = "sender@example.com";
            var subject = "Compatibility Test";
            var sentDate = new DateTime(2023, 6, 15, 14, 30, 0);
            var body = "Testing backward compatibility";
            var attachment = ("test.txt", Encoding.UTF8.GetBytes("Attachment content"));

            // Act
            var emailBuilderResult = EmailBuilder.BuildEmail(to, from, subject, sentDate, body, attachment);
            var emlGeneratorResult = EmlGenerator.CreateEmlContent(to, from, subject, sentDate, body, attachment);

            // Assert - Both should produce valid EML content
            Assert.NotNull(emailBuilderResult);
            Assert.NotNull(emlGeneratorResult);
            Assert.True(emailBuilderResult.Length > 0);
            Assert.True(emlGeneratorResult.Length > 0);

            // Both should contain expected EML headers
            var builderContent = Encoding.UTF8.GetString(emailBuilderResult);
            var generatorContent = Encoding.UTF8.GetString(emlGeneratorResult);

            Assert.Contains("From: sender@example.com", builderContent);
            Assert.Contains("To: test@example.com", builderContent);
            Assert.Contains("Subject: Compatibility Test", builderContent);

            Assert.Contains("From: sender@example.com", generatorContent);
            Assert.Contains("To: test@example.com", generatorContent);
            Assert.Contains("Subject: Compatibility Test", generatorContent);
        }
    }
}
