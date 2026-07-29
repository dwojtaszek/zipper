using Xunit;
using Zipper.SourceInput;

namespace Zipper.Tests;

public class SourcePathSanitizerTests
{
    [Theory]
    [InlineData("folder/file.pdf", "folder/file.pdf")]
    [InlineData("file.pdf", "file.pdf")]
    [InlineData("a/b/c/d.eml", "a/b/c/d.eml")]
    [InlineData("  folder/file.pdf  ", "folder/file.pdf")]
    public void TryNormalize_ValidRelativePath_ReturnsNormalized(string raw, string expected)
    {
        var ok = SourcePathSanitizer.TryNormalize(raw, out var normalized, out var error);

        Assert.True(ok);
        Assert.Equal(expected, normalized);
        Assert.Null(error);
    }

    [Fact]
    public void TryNormalize_BackslashSeparators_ConvertsToForwardSlash()
    {
        var ok = SourcePathSanitizer.TryNormalize(@"folder\sub\file.pdf", out var normalized, out _);

        Assert.True(ok);
        Assert.Equal("folder/sub/file.pdf", normalized);
    }

    [Fact]
    public void TryNormalize_DuplicateSeparators_Collapses()
    {
        var ok = SourcePathSanitizer.TryNormalize("folder//sub///file.pdf", out var normalized, out _);

        Assert.True(ok);
        Assert.Equal("folder/sub/file.pdf", normalized);
    }

    [Theory]
    [InlineData("../escape.pdf")]
    [InlineData("folder/../../escape.pdf")]
    [InlineData("folder/../escape.pdf")]
    [InlineData("..")]
    public void TryNormalize_ParentTraversal_Rejected(string raw)
    {
        var ok = SourcePathSanitizer.TryNormalize(raw, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("/rooted.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("//server/share/file.pdf")]
    [InlineData(@"\\server\share\file.pdf")]
    public void TryNormalize_RootedOrUnc_Rejected(string raw)
    {
        var ok = SourcePathSanitizer.TryNormalize(raw, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("C:/docs/file.pdf")]
    [InlineData(@"C:\docs\file.pdf")]
    [InlineData("c:file.pdf")]
    public void TryNormalize_DriveLetter_Rejected(string raw)
    {
        var ok = SourcePathSanitizer.TryNormalize(raw, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("./")]
    [InlineData("a/./b.pdf")]
    public void TryNormalize_EmptyOrDotSegments_Rejected(string? raw)
    {
        var ok = SourcePathSanitizer.TryNormalize(raw, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("folder/file?.pdf")]
    [InlineData("folder/fi*le.pdf")]
    [InlineData("folder/fi|le.pdf")]
    [InlineData("folder/fi<le.pdf")]
    [InlineData("folder/fi>le.pdf")]
    [InlineData("folder/fi\"le.pdf")]
    public void TryNormalize_InvalidFilenameCharacters_Rejected(string raw)
    {
        var ok = SourcePathSanitizer.TryNormalize(raw, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryNormalize_ControlCharacters_Rejected()
    {
        var ok = SourcePathSanitizer.TryNormalize("folder/fi\tle.pdf", out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
    }
}
