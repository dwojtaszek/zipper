using Xunit;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public sealed class DerivedPathTests
{
    [Theory]
    [InlineData("photo.jpeg", "photo.txt")]
    [InlineData("REPORT.PDF", "REPORT.txt")]
    [InlineData("report", "report.txt")]
    [InlineData("report.txt", "report.txt")]
    public void GetTextPath_WithNativeFileName_ReplacesOnlyLastExtension(string nativePath, string expected)
    {
        Assert.Equal(expected, TextPathHelper.GetTextPath(nativePath));
    }

    [Theory]
    [InlineData("DOC00000001.pdf", "DOC00000001.tif")]
    [InlineData("report.eml.eml", "report.eml.tif")]
    [InlineData("scan", "scan.tif")]
    [InlineData("scan.tif", "scan.tif")]
    public void GetImagePath_WithNativeFileName_ReplacesOnlyLastExtension(string nativePath, string expected)
    {
        Assert.Equal(expected, ImagePathHelper.GetImagePath(nativePath));
    }
}
