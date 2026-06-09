using Xunit;
using Zipper.Emails;
using Zipper.Profiles.Generation;

namespace Zipper
{
    /// <summary>
    /// Tests for legacy metadata generators that replace MetadataRowBuilder.
    /// </summary>
    public class LegacyMetadataGeneratorTests
    {
        private static ColumnGenerationContext MakeContext(
            long index = 1,
            int folderNumber = 3,
            int documentIndex = 1,
            int? seed = null,
            Email? email = null,
            int dataLength = 1024)
        {
#pragma warning disable S2245
            var random = seed.HasValue ? new Random(seed.Value) : new Random(42);
#pragma warning restore S2245
            var workItem = new FileWorkItem { Index = index, FolderNumber = folderNumber };
            var fileData = new FileData { WorkItem = workItem, DataLength = dataLength, Email = email };
            return new ColumnGenerationContext
            {
                NativeFileIndex = index,
                FolderNumber = folderNumber,
                DocumentIndex = documentIndex,
                Seeded = random,
                Now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                FileData = fileData,
            };
        }

        [Fact]
        public void FolderCustodianGenerator_ProducesExpectedFormat()
        {
            var gen = new LegacyFolderCustodianGenerator();
            var ctx = MakeContext(folderNumber: 5);
            Assert.Equal("Custodian 5", gen.Generate(ctx));
        }

        [Fact]
        public void FolderCustodianGenerator_FolderNumber1_ProducesCustodian1()
        {
            var gen = new LegacyFolderCustodianGenerator();
            var ctx = MakeContext(folderNumber: 1);
            Assert.Equal("Custodian 1", gen.Generate(ctx));
        }

        [Fact]
        public void IndexCustodianGenerator_ModuloPattern()
        {
            var gen = new LegacyIndexCustodianGenerator();
            Assert.Equal("Custodian 1", gen.Generate(MakeContext(index: 0)));
            Assert.Equal("Custodian 3", gen.Generate(MakeContext(index: 2)));
            Assert.Equal("Custodian 10", gen.Generate(MakeContext(index: 9)));
            Assert.Equal("Custodian 1", gen.Generate(MakeContext(index: 10)));
        }

        [Fact]
        public void DateSentGenerator_ProducesDateFormat()
        {
            var gen = new LegacyDateSentGenerator();
            var result = gen.Generate(MakeContext(seed: 42));
            Assert.True(DateTime.TryParseExact(result, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _));
        }

        [Fact]
        public void DateSentGenerator_WithSeed_IsDeterministic()
        {
            var gen = new LegacyDateSentGenerator();
            var r1 = gen.Generate(MakeContext(seed: 42));
            var r2 = gen.Generate(MakeContext(seed: 42));
            Assert.Equal(r1, r2);
        }

        [Fact]
        public void AuthorGenerator_ProducesExpectedFormat()
        {
            var gen = new LegacyAuthorGenerator();
            var result = gen.Generate(MakeContext(seed: 42));
            Assert.Matches(@"Author \d{3}", result);
        }

        [Fact]
        public void AuthorGenerator_WithSeed_IsDeterministic()
        {
            var gen = new LegacyAuthorGenerator();
            Assert.Equal(gen.Generate(MakeContext(seed: 42)), gen.Generate(MakeContext(seed: 42)));
        }

        [Fact]
        public void FileSizeFromDataGenerator_ReturnsDataLength()
        {
            var gen = new LegacyFileSizeFromDataGenerator();
            var ctx = MakeContext(dataLength: 2048);
            Assert.Equal("2048", gen.Generate(ctx));
        }

        [Fact]
        public void RandomFileSizeGenerator_ReturnsReasonableValue()
        {
            var gen = new LegacyRandomFileSizeGenerator();
            var result = gen.Generate(MakeContext(seed: 42));
            var parsed = int.Parse(result, System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(parsed, 1024, 10485760);
        }

        [Fact]
        public void EmailToGenerator_ReadsFromFileDataEmail_WhenPresent()
        {
            var gen = new LegacyEmailToGenerator();
            var email = new Email { To = "test@example.com", From = "f@x.com", Subject = "S", SentDate = DateTime.UtcNow };
            var ctx = MakeContext(email: email);
            Assert.Equal("test@example.com", gen.Generate(ctx));
        }

        [Fact]
        public void EmailToGenerator_FallsBackToSynthetic_WhenEmailNull()
        {
            var gen = new LegacyEmailToGenerator();
            var ctx = MakeContext(index: 5);
            Assert.Equal("recipient5@example.com", gen.Generate(ctx));
        }

        [Fact]
        public void EmailSentDateGenerator_ReadsFromEmail_WhenPresent()
        {
            var gen = new LegacyEmailSentDateGenerator();
            var sentDate = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
            var email = new Email { To = "t@x.com", From = "f@x.com", Subject = "S", SentDate = sentDate };
            var ctx = MakeContext(email: email);
            Assert.Equal("2026-01-15 10:30:00", gen.Generate(ctx));
        }

        [Fact]
        public void EmailAttachmentGenerator_ReturnsEmpty_WhenNoAttachment()
        {
            var gen = new LegacyEmailAttachmentGenerator();
            var ctx = MakeContext();
            Assert.Equal(string.Empty, gen.Generate(ctx));
        }
    }
}
