using Xunit;

namespace Zipper
{
    public class ContentTypeHelperTests
    {
        [Theory]
        [InlineData(".pdf", "application/pdf")]
        [InlineData(".doc", "application/msword")]
        [InlineData(".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
        [InlineData(".xls", "application/vnd.ms-excel")]
        [InlineData(".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
        [InlineData(".txt", "text/plain")]
        [InlineData(".jpg", "image/jpeg")]
        [InlineData(".jpeg", "image/jpeg")]
        [InlineData(".png", "image/png")]
        [InlineData(".gif", "image/gif")]
        [InlineData(".eml", "message/rfc822")]
        [InlineData(".tiff", "image/tiff")]
        [InlineData(".tif", "image/tiff")]
        [InlineData(".unknown", "application/octet-stream")]
        [InlineData("", "application/octet-stream")]
        public void GetContentTypeForExtension_ReturnsExpectedType(string extension, string expectedType)
        {
            var result = ContentTypeHelper.GetContentTypeForExtension(extension);
            Assert.Equal(expectedType, result);
        }

        [Theory]
        [InlineData(".DOCX")]
        [InlineData(".PDF")]
        [InlineData(".EML")]
        [InlineData(".TIF")]
        public void GetContentTypeForExtension_IsCaseInsensitive(string extension)
        {
            var result = ContentTypeHelper.GetContentTypeForExtension(extension);
            Assert.NotEqual("application/octet-stream", result);
        }
    }
}
