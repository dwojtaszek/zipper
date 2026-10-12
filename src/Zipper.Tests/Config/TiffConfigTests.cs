using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

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

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void ShouldIncludePageCount_MixedFileTypes_ReturnsExpected(bool includeTiff, bool hasRange, bool expected)
    {
        // Arrange
        var tiff = hasRange ? new TiffConfig { PageRange = (1, 10) } : new TiffConfig();
        var ratios = new List<FileTypeRatio>
        {
            new() { Type = "pdf", Weight = 50 },
            new() { Type = includeTiff ? "tiff" : "eml", Weight = 50 },
        };
        var output = new OutputConfig
        {
            FileType = "pdf",
            FileCount = 100,
            FileTypeRatios = ratios,
            FileTypePlan = new FileTypePlan(ratios, 100),
        };

        // Assert
        Assert.True(output.IsMixedFileTypes);
        Assert.Equal(expected, tiff.ShouldIncludePageCount(output));
    }

    [Theory]
    [InlineData("tiff", true, true)]
    [InlineData("tiff", false, false)]
    [InlineData("eml", true, false)]
    public void ShouldIncludePageCount_SourceFileTypes_ReturnsExpected(string secondType, bool hasRange, bool expected)
    {
        // Arrange
        var tiff = hasRange ? new TiffConfig { PageRange = (1, 10) } : new TiffConfig();
        var output = new OutputConfig
        {
            FileType = "pdf",
            SourceFileTypes = new[] { "pdf", secondType },
        };

        // Assert
        Assert.True(output.IsMixedFileTypes);
        Assert.Equal(expected, tiff.ShouldIncludePageCount(output));
    }
}
