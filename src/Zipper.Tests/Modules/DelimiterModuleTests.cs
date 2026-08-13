using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class DelimiterModuleTests
{
    private static bool TryBuild(ParsedArguments parsed, string[] apply, out DelimiterConfig config)
    {
        var module = new DelimiterModule();
        for (int i = 0; i < apply.Length; i += 2)
        {
            Assert.True(module.TryApply(apply[i], apply[i + 1]));
        }
        return module.TryBuild(parsed, out config);
    }

    [Fact]
    public void TryBuild_DatDelimitersCsv_SetsCommaDelimiters()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, new[] { "--dat-delimiters", "csv" }, out var config));

        Assert.Equal(",", config.ColumnDelimiter);
        Assert.Equal("\"", config.QuoteDelimiter);
    }

    [Fact]
    public void TryBuild_DelimiterOverride_OverridesPreset()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, new[] { "--dat-delimiters", "csv", "--delimiter-column", "|" }, out var config));

        Assert.Equal("|", config.ColumnDelimiter);
        Assert.Equal("\"", config.QuoteDelimiter);
    }

    [Fact]
    public void TryBuild_StrictDelimiters_OverrideOldDelimiters()
    {
        var parsed = new ParsedArguments { LoadfileOnly = true };
        Assert.True(TryBuild(parsed, new[] { "--delimiter-column", ",", "--col-delim", "ascii:20" }, out var config));

        Assert.Equal("\u0014", config.ColumnDelimiter);
    }

    [Fact]
    public void TryBuild_LoadfileOnlyEol_SetsEndOfLine()
    {
        var parsed = new ParsedArguments { LoadfileOnly = true };
        Assert.True(TryBuild(parsed, new[] { "--eol", "LF" }, out var config));

        Assert.Equal("LF", config.EndOfLine);
    }

    [Fact]
    public void TryBuild_EolWithProductionSet_ReturnsTrue()
    {
        var parsed = new ParsedArguments { ProductionSet = true };
        Assert.True(TryBuild(parsed, new[] { "--eol", "LF" }, out _));
    }

    [Fact]
    public void TryBuild_EolWithoutLoadfileOnlyOrProductionSet_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, new[] { "--eol", "LF" }, out _));
    }

    [Fact]
    public void TryBuild_InvalidEol_ReturnsFalse()
    {
        var parsed = new ParsedArguments { LoadfileOnly = true };
        Assert.False(TryBuild(parsed, new[] { "--eol", "INVALID" }, out _));
    }

    [Fact]
    public void TryBuild_ValidEol_ReturnsTrue()
    {
        var parsed = new ParsedArguments { LoadfileOnly = true };
        foreach (var eol in new[] { "CRLF", "LF", "CR" })
        {
            Assert.True(TryBuild(parsed, new[] { "--eol", eol }, out _));
        }
    }

    [Fact]
    public void TryBuild_InvalidStrictDelimiter_ReturnsFalse()
    {
        var parsed = new ParsedArguments { LoadfileOnly = true };
        Assert.False(TryBuild(parsed, new[] { "--col-delim", "20" }, out _));
    }

    [Fact]
    public void TryBuild_InvalidDatDelimiters_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, new[] { "--dat-delimiters", "bogus" }, out _));
    }

    [Theory]
    [InlineData("\\t", "\t")]
    [InlineData("\\n", "\n")]
    [InlineData("\\r", "\r")]
    [InlineData("20", "\u0014")]
    [InlineData("254", "\u00fe")]
    [InlineData("|", "|")]
    public void ParseDelimiterArgument_ValidInputs_ReturnsCorrectValue(string input, string expected)
    {
        Assert.Equal(expected, DelimiterModule.ParseDelimiterArgument(input));
    }

    [Fact]
    public void ParseDelimiterArgument_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => DelimiterModule.ParseDelimiterArgument(string.Empty));
    }

    [Theory]
    [InlineData("ascii:20", "\u0014")]
    [InlineData("ascii:254", "\u00fe")]
    [InlineData("char:;", ";")]
    [InlineData("char:|", "|")]
    public void ParseStrictDelimiter_ValidInputs_ReturnsCorrectValue(string input, string expected)
    {
        Assert.Equal(expected, DelimiterModule.ParseStrictDelimiter(input));
    }

    [Fact]
    public void ParseStrictDelimiter_InvalidPrefix_Throws()
    {
        Assert.Throws<ArgumentException>(() => DelimiterModule.ParseStrictDelimiter("20"));
    }

    [Fact]
    public void IsValidStrictDelimiter_ValidAscii_ReturnsTrue()
    {
        Assert.True(DelimiterModule.IsValidStrictDelimiter("ascii:20"));
        Assert.True(DelimiterModule.IsValidStrictDelimiter("ascii:0"));
        Assert.True(DelimiterModule.IsValidStrictDelimiter("ascii:255"));
    }

    [Fact]
    public void IsValidStrictDelimiter_InvalidAscii_ReturnsFalse()
    {
        Assert.False(DelimiterModule.IsValidStrictDelimiter("ascii:256"));
        Assert.False(DelimiterModule.IsValidStrictDelimiter("ascii:-1"));
        Assert.False(DelimiterModule.IsValidStrictDelimiter("ascii:abc"));
    }

    [Fact]
    public void IsValidStrictDelimiter_ValidChar_ReturnsTrue()
    {
        Assert.True(DelimiterModule.IsValidStrictDelimiter("char:;"));
        Assert.True(DelimiterModule.IsValidStrictDelimiter("char:|"));
    }

    [Fact]
    public void IsValidStrictDelimiter_InvalidFormat_ReturnsFalse()
    {
        Assert.False(DelimiterModule.IsValidStrictDelimiter("20"));
        Assert.False(DelimiterModule.IsValidStrictDelimiter(string.Empty));
        Assert.False(DelimiterModule.IsValidStrictDelimiter("ascii:"));
    }

    [Fact]
    public void TryApply_DelimiterArgs_ParsesCorrectly()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, new[] { "--dat-delimiters", "csv", "--delimiter-column", "|", "--delimiter-quote", "~", "--delimiter-newline", " " }, out var config));

        Assert.Equal("|", config.ColumnDelimiter);
        Assert.Equal("~", config.QuoteDelimiter);
        Assert.Equal(" ", config.NewlineDelimiter);
    }

    [Fact]
    public void TryApply_LoadfileOnlyDelimiterArgs_ParsesCorrectly()
    {
        var parsed = new ParsedArguments { LoadfileOnly = true };
        Assert.True(TryBuild(parsed, new[] { "--col-delim", "ascii:20", "--quote-delim", "none", "--multi-delim", "char:;", "--nested-delim", "char:\\" }, out var config));

        Assert.Equal("\u0014", config.ColumnDelimiter);
        Assert.Equal(string.Empty, config.QuoteDelimiter);
        Assert.Equal(";", config.MultiValueDelimiter);
        Assert.Equal("\\", config.NestedValueDelimiter);
    }
}
