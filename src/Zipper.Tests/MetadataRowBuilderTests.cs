using Xunit;

using Zipper.Config;
using Zipper.Emails;

namespace Zipper
{
    public class MetadataRowBuilderTests
    {
        private static MetadataRowBuilder CreateBuilder(int? seed = null, string fileType = "pdf")
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig
                {
                    FileType = fileType,
                    OutputPath = "/tmp",
                    FileCount = 10,
                },
            };
            var random = seed.HasValue ? new Random(seed.Value) : new Random();
            return new MetadataRowBuilder(request, random, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        }

        [Fact]
        public void GetControlNumber_ByWorkItem_ProducesExpectedFormat()
        {
            var builder = CreateBuilder();
            var workItem = new FileWorkItem { Index = 42 };
            Assert.Equal("DOC00000042", builder.GetControlNumber(workItem));
        }

        [Fact]
        public void GetControlNumber_ByIndex_ProducesExpectedFormat()
        {
            var builder = CreateBuilder();
            Assert.Equal("DOC00000042", builder.GetControlNumber(42));
        }

        [Fact]
        public void GetFileSize_FromFileData_ReturnsDataLength()
        {
            var builder = CreateBuilder();
            var fileData = new FileData { DataLength = 1024 };
            Assert.Equal("1024", builder.GetFileSize(fileData));
        }

        [Fact]
        public void GetFileSize_FromLong_ReturnsStringValue()
        {
            var builder = CreateBuilder();
            Assert.Equal("2048", builder.GetFileSize(2048L));
        }

        [Fact]
        public void GetFileSize_Fallback_ReturnsReasonableValue()
        {
            var builder = CreateBuilder(seed: 42);
            var result = builder.GetFileSize();
            var parsed = int.Parse(result);
            Assert.InRange(parsed, 1024, 10485760);
        }

        [Fact]
        public void GetCustodian_ByFolderNumber_MatchesPattern()
        {
            var builder = CreateBuilder();
            Assert.Equal("Custodian 5", builder.GetCustodian(5));
        }

        [Fact]
        public void GetCustodian_ByIndex_ModuloPattern()
        {
            var builder = CreateBuilder();
            Assert.Equal("Custodian 1", builder.GetCustodianByIndex(0));
            Assert.Equal("Custodian 3", builder.GetCustodianByIndex(2));
            Assert.Equal("Custodian 10", builder.GetCustodianByIndex(9));
            Assert.Equal("Custodian 1", builder.GetCustodianByIndex(10));
        }

