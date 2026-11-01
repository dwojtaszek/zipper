using Xunit;

namespace Zipper.Tests;

public class TiffFileGeneratorTests
{
    [Fact]
    public void Generate_WithPageRangeAndRealismOff_ReturnsPlaceholderBytesAndRequestedPageCount()
    {
        var request = new FileGenerationRequest
        {
            Tiff = new Config.TiffConfig { PageRange = (3, 3) },
        };

        var result = new TiffFileGenerator(request).Generate(new FileWorkItem { Index = 1 }, request);

        Assert.Equal(PlaceholderFiles.GetContent("tiff"), result.Content);
        Assert.Equal(3, result.PageCount);
    }

    [Fact]
    public void Generate_WithRealismAndNoPageRange_EmitsSingleRealisticPage()
    {
        var request = new FileGenerationRequest
        {
            Tiff = new Config.TiffConfig { RealismMode = "group4" },
        };

        var result = new TiffFileGenerator(request).Generate(new FileWorkItem { Index = 7 }, request);

        Assert.NotEqual(PlaceholderFiles.GetContent("tiff"), result.Content);
        Assert.Equal(1, result.PageCount);
        Assert.Equal(0x49, result.Content[0]);
        Assert.Equal(0x49, result.Content[1]);
        Assert.Equal(0x2A, result.Content[2]);
        Assert.Equal(0x00, result.Content[3]);
    }

    [Fact]
    public void Generate_WithRealismAndBatesOverrides_ProducesDistinctPageImages()
    {
        var request = new FileGenerationRequest
        {
            Tiff = new Config.TiffConfig { RealismMode = "group4" },
        };
        var generator = new TiffFileGenerator(request);

        var first = generator.Generate(new FileWorkItem { Index = 1, BatesNumberOverride = "AAA0001" }, request);
        var second = generator.Generate(new FileWorkItem { Index = 2, BatesNumberOverride = "BBB0002" }, request);

        Assert.NotEqual(first.Content, second.Content);
    }

    [Fact]
    public void Generate_WithPageRangeAndRealismOff_DoesNotEvaluateBatesConfig()
    {
        // The endorsement label only feeds realistic emission, so an invalid Bates config must not
        // fail default placeholder output.
        var request = new FileGenerationRequest
        {
            Tiff = new Config.TiffConfig { PageRange = (3, 3) },
            Bates = new Config.BatesNumberConfig { Digits = 0 },
        };

        var result = new TiffFileGenerator(request).Generate(new FileWorkItem { Index = 1 }, request);

        Assert.Equal(PlaceholderFiles.GetContent("tiff"), result.Content);
        Assert.Equal(3, result.PageCount);
    }
}
