using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

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

    private static OutputConfig MixedConfig(long count = 10)
    {
        var ratios = new List<FileTypeRatio>
        {
            new() { Type = "pdf", Weight = 1 },
            new() { Type = "eml", Weight = 1 },
        };
        return new OutputConfig
        {
            FileType = "pdf",
            FileCount = count,
            FileTypeRatios = ratios,
            FileTypePlan = new FileTypePlan(ratios, count),
        };
    }

    [Fact]
    public void IsMixedFileTypes_SingleType_ReturnsFalse()
    {
        var config = new OutputConfig { FileType = "eml" };

        Assert.False(config.IsMixedFileTypes);
    }

    [Fact]
    public void IsMixedFileTypes_WithPlan_ReturnsTrue()
    {
        Assert.True(MixedConfig().IsMixedFileTypes);
    }

    [Fact]
    public void HasFileType_SingleType_MatchesRequestType()
    {
        var config = new OutputConfig { FileType = "tiff" };

        Assert.True(config.HasFileType("TIFF"));
        Assert.False(config.HasFileType("eml"));
    }

    [Fact]
    public void HasFileType_Mix_MatchesDeclaredTypes()
    {
        var config = MixedConfig();

        Assert.True(config.HasFileType("pdf"));
        Assert.True(config.HasFileType("EML"));
        Assert.False(config.HasFileType("tiff"));
    }

    [Fact]
    public void ResolveFileType_SingleType_AlwaysReturnsRequestType()
    {
        var config = new OutputConfig { FileType = "docx", FileCount = 10 };

        Assert.Equal("docx", config.ResolveFileType(1));
        Assert.Equal("docx", config.ResolveFileType(10));
    }

    [Fact]
    public void ResolveFileType_Mix_ReturnsPerIndexType()
    {
        var config = MixedConfig(count: 10);

        Assert.Equal("pdf", config.ResolveFileType(1));
        Assert.Equal("pdf", config.ResolveFileType(5));
        Assert.Equal("eml", config.ResolveFileType(6));
        Assert.Equal("eml", config.ResolveFileType(10));
    }

    [Fact]
    public void FileTypeDisplay_SingleType_ReturnsType()
    {
        var config = new OutputConfig { FileType = "pdf" };

        Assert.Equal("pdf", config.FileTypeDisplay);
    }

    [Fact]
    public void FileTypeDisplay_Mix_ReturnsDeclaredPairs()
    {
        var ratios = new List<FileTypeRatio>
        {
            new() { Type = "pdf", Weight = 50 },
            new() { Type = "eml", Weight = 30 },
            new() { Type = "tiff", Weight = 20 },
        };
        var config = new OutputConfig
        {
            FileType = "pdf",
            FileCount = 10,
            FileTypeRatios = ratios,
            FileTypePlan = new FileTypePlan(ratios, 10),
        };

        Assert.Equal("pdf:50,eml:30,tiff:20", config.FileTypeDisplay);
    }
}
