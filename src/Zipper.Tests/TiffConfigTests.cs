using Xunit;
using Zipper.Config;

namespace Zipper.Tests
{
    public class TiffConfigTests
    {
        [Theory]
        [InlineData("tiff", true, true)]
        [InlineData("tiff", false, false)]
        [InlineData("pdf", true, false)]
        [InlineData("pdf", false, false)]
        [InlineData("eml", true, false)]
        public void ShouldIncludePageCount_ReturnsExpected(string fileType, bool hasRange, bool expected)
        {
            // Arrange
            var tiff = hasRange ? new TiffConfig { PageRange = (1, 10) } : new TiffConfig();
            var output = new OutputConfig { FileType = fileType };

            // Act & Assert
            Assert.Equal(expected, tiff.ShouldIncludePageCount(output));
        }
    }
}
