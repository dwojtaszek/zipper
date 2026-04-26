using System.Text;
using Xunit;

namespace Zipper
{
    public class EncodingHelperTests
    {
        public EncodingHelperTests()
        {
            // Required for ANSI/Windows-1252 encoding lookups.
            // In production, this is called in Program.Main.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Fact]
        public void GetEncoding_WithUtf8_ReturnsUtf8Encoding()
        {
            var encoding = EncodingHelper.GetEncoding("UTF-8");
            Assert.NotNull(encoding);
            Assert.Equal("utf-8", encoding!.BodyName);
        }

        [Fact]
        public void GetEncoding_WithAnsi_ReturnsWindows1252Encoding()
        {
            var encoding = EncodingHelper.GetEncoding("ANSI");
            Assert.NotNull(encoding);
            Assert.Equal(1252, encoding!.CodePage);
        }

        [Fact]
        public void GetEncoding_WithWindows1252_ReturnsWindows1252Encoding()
        {
            var encoding = EncodingHelper.GetEncoding("Windows-1252");
            Assert.NotNull(encoding);
            Assert.Equal(1252, encoding!.CodePage);
        }

        [Fact]
        public void GetEncoding_WithUtf16_ReturnsUnicodeEncoding()
        {
            var encoding = EncodingHelper.GetEncoding("UTF-16");
            Assert.NotNull(encoding);
            Assert.Equal("Unicode", encoding!.EncodingName);
        }

        [Fact]
        public void GetEncoding_WithNull_ReturnsNull()
        {
            var encoding = EncodingHelper.GetEncoding(null);
            Assert.Null(encoding);
        }

        [Fact]
        public void GetEncoding_WithEmptyString_ReturnsNull()
        {
            var encoding = EncodingHelper.GetEncoding(string.Empty);
            Assert.Null(encoding);
        }

        [Fact]
        public void GetEncoding_WithUnknownName_ReturnsNull()
        {
            var encoding = EncodingHelper.GetEncoding("NONEXISTENT");
            Assert.Null(encoding);
        }

        [Fact]
        public void GetEncodingOrDefault_WithValidName_ReturnsEncoding()
        {
            var encoding = EncodingHelper.GetEncodingOrDefault("ANSI");
            Assert.NotNull(encoding);
            Assert.Equal(1252, encoding!.CodePage);
        }

        [Fact]
        public void GetEncodingOrDefault_WithUnknownName_ReturnsUtf8Fallback()
        {
            var encoding = EncodingHelper.GetEncodingOrDefault("UNKNOWN");
            Assert.NotNull(encoding);
            Assert.Equal("utf-8", encoding!.BodyName);
        }

        [Fact]
        public void GetEncoding_WithAscii_ReturnsAsciiEncoding()
        {
            var encoding = EncodingHelper.GetEncoding("ASCII");
            Assert.NotNull(encoding);
            Assert.Equal("us-ascii", encoding!.BodyName);
        }
    }
}
