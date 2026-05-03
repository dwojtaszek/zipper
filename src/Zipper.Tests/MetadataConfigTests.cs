using Xunit;
using Zipper.Config;

namespace Zipper.Tests
{
    public class MetadataConfigTests
    {
        [Theory]
        [InlineData("eml", true)]
        [InlineData("EML", true)]
        [InlineData("pdf", false)]
        [InlineData("tiff", false)]
        [InlineData("docx", false)]
        public void ShouldIncludeEmlColumns_TrueOnlyForEmlFileType(string fileType, bool expected)
        {
            // Arrange
            var metadata = new MetadataConfig();
            var output = new OutputConfig { FileType = fileType };

            // Act & Assert
            Assert.Equal(expected, metadata.ShouldIncludeEmlColumns(output));
        }

        [Theory]
        [InlineData(true, "pdf", true)]
        [InlineData(false, "pdf", false)]
        [InlineData(false, "eml", true)]
        [InlineData(true, "eml", true)]
        [InlineData(false, "tiff", false)]
        [InlineData(true, "tiff", true)]
        public void ShouldIncludeMetadataColumns_ReturnsExpected(bool withMetadata, string fileType, bool expected)
        {
            // Arrange
            var metadata = new MetadataConfig { WithMetadata = withMetadata };
            var output = new OutputConfig { FileType = fileType };

            // Act & Assert
            Assert.Equal(expected, metadata.ShouldIncludeMetadataColumns(output));
        }
    }
}
