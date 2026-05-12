using Xunit;

using Zipper.Config;

namespace Zipper.Tests;

public class FileGeneratorFactoryTests
{
    private static FileGenerationRequest MinimalRequest() => new()
    {
        Output = new OutputConfig { FileType = "pdf", FileCount = 1, Concurrency = 1 },
    };

    [Theory]
    [InlineData("pdf", typeof(PlaceholderFileGenerator))]
    [InlineData("jpg", typeof(PlaceholderFileGenerator))]
    [InlineData("eml", typeof(EmlFileGenerator))]
    [InlineData("docx", typeof(OfficeFileGenerator))]
    [InlineData("xlsx", typeof(OfficeFileGenerator))]
    [InlineData("tiff", typeof(TiffFileGenerator))]
    public void Create_KnownType_ReturnsCorrectGenerator(string fileType, Type expectedType)
    {
        var generator = FileGeneratorFactory.Create(fileType, MinimalRequest());

        Assert.NotNull(generator);
        Assert.IsType(expectedType, generator);
    }

    [Theory]
    [InlineData("PDF")]
    [InlineData("Tiff")]
    [InlineData("EML")]
    public void Create_CaseInsensitive_ReturnsGenerator(string fileType)
    {
        var generator = FileGeneratorFactory.Create(fileType, MinimalRequest());
        Assert.NotNull(generator);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("mp3")]
    [InlineData("")]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("bmp")]
    [InlineData("txt")]
    public void Create_UnknownType_ReturnsNull(string fileType)
    {
        var generator = FileGeneratorFactory.Create(fileType, MinimalRequest());
        Assert.Null(generator);
    }

    [Theory]
    [InlineData("pdf", true)]
    [InlineData("jpg", true)]
    [InlineData("eml", true)]
    [InlineData("tiff", true)]
    [InlineData("docx", true)]
    [InlineData("xlsx", true)]
    [InlineData("jpeg", false)]
    [InlineData("png", false)]
    [InlineData("unknown", false)]
    [InlineData("mp3", false)]
    public void IsKnownType_ReturnsExpected(string fileType, bool expected)
    {
        Assert.Equal(expected, FileGeneratorFactory.IsKnownType(fileType));
    }

    [Fact]
    public void Create_PlaceholderTypes_SetCorrectFileType()
    {
        var gen = FileGeneratorFactory.Create("pdf", MinimalRequest()) as PlaceholderFileGenerator;
        Assert.NotNull(gen);
        Assert.Equal("pdf", gen!.FileType);
    }

    [Fact]
    public void Create_EmlGenerator_IsNotPlaceholderBased()
    {
        var gen = FileGeneratorFactory.Create("eml", MinimalRequest());
        Assert.NotNull(gen);
        Assert.False(gen!.IsPlaceholderBased);
    }
}
