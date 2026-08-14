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
}
