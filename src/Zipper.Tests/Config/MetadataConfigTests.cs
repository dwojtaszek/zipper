using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

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

    [Theory]
    [InlineData(true, "pdf", true)]
    [InlineData(false, "pdf", false)]
    [InlineData(false, "eml", true)]
    [InlineData(true, "eml", true)]
    public void ShouldIncludeCompressionColumn_ReturnsExpected(bool withMetadata, string fileType, bool expected)
    {
        // Arrange
        var metadata = new MetadataConfig { WithMetadata = withMetadata };
        var output = new OutputConfig { FileType = fileType };

        // Act & Assert
        Assert.Equal(expected, metadata.ShouldIncludeCompressionColumn(output));
    }

    [Fact]
    public void ShouldIncludeEmlColumns_MixedFileTypesWithEml_ReturnsTrue()
    {
        // Arrange: FileType = "pdf" but mixed files include EML (pdf:50,eml:50)
        var metadata = new MetadataConfig();
        var ratios = new List<FileTypeRatio>
        {
            new() { Type = "pdf", Weight = 50 },
            new() { Type = "eml", Weight = 50 },
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
        Assert.True(metadata.ShouldIncludeEmlColumns(output));
    }

    [Fact]
    public void ShouldIncludeEmlColumns_MixedFileTypesWithoutEml_ReturnsFalse()
    {
        // Arrange: FileType = "pdf" but mixed files do not include EML (pdf:50,tiff:50)
        var metadata = new MetadataConfig();
        var ratios = new List<FileTypeRatio>
        {
            new() { Type = "pdf", Weight = 50 },
            new() { Type = "tiff", Weight = 50 },
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
        Assert.False(metadata.ShouldIncludeEmlColumns(output));
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    public void ShouldIncludeCompressionColumn_MixedFileTypes_ReturnsExpected(bool withMetadata, bool includeEml, bool expected)
    {
        // Arrange
        var metadata = new MetadataConfig { WithMetadata = withMetadata };
        var ratios = new List<FileTypeRatio>
        {
            new() { Type = "pdf", Weight = 50 },
            new() { Type = includeEml ? "eml" : "tiff", Weight = 50 },
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
        Assert.Equal(expected, metadata.ShouldIncludeCompressionColumn(output));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void ShouldIncludeCollectionMetadataColumns_ReturnsExpected(bool withCollectionMetadata, bool expected)
    {
        // Arrange
        var metadata = new MetadataConfig { WithCollectionMetadata = withCollectionMetadata };

        // Act & Assert
        Assert.Equal(expected, metadata.ShouldIncludeCollectionMetadataColumns());
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    public void ShouldIncludeMetadataColumns_MixedFileTypes_ReturnsExpected(bool withMetadata, bool includeEml, bool expected)
    {
        // Arrange
        var metadata = new MetadataConfig { WithMetadata = withMetadata };
        var ratios = new List<FileTypeRatio>
        {
            new() { Type = "pdf", Weight = 50 },
            new() { Type = includeEml ? "eml" : "tiff", Weight = 50 },
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
        Assert.Equal(expected, metadata.ShouldIncludeMetadataColumns(output));
    }

    [Theory]
    [InlineData("eml", true)]
    [InlineData("tiff", false)]
    public void ShouldIncludeEmlColumns_SourceFileTypes_ReturnsExpected(string secondType, bool expected)
    {
        // Arrange
        var metadata = new MetadataConfig();
        var output = new OutputConfig
        {
            FileType = "pdf",
            SourceFileTypes = new[] { "pdf", secondType },
        };

        // Assert
        Assert.True(output.IsMixedFileTypes);
        Assert.Equal(expected, metadata.ShouldIncludeEmlColumns(output));
    }
}
