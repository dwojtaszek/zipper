using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class FileTypeRatioParserTests
{
    [Fact]
    public void TryParse_ValidInput_ReturnsRatiosInDeclaredOrder()
    {
        var ok = FileTypeRatioParser.TryParse("pdf:50,eml:30,tiff:20", out var ratios, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(3, ratios.Count);
        Assert.Equal("pdf", ratios[0].Type);
        Assert.Equal(50, ratios[0].Weight);
        Assert.Equal("eml", ratios[1].Type);
        Assert.Equal(30, ratios[1].Weight);
        Assert.Equal("tiff", ratios[2].Type);
        Assert.Equal(20, ratios[2].Weight);
    }

    [Fact]
    public void TryParse_UppercaseTypes_NormalizesToLowercase()
    {
        var ok = FileTypeRatioParser.TryParse("PDF:1,EML:1", out var ratios, out _);

        Assert.True(ok);
        Assert.Equal("pdf", ratios[0].Type);
        Assert.Equal("eml", ratios[1].Type);
    }

    [Fact]
    public void TryParse_WhitespaceAroundEntries_TrimsEntries()
    {
        var ok = FileTypeRatioParser.TryParse("  pdf : 50 , eml: 50 ", out var ratios, out _);

        Assert.True(ok);
        Assert.Equal("pdf", ratios[0].Type);
        Assert.Equal(50, ratios[0].Weight);
        Assert.Equal("eml", ratios[1].Type);
    }

    [Fact]
    public void TryParse_SingleEntry_IsAllowed()
    {
        var ok = FileTypeRatioParser.TryParse("pdf:1", out var ratios, out _);

        Assert.True(ok);
        Assert.Single(ratios);
        Assert.Equal("pdf", ratios[0].Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_NullOrEmpty_ReturnsFalse(string? input)
    {
        var ok = FileTypeRatioParser.TryParse(input, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_UnknownType_ReturnsFalse()
    {
        var ok = FileTypeRatioParser.TryParse("pdf:1,exe:1", out _, out var error);

        Assert.False(ok);
        Assert.Contains("exe", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pdf:0")]
    [InlineData("pdf:-1")]
    [InlineData("pdf:+5")]
    [InlineData("pdf:1,eml:0")]
    public void TryParse_NonDigitOrNonPositiveWeight_ReturnsFalse(string input)
    {
        var ok = FileTypeRatioParser.TryParse(input, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("pdf:")]
    [InlineData(":50")]
    [InlineData("pdf:1,,eml:1")]
    [InlineData("pdf:abc")]
    public void TryParse_MalformedEntries_ReturnsFalse(string input)
    {
        var ok = FileTypeRatioParser.TryParse(input, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryParse_DuplicateType_ReturnsFalse()
    {
        var ok = FileTypeRatioParser.TryParse("pdf:1,eml:1,PDF:2", out _, out var error);

        Assert.False(ok);
        Assert.Contains("pdf", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryParse_ExcessiveWeight_ReturnsFalse()
    {
        var ok = FileTypeRatioParser.TryParse("pdf:1000001", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
