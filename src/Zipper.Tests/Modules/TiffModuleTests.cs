using Xunit;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class TiffModuleTests
{
    private static TiffConfig Build(string? pageRange)
    {
        var module = new TiffModule();
        if (pageRange is not null)
        {
            Assert.True(module.TryApply("--tiff-pages", pageRange));
        }
        Assert.True(module.TryBuild(out var config));
        return config;
    }

    [Fact]
    public void TryBuild_ValidRange_SetsPageRange()
    {
        var config = Build("1-20");
        Assert.Equal((1, 20), config.PageRange);
    }

    [Fact]
    public void TryBuild_Default_NullPageRange()
    {
        var config = Build(null);
        Assert.Null(config.PageRange);
    }

    [Fact]
    public void TryBuild_InvalidRange_ReturnsFalse()
    {
        var module = new TiffModule();
        Assert.True(module.TryApply("--tiff-pages", "not-a-range"));
        Assert.False(module.TryBuild(out _));
    }

    [Fact]
    public void TryApply_UnknownFlag_ReturnsFalse()
    {
        Assert.False(new TiffModule().TryApply("--unknown-flag", "x"));
    }

    [Theory]
    [InlineData("standard", true)]
    [InlineData("group4", true)]
    [InlineData("uncompressed", true)]
    [InlineData("none", false)]
    public void TryBuild_WithTiffRealism_SetsConfig(string mode, bool expectedRealistic)
    {
        var module = new TiffModule();
        Assert.True(module.TryApply("--tiff-realism", mode));
        Assert.True(module.TryBuild(out var config));
        Assert.Equal(mode, config.RealismMode);
        Assert.Equal(expectedRealistic, config.IsRealistic);
    }

    [Fact]
    public void TryBuild_WithWithTiffRealismFlag_SetsStandardConfig()
    {
        var module = new TiffModule();
        Assert.False(module.TakesValue("--with-tiff-realism"));
        Assert.True(module.TryApply("--with-tiff-realism", null));
        Assert.True(module.TryBuild(out var config));
        Assert.Equal("standard", config.RealismMode);
        Assert.True(config.IsRealistic);
    }

    [Fact]
    public void TryBuild_WithRealisticTiffAlias_SetsStandardConfig()
    {
        var module = new TiffModule();
        Assert.False(module.TakesValue("--realistic-tiff"));
        Assert.True(module.TryApply("--realistic-tiff", null));
        Assert.True(module.TryBuild(out var config));
        Assert.Equal("standard", config.RealismMode);
        Assert.True(config.IsRealistic);
    }

    [Fact]
    public void TryBuild_WithTiffCompression_SetsCompression()
    {
        var module = new TiffModule();
        Assert.True(module.TryApply("--with-tiff-realism", null));
        Assert.True(module.TryApply("--tiff-compression", "uncompressed"));
        Assert.True(module.TryBuild(out var config));
        Assert.Equal("uncompressed", config.Compression);
    }

    [Fact]
    public void TryBuild_RealismWithoutCompression_DefaultsToGroup4()
    {
        var module = new TiffModule();
        Assert.True(module.TryApply("--with-tiff-realism", null));
        Assert.True(module.TryBuild(out var config));
        Assert.Equal("group4", config.Compression);
    }

    [Fact]
    public void TryBuild_UncompressedRealismWithGroup4Compression_ReturnsFalse()
    {
        var module = new TiffModule();
        Assert.True(module.TryApply("--tiff-realism", "uncompressed"));
        Assert.True(module.TryApply("--tiff-compression", "group4"));
        Assert.False(module.TryBuild(out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("none")]
    public void TryBuild_TiffCompressionWithoutRealism_ReturnsFalse(string? mode)
    {
        var module = new TiffModule();
        if (mode is not null)
        {
            Assert.True(module.TryApply("--tiff-realism", mode));
        }
        Assert.True(module.TryApply("--tiff-compression", "uncompressed"));
        Assert.False(module.TryBuild(out _));
    }

    [Fact]
    public void TryBuild_WithTiffRealismFlagBeforeUncompressedMode_KeepsUncompressed()
    {
        var module = new TiffModule();
        Assert.True(module.TryApply("--with-tiff-realism", null));
        Assert.True(module.TryApply("--tiff-realism", "uncompressed"));
        Assert.True(module.TryBuild(out var config));
        Assert.Equal("uncompressed", config.RealismMode);
        Assert.Equal("uncompressed", config.Compression);
    }

    [Fact]
    public void TryBuild_UncompressedModeBeforeWithTiffRealismFlag_KeepsUncompressed()
    {
        var module = new TiffModule();
        Assert.True(module.TryApply("--tiff-realism", "uncompressed"));
        Assert.True(module.TryApply("--with-tiff-realism", null));
        Assert.True(module.TryBuild(out var config));
        Assert.Equal("uncompressed", config.RealismMode);
        Assert.Equal("uncompressed", config.Compression);
    }

    [Fact]
    public void TryApply_InvalidRealismMode_ReturnsFalse()
    {
        var module = new TiffModule();
        Assert.False(module.TryApply("--tiff-realism", "invalid-mode"));
    }

    [Fact]
    public void TryApply_InvalidCompression_ReturnsFalse()
    {
        var module = new TiffModule();
        Assert.False(module.TryApply("--tiff-compression", "deflate"));
    }
}
