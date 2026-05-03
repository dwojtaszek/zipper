using Xunit;
using Zipper.Config;

namespace Zipper.Tests
{
    public class OutputConfigTests
    {
        [Theory]
        [InlineData("pdf", "pdf")]
        [InlineData("PDF", "pdf")]
        [InlineData("Pdf", "pdf")]
        [InlineData("eml", "eml")]
        [InlineData("EML", "eml")]
        [InlineData("Eml", "eml")]
        [InlineData("tiff", "tiff")]
        [InlineData("TIFF", "tiff")]
        [InlineData("docx", "docx")]
        [InlineData("xlsx", "xlsx")]
        [InlineData("jpg", "jpg")]
        [InlineData("jpeg", "jpeg")]
        [InlineData("png", "png")]
        [InlineData("bmp", "bmp")]
        [InlineData("txt", "txt")]
        public void FileTypeLower_ReturnsLowercaseFileType(string fileType, string expected)
        {
            // Arrange
            var config = new OutputConfig { FileType = fileType };

            // Act & Assert
            Assert.Equal(expected, config.FileTypeLower);
        }

        [Theory]
        [InlineData("eml", true)]
        [InlineData("EML", true)]
        [InlineData("Eml", true)]
        [InlineData("pdf", false)]
        [InlineData("tiff", false)]
        [InlineData("docx", false)]
        public void IsEml_ReturnsTrueOnlyForEmlFileType(string fileType, bool expected)
        {
            // Arrange
            var config = new OutputConfig { FileType = fileType };

            // Act & Assert
            Assert.Equal(expected, config.IsEml);
        }

        [Theory]
        [InlineData("tiff", true)]
        [InlineData("TIFF", true)]
        [InlineData("Tiff", true)]
        [InlineData("pdf", false)]
        [InlineData("eml", false)]
        [InlineData("docx", false)]
        public void IsTiff_ReturnsTrueOnlyForTiffFileType(string fileType, bool expected)
        {
            // Arrange
            var config = new OutputConfig { FileType = fileType };

            // Act & Assert
            Assert.Equal(expected, config.IsTiff);
        }
    }
}
