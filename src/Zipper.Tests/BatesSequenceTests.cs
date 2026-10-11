using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class BatesSequenceTests
{
    [Theory]
    [InlineData("Generate_WithDefaultConfig_ShouldGenerateCorrectNumber", "DOC", 1L, 8, 1L, 0L, "DOC00000001")]
    [InlineData("Generate_WithCustomPrefix_ShouldIncludePrefix", "CLIENT001", 1L, 8, 1L, 0L, "CLIENT00100000001")]
    [InlineData("Generate_WithMultipleIndices_ShouldIncrementCorrectly", "DOC", 1L, 8, 1L, 0L, "DOC00000001")]
    [InlineData("Generate_WithMultipleIndices_ShouldIncrementCorrectly", "DOC", 1L, 8, 1L, 1L, "DOC00000002")]
    [InlineData("Generate_WithMultipleIndices_ShouldIncrementCorrectly", "DOC", 1L, 8, 1L, 2L, "DOC00000003")]
    [InlineData("Generate_WithCustomStart_ShouldStartFromCorrectNumber", "DOC", 100L, 8, 1L, 0L, "DOC00000100")]
    [InlineData("Generate_WithCustomIncrement_ShouldIncrementByStep", "DOC", 1L, 8, 5L, 0L, "DOC00000001")]
    [InlineData("Generate_WithCustomIncrement_ShouldIncrementByStep", "DOC", 1L, 8, 5L, 1L, "DOC00000006")]
    [InlineData("Generate_WithCustomIncrement_ShouldIncrementByStep", "DOC", 1L, 8, 5L, 2L, "DOC00000011")]
    [InlineData("Generate_WithDifferentDigitCounts_ShouldPadCorrectly", "A", 1L, 4, 1L, 0L, "A0001")]
    [InlineData("Generate_WithDifferentDigitCounts_ShouldPadCorrectly", "B", 1L, 6, 1L, 0L, "B000001")]
    [InlineData("Generate_WithDifferentDigitCounts_ShouldPadCorrectly", "C", 1L, 10, 1L, 0L, "C0000000001")]
    [InlineData("Generate_WithLargeNumber_ShouldHandleCorrectly", "DOC", 999999L, 8, 1L, 0L, "DOC00999999")]
    [InlineData("Generate_WithZeroStart_ShouldGenerateCorrectly", "DOC", 0L, 8, 1L, 0L, "DOC00000000")]
    public void Generate_WithConfigAndIndex_ReturnsExpectedNumber(string scenario, string prefix, long start, int digits, long increment, long index, string expected)
    {
        var config = new BatesNumberConfig
        {
            Prefix = prefix,
            Start = start,
            Digits = digits,
            Increment = increment,
        };

        var result = BatesSequence.FromConfig(config).Format(index).ToString();

        Assert.True(string.Equals(expected, result, StringComparison.Ordinal), $"{scenario}: expected <{expected}> but got <{result}>.");
    }

    [Theory]
    [InlineData("Format_WithDefaultConfig_ShouldGenerateNumberOnly", "DOC", 1L, 8, 1L, 0L, "00000001")]
    [InlineData("Format_WithMultipleIndices_ShouldIncrementCorrectly", "PREFIX", 100L, 6, 10L, 0L, "000100")]
    [InlineData("Format_WithMultipleIndices_ShouldIncrementCorrectly", "PREFIX", 100L, 6, 10L, 1L, "000110")]
    [InlineData("Format_WithMultipleIndices_ShouldIncrementCorrectly", "PREFIX", 100L, 6, 10L, 2L, "000120")]
    public void Format_WithConfigAndIndex_ReturnsNumberOnly(string scenario, string prefix, long start, int digits, long increment, long index, string expected)
    {
        var config = new BatesNumberConfig
        {
            Prefix = prefix,
            Start = start,
            Digits = digits,
            Increment = increment,
        };

        var result = BatesSequence.FromConfig(config).Format(index).WithoutPrefix();

        Assert.True(string.Equals(expected, result, StringComparison.Ordinal), $"{scenario}: expected <{expected}> but got <{result}>.");
    }

    [Fact]
    public void Format_WithNegativeIndex_ShouldThrowArgumentOutOfRangeException()
    {
        var seq = BatesSequence.FromConfig(new BatesNumberConfig());
        Assert.Throws<ArgumentOutOfRangeException>(() => seq.Format(-1));
    }

    [Theory]
    [InlineData("NullConfig", "Config cannot be null.")]
    [InlineData("NegativeStart", "Bates start number must be non-negative.")]
    [InlineData("ZeroDigits", "Bates digits must be between 1 and 20.")]
    [InlineData("TooManyDigits", "Bates digits must be between 1 and 20.")]
    [InlineData("ZeroIncrement", "Bates increment must be at least 1.")]
    [InlineData("PathSeparatorSlash", "--bates-prefix must not contain path separators.")]
    [InlineData("PathSeparatorBackslash", "--bates-prefix must not contain path separators.")]
    [InlineData("DirTraversalDots", "--bates-prefix must not contain directory traversal sequences.")]
    [InlineData("DirTraversalSlash", "--bates-prefix must not contain directory traversal sequences.")]
    [InlineData("DirTraversalBackslash", "--bates-prefix must not contain directory traversal sequences.")]
    [InlineData("InvalidChar", "--bates-prefix must only contain letters, digits, underscores, and hyphens.")]
    public void FromConfig_WithInvalidConfig_ShouldThrowArgumentException(string scenario, string expectedMessage)
    {
        BatesNumberConfig badConfig = scenario switch
        {
            "NullConfig" => null!,
            "NegativeStart" => new BatesNumberConfig { Start = -1 },
            "ZeroDigits" => new BatesNumberConfig { Digits = 0 },
            "TooManyDigits" => new BatesNumberConfig { Digits = 21 },
            "ZeroIncrement" => new BatesNumberConfig { Increment = 0 },
            "PathSeparatorSlash" => new BatesNumberConfig { Prefix = "a/b" },
            "PathSeparatorBackslash" => new BatesNumberConfig { Prefix = "a\\b" },
            "DirTraversalDots" => new BatesNumberConfig { Prefix = ".." },
            "DirTraversalSlash" => new BatesNumberConfig { Prefix = "../a" },
            "DirTraversalBackslash" => new BatesNumberConfig { Prefix = "..\\a" },
            "InvalidChar" => new BatesNumberConfig { Prefix = "a b" },
            _ => throw new System.ArgumentException("Unknown scenario")
        };

        var ex = Assert.Throws<System.ArgumentException>(() => BatesSequence.FromConfig(badConfig));
        Assert.Contains(expectedMessage, ex.Message, StringComparison.Ordinal);
    }
}