        [Fact]
        public void GetCustodian_ByCountOverride_WithinRange()
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig { FileType = "pdf" },
                Metadata = new MetadataConfig { CustodianCountOverride = 5 },
            };
            var builder = new MetadataRowBuilder(request, new Random(42), DateTime.UtcNow);
            var result = builder.GetCustodian();
            var parsed = int.Parse(result.AsSpan("Custodian ".Length));
            Assert.InRange(parsed, 1, 5);
        }

        [Fact]
        public void GetDateSent_ReturnsFormattedDate()
        {
            var builder = CreateBuilder(seed: 42);
            var result = builder.GetDateSent();
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", result);
        }

        [Fact]
        public void GetDateCreated_ReturnsFormattedDate()
        {
            var builder = CreateBuilder(seed: 42);
            var result = builder.GetDateCreated();
            Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", result);
        }

        [Fact]
        public void GetAuthor_ReturnsFormattedAuthor()
        {
            var builder = CreateBuilder(seed: 42);
            var result = builder.GetAuthor();
            Assert.Matches(@"^Author \d{3}$", result);
        }

        [Fact]
        public void GetEmailTo_FromTemplate_UsesTemplate()
        {
            var builder = CreateBuilder();
            var template = new Email { To = "lawyer@firm.com" };
            var fileData = new FileData
            {
                WorkItem = new FileWorkItem { Index = 1 },
                Email = template,
            };
            Assert.Equal("lawyer@firm.com", builder.GetEmailTo(fileData.WorkItem, fileData));
        }

        [Fact]
        public void GetEmailTo_Fallback_UsesIndex()
        {
            var builder = CreateBuilder();
            var workItem = new FileWorkItem { Index = 5 };
            var fileData = new FileData { WorkItem = workItem };
            Assert.Equal("recipient5@example.com", builder.GetEmailTo(workItem, fileData));
        }

        [Fact]
        public void GetEmailFrom_Fallback_UsesIndex()
        {
            var builder = CreateBuilder();
            var workItem = new FileWorkItem { Index = 3 };
            var fileData = new FileData { WorkItem = workItem };
            Assert.Equal("sender3@example.com", builder.GetEmailFrom(workItem, fileData));
        }

        [Fact]
        public void GetEmailSubject_Fallback_UsesIndex()
        {
            var builder = CreateBuilder();
            var workItem = new FileWorkItem { Index = 7 };
            var fileData = new FileData { WorkItem = workItem };
            Assert.Equal("Email Subject 7", builder.GetEmailSubject(workItem, fileData));
        }

        [Fact]
        public void GetEmailSentDate_FromTemplate_UsesTemplate()
        {
            var builder = CreateBuilder();
            var template = new Email
            {
                To = "a@b.com",
                SentDate = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Utc),
            };
            var fileData = new FileData
            {
                WorkItem = new FileWorkItem { Index = 1 },
                Email = template,
            };
            Assert.Equal("2025-06-15 14:30:00", builder.GetEmailSentDate(fileData.WorkItem, fileData));
        }

        [Fact]
        public void GetPageCount_ReturnsPageCount()
        {
            var builder = CreateBuilder();
            var fileData = new FileData { PageCount = 7 };
            Assert.Equal("7", builder.GetPageCount(fileData));
        }

        [Fact]
        public void GetTextPath_ReplacesExtension()
        {
            var builder = CreateBuilder(fileType: "pdf");
            var workItem = new FileWorkItem { FilePathInZip = "folder_001/doc_0001.pdf" };
            Assert.Equal("folder_001/doc_0001.txt", builder.GetTextPath(workItem));
        }

        [Fact]
        public void GetBatesNumber_WithConfig_ReturnsGeneratedNumber()
        {
            var request = new FileGenerationRequest
            {
                Output = new OutputConfig { FileType = "pdf" },
                Bates = new BatesNumberConfig
                {
                    Prefix = "TEST",
                    Start = 100,
                    Digits = 6,
                },
            };
            var builder = new MetadataRowBuilder(request, new Random(), DateTime.UtcNow);
            var workItem = new FileWorkItem { Index = 5 };
            Assert.Equal("TEST000104", builder.GetBatesNumber(workItem));
        }

        [Fact]
        public void GetBatesNumber_WithoutConfig_ReturnsEmpty()
        {
            var builder = CreateBuilder();
            var workItem = new FileWorkItem { Index = 1 };
            Assert.Equal(string.Empty, builder.GetBatesNumber(workItem));
        }

        [Fact]
        public void SanitizeField_ReplacesWindowsNewline()
        {
            var result = MetadataRowBuilder.SanitizeField("line1\r\nline2", "\u00ae");
            Assert.Equal("line1®line2", result);
        }

        [Fact]
        public void SanitizeField_ReplacesUnixNewline()
        {
            var result = MetadataRowBuilder.SanitizeField("line1\nline2", "\u00ae");
            Assert.Equal("line1®line2", result);
        }

        [Fact]
        public void SanitizeField_ReplacesCarriageReturn()
        {
            var result = MetadataRowBuilder.SanitizeField("line1\rline2", "\u00ae");
            Assert.Equal("line1®line2", result);
        }

        [Fact]
        public void SanitizeField_EmptyString_ReturnsEmpty()
        {
            Assert.Equal(string.Empty, MetadataRowBuilder.SanitizeField(string.Empty, "\u00ae"));
            Assert.Null(MetadataRowBuilder.SanitizeField(null!, "\u00ae"));
        }

        [Fact]
        public void SanitizeField_NoNewlines_ReturnsUnchanged()
        {
            var result = MetadataRowBuilder.SanitizeField("hello world", "\u00ae");
            Assert.Equal("hello world", result);
        }

        [Fact]
        public void AppendField_WithQuote_WrapsValue()
        {
            var sb = new System.Text.StringBuilder();
            MetadataRowBuilder.AppendField(sb, "test", '\u00fe', true);
            Assert.Equal("\u00fetest\u00fe", sb.ToString());
        }

        [Fact]
        public void AppendField_WithoutQuote_AppendsRaw()
        {
            var sb = new System.Text.StringBuilder();
            MetadataRowBuilder.AppendField(sb, "test", '\u00fe', false);
            Assert.Equal("test", sb.ToString());
        }

        [Fact]
        public void GetEmailAttachment_WithAttachment_ReturnsFilename()
        {
            var builder = CreateBuilder();
            var fileData = new FileData
            {
                WorkItem = new FileWorkItem { Index = 1 },
                Attachment = ("report.pdf", new byte[0]),
            };
            Assert.Equal("report.pdf", builder.GetEmailAttachment(fileData));
        }

        [Fact]
        public void GetEmailAttachment_WithoutAttachment_ReturnsEmpty()
        {
            var builder = CreateBuilder();
            var fileData = new FileData { WorkItem = new FileWorkItem { Index = 1 } };
            Assert.Equal(string.Empty, builder.GetEmailAttachment(fileData));
        }

        [Fact]
        public void GetFileSize_Fallback_WithSeed_IsDeterministic()
        {
            var builder1 = CreateBuilder(seed: 123);
            var builder2 = CreateBuilder(seed: 123);
            Assert.Equal(builder1.GetFileSize(), builder2.GetFileSize());
        }

        [Fact]
        public void GetDateSent_WithSeed_IsDeterministic()
        {
            var builder1 = CreateBuilder(seed: 42);
            var builder2 = CreateBuilder(seed: 42);
            Assert.Equal(builder1.GetDateSent(), builder2.GetDateSent());
        }
    }
}
