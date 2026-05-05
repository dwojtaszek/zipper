using System.Text;
using Xunit;

namespace Zipper
{
    public class EmailSerializerTests
    {
        [Fact]
        public void ToEml_ProducesRfc2822ValidHeaders()
        {
            var email = new EmailTemplate
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
            var email = new EmailTemplate
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
            var email = new EmailTemplate
            {
                To = "to@example.com",
                From = "from@example.com",
                Subject = "Attachment Test",
                SentDate = new DateTime(2024, 1, 1),
                Body = "See attached.",
            };
            var attachment = new AttachmentInfo
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
            var email = new EmailTemplate
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
            var email = new EmailTemplate
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
            var email = new EmailTemplate
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
            var email = new EmailTemplate
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
            var email = new EmailTemplate
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
            var email = new EmailTemplate
            {
                To = "to@example.com",
                From = "from@example.com",
                Subject = "Inline",
                SentDate = new DateTime(2024, 1, 1),
                Body = "Inline image below.",
            };
            var attachment = new AttachmentInfo
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
