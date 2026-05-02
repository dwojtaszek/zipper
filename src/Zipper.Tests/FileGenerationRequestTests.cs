using Xunit;
using Zipper.Config;
using Zipper.Profiles;

namespace Zipper.Tests
{
    [Collection("ConsoleTests")]
    public class FileGenerationRequestTests
    {
        [Fact]
        public void DelimiterConfig_GetColumnChar_ReturnsFirstChar()
        {
            var config = new DelimiterConfig { ColumnDelimiter = "|" };
            Assert.Equal('|', config.GetColumnChar());
        }

        [Fact]
        public void DelimiterConfig_GetQuoteChar_WithQuote_ReturnsFirstChar()
        {
            var config = new DelimiterConfig { QuoteDelimiter = "\"" };
            Assert.Equal('"', config.GetQuoteChar());
        }

        [Fact]
        public void DelimiterConfig_GetQuoteChar_EmptyQuote_ReturnsNullChar()
        {
            var config = new DelimiterConfig { QuoteDelimiter = string.Empty };
            Assert.Equal('\0', config.GetQuoteChar());
        }

        [Fact]
        public void DelimiterConfig_HasQuote_WithQuote_ReturnsTrue()
        {
            var config = new DelimiterConfig { QuoteDelimiter = "\"" };
            Assert.True(config.HasQuote);
        }

        [Fact]
        public void DelimiterConfig_HasQuote_EmptyQuote_ReturnsFalse()
        {
            var config = new DelimiterConfig { QuoteDelimiter = string.Empty };
            Assert.False(config.HasQuote);
        }

        [Fact]
        public void FileGenerationRequest_SetColumnProfile_DelegatesToMetadata()
        {
            var request = new FileGenerationRequest();
            var profile = new ColumnProfile { Name = "test", Description = "standard" };
            request.ColumnProfile = profile;
            Assert.Same(profile, request.ColumnProfile);
            Assert.Same(profile, request.Metadata.ColumnProfile);
        }

        [Fact]
        public void FileGenerationRequest_DateFormatOverride_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.DateFormatOverride = "yyyy-MM-dd";
            Assert.Equal("yyyy-MM-dd", request.DateFormatOverride);
            Assert.Equal("yyyy-MM-dd", request.Metadata.DateFormatOverride);
        }

        [Fact]
        public void FileGenerationRequest_EmptyPercentageOverride_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.EmptyPercentageOverride = 25;
            Assert.Equal(25, request.EmptyPercentageOverride);
            Assert.Equal(25, request.Metadata.EmptyPercentageOverride);
        }

        [Fact]
        public void FileGenerationRequest_WithFamilies_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.WithFamilies = true;
            Assert.True(request.WithFamilies);
            Assert.True(request.Metadata.WithFamilies);
        }

        [Fact]
        public void FileGenerationRequest_AttachmentRate_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.AttachmentRate = 50;
            Assert.Equal(50, request.AttachmentRate);
            Assert.Equal(50, request.LoadFile.AttachmentRate);
        }

        [Fact]
        public void FileGenerationRequest_NewlineDelimiter_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.NewlineDelimiter = " ";
            Assert.Equal(" ", request.NewlineDelimiter);
            Assert.Equal(" ", request.Delimiters.NewlineDelimiter);
        }

        [Fact]
        public void FileGenerationRequest_MultiValueDelimiter_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.MultiValueDelimiter = ",";
            Assert.Equal(",", request.MultiValueDelimiter);
            Assert.Equal(",", request.Delimiters.MultiValueDelimiter);
        }

        [Fact]
        public void FileGenerationRequest_NestedValueDelimiter_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.NestedValueDelimiter = "/";
            Assert.Equal("/", request.NestedValueDelimiter);
            Assert.Equal("/", request.Delimiters.NestedValueDelimiter);
        }

        [Fact]
        public void FileGenerationRequest_ChaosScenario_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.ChaosScenario = "encoding-nightmare";
            Assert.Equal("encoding-nightmare", request.ChaosScenario);
            Assert.Equal("encoding-nightmare", request.Chaos.ChaosScenario);
        }

        [Fact]
        public void FileGenerationRequest_LoadfileOnly_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.LoadfileOnly = true;
            Assert.True(request.LoadfileOnly);
        }

        [Fact]
        public void Clone_CreatesIndependentCopy()
        {
            var original = new FileGenerationRequest
            {
                Output = new OutputConfig { FileCount = 100, FileType = "pdf" },
                Metadata = new MetadataConfig { WithMetadata = true },
                Chaos = new ChaosConfig { ChaosAmount = "5%" },
            };
            var clone = original.Clone();

            Assert.Equal(100, clone.FileCount);
            Assert.Equal("pdf", clone.FileType);
            Assert.True(clone.WithMetadata);
            Assert.Equal("5%", clone.ChaosAmount);
        }

        [Fact]
        public void Clone_ModifyingClone_DoesNotAffectOriginal()
        {
            var original = new FileGenerationRequest
            {
                Output = new OutputConfig { FileCount = 100 },
                Chaos = new ChaosConfig { ChaosAmount = "5%" },
            };
            var clone = original.Clone();

            clone.Output = clone.Output with { FileCount = 200 };
            clone.Chaos = clone.Chaos with { ChaosAmount = "10%" };

            Assert.Equal(100, original.FileCount);
            Assert.Equal("5%", original.ChaosAmount);
            Assert.Equal(200, clone.FileCount);
            Assert.Equal("10%", clone.ChaosAmount);
        }
    }
}
