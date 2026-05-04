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
            request.Metadata = request.Metadata with { ColumnProfile = profile };
            Assert.Same(profile, request.Metadata.ColumnProfile);
            Assert.Same(profile, request.Metadata.ColumnProfile);
        }

        [Fact]
        public void FileGenerationRequest_DateFormatOverride_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.Metadata = request.Metadata with { DateFormatOverride = "yyyy-MM-dd" };
            Assert.Equal("yyyy-MM-dd", request.Metadata.DateFormatOverride);
            Assert.Equal("yyyy-MM-dd", request.Metadata.DateFormatOverride);
        }

        [Fact]
        public void FileGenerationRequest_EmptyPercentageOverride_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.Metadata = request.Metadata with { EmptyPercentageOverride = 25 };
            Assert.Equal(25, request.Metadata.EmptyPercentageOverride);
            Assert.Equal(25, request.Metadata.EmptyPercentageOverride);
        }

        [Fact]
        public void FileGenerationRequest_WithFamilies_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.Metadata = request.Metadata with { WithFamilies = true };
            Assert.True(request.Metadata.WithFamilies);
            Assert.True(request.Metadata.WithFamilies);
        }

        [Fact]
        public void FileGenerationRequest_AttachmentRate_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.LoadFile = request.LoadFile with { AttachmentRate = 50 };
            Assert.Equal(50, request.LoadFile.AttachmentRate);
            Assert.Equal(50, request.LoadFile.AttachmentRate);
        }

        [Fact]
        public void FileGenerationRequest_NewlineDelimiter_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.Delimiters = request.Delimiters with { NewlineDelimiter = " " };
            Assert.Equal(" ", request.Delimiters.NewlineDelimiter);
            Assert.Equal(" ", request.Delimiters.NewlineDelimiter);
        }

        [Fact]
        public void FileGenerationRequest_MultiValueDelimiter_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.Delimiters = request.Delimiters with { MultiValueDelimiter = "," };
            Assert.Equal(",", request.Delimiters.MultiValueDelimiter);
            Assert.Equal(",", request.Delimiters.MultiValueDelimiter);
        }

        [Fact]
        public void FileGenerationRequest_NestedValueDelimiter_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.Delimiters = request.Delimiters with { NestedValueDelimiter = "/" };
            Assert.Equal("/", request.Delimiters.NestedValueDelimiter);
            Assert.Equal("/", request.Delimiters.NestedValueDelimiter);
        }

        [Fact]
        public void FileGenerationRequest_ChaosScenario_Roundtrips()
        {
            var request = new FileGenerationRequest();
            request.Chaos = request.Chaos with { ChaosScenario = "encoding-nightmare" };
            Assert.Equal("encoding-nightmare", request.Chaos.ChaosScenario);
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

        [Fact]
        public void Clone_LoadFileFormatsList_IsIsolatedFromOriginal()
        {
            var original = new FileGenerationRequest
            {
                LoadFile = new LoadFileConfig
                {
                    LoadFileFormats = new List<LoadFileFormat> { LoadFileFormat.Dat },
                },
            };
            var clone = original.Clone();

            clone.LoadFileFormats!.Add(LoadFileFormat.Csv);

            Assert.Single(original.LoadFileFormats!);
            Assert.Equal(2, clone.LoadFileFormats!.Count);
            Assert.NotSame(original.LoadFileFormats, clone.LoadFileFormats);
        }

        [Fact]
        public void Clone_NullLoadFileFormats_RemainsNull()
        {
            var original = new FileGenerationRequest();
            var clone = original.Clone();
            Assert.Null(clone.LoadFileFormats);
        }
    }
}
