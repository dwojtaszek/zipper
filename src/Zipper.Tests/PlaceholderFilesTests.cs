using Xunit;

namespace Zipper
{
    public class PlaceholderFilesTests
    {
        [Fact]
        public void GetContent_Pdf_ReturnsNonEmptyBytes()
        {
            var content = PlaceholderFiles.GetContent("pdf");
            Assert.NotEmpty(content);
        }

        [Fact]
        public void GetContent_Pdf_StartsWithPdfMagicBytes()
        {
            var content = PlaceholderFiles.GetContent("pdf");
            Assert.Equal(0x25, content[0]);
            Assert.Equal(0x50, content[1]);
            Assert.Equal(0x44, content[2]);
            Assert.Equal(0x46, content[3]);
        }

        [Fact]
        public void GetContent_Jpg_ReturnsNonEmptyBytes()
        {
            var content = PlaceholderFiles.GetContent("jpg");
            Assert.NotEmpty(content);
        }

        [Fact]
        public void GetContent_Jpg_StartsWithJpegMagicBytes()
        {
            var content = PlaceholderFiles.GetContent("jpg");
            Assert.Equal(0xFF, content[0]);
            Assert.Equal(0xD8, content[1]);
        }

        [Fact]
        public void GetContent_Tiff_ReturnsNonEmptyBytes()
        {
            var content = PlaceholderFiles.GetContent("tiff");
            Assert.NotEmpty(content);
        }

        [Fact]
        public void GetContent_Tiff_StartsWithTiffMagicBytes()
        {
            var content = PlaceholderFiles.GetContent("tiff");
            Assert.Equal(0x49, content[0]);
            Assert.Equal(0x49, content[1]);
            Assert.Equal(0x2A, content[2]);
            Assert.Equal(0x00, content[3]);
        }

        [Fact]
        public void GetContent_UnknownType_ReturnsEmptyArray()
        {
            var content = PlaceholderFiles.GetContent("unknown");
            Assert.Empty(content);
        }

        [Fact]
        public void GetContent_IsCaseInsensitive_Lowercase()
        {
            var lower = PlaceholderFiles.GetContent("pdf");
            var upper = PlaceholderFiles.GetContent("PDF");
            Assert.Equal(lower, upper);
        }

        [Fact]
        public void GetContent_IsCaseInsensitive_MixedCase()
        {
            var lower = PlaceholderFiles.GetContent("jpg");
            var mixed = PlaceholderFiles.GetContent("JpG");
            Assert.Equal(lower, mixed);
        }

        [Fact]
        public void GetContent_EmptyString_ReturnsEmptyArray()
        {
            var content = PlaceholderFiles.GetContent(string.Empty);
            Assert.Empty(content);
        }

        [Fact]
        public void GetContent_NullString_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PlaceholderFiles.GetContent(null!));
        }

        [Fact]
        public void GetRandomAttachment_ReturnsNonNull()
        {
            var attachment = PlaceholderFiles.GetRandomAttachment();
            Assert.NotNull(attachment);
            Assert.NotNull(attachment.Value.filename);
            Assert.NotEmpty(attachment.Value.content);
        }

        [Fact]
        public void GetRandomAttachment_FilenameHasCorrectExtension()
        {
            var nullableAttachment = PlaceholderFiles.GetRandomAttachment();
            Assert.NotNull(nullableAttachment);
            var attachment = nullableAttachment.Value;
            Assert.StartsWith("attachment.", attachment.filename);
            Assert.True(attachment.filename.EndsWith(".jpg") || attachment.filename.EndsWith(".pdf") || attachment.filename.EndsWith(".tiff"));
        }

        [Fact]
        public void ExtractedText_IsNonEmpty()
        {
            Assert.NotEmpty(PlaceholderFiles.ExtractedText);
        }

        [Fact]
        public void EmlExtractedText_IsNonEmpty()
        {
            Assert.NotEmpty(PlaceholderFiles.EmlExtractedText);
        }
    }
}
