using Xunit;

namespace Zipper.Tests;

public class ChaosSamplerTests
{
    [Fact]
    public void Constructor_TotalLinesExceedsIntMax_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ChaosSampler((long)int.MaxValue + 1, "1%", new Random(42)));
        Assert.Contains("Chaos Engine does not support load files larger than Int32.MaxValue lines", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_TotalLinesZero_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ChaosSampler(0, "1%", new Random(42)));
        Assert.Contains("Chaos Engine requires a positive totalLines count", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseChaosAmount_Percentage_ReturnsCorrectCount()
    {
        int count = ChaosSampler.ParseChaosAmount("10%", 100);
        Assert.Equal(10, count);
    }

    [Fact]
    public void ParseChaosAmount_ExactCount_ReturnsCorrectCount()
    {
        int count = ChaosSampler.ParseChaosAmount("5", 100);
        Assert.Equal(5, count);
    }

    [Fact]
    public void ParseChaosAmount_NullOrEmpty_DefaultsToOnePercent()
    {
        int count = ChaosSampler.ParseChaosAmount(null, 100);
        Assert.Equal(1, count);
    }

    [Fact]
    public void SelectTargetLines_ReturnsExactUniqueSample()
    {
        var random = new Random(42);
        var selected = ChaosSampler.SelectTargetLines(100, 10, random);
        Assert.Equal(10, selected.Count);
        foreach (var item in selected)
        {
            Assert.True(item >= 1 && item <= 100);
        }
    }

    [Fact]
    public void SelectTargetLines_ZeroCount_ReturnsEmpty()
    {
        var random = new Random(42);
        var selected = ChaosSampler.SelectTargetLines(100, 0, random);
        Assert.Empty(selected);
    }

    [Fact]
    public void SelectTargetLines_CountExceedsTotal_ClampsToTotal()
    {
        var random = new Random(42);
        var selected = ChaosSampler.SelectTargetLines(10, 100, random);
        Assert.Equal(10, selected.Count);
    }

    [Fact]
    public void SelectTargetLines_UniformDistribution()
    {
        // Sample 1 from 100, repeat 10,000 times, and ensure max/min frequency ratio is within reasonable bounds
        var random = new Random(42);
        int[] hits = new int[100];
        int iterations = 10000;

        for (int i = 0; i < iterations; i++)
        {
            var selected = ChaosSampler.SelectTargetLines(100, 1, random);
            foreach (var item in selected)
            {
                hits[item - 1]++;
            }
        }

        // Expected hits ~ 100.
        foreach (var count in hits)
        {
            Assert.True(count > 0, "All items should be hit at least once");
            Assert.True(count < 200, "Max frequency should not drastically exceed expected value");
        }
    }

    // Issue 6: Chaos amount parsing is silently wrong
    [Theory]
    [InlineData("10.5")] // No % suffix, should fail int.TryParse
    [InlineData("abc")]  // Invalid string
    [InlineData("  ")]   // Whitespace
    public void ParseChaosAmount_UnparseableString_ThrowsArgumentException(string invalidAmount)
    {
        // Currently falls back to default (1% of totalLines) silently
        // Should throw ArgumentException instead
        Assert.Throws<ArgumentException>(() => ChaosSampler.ParseChaosAmount(invalidAmount, 100));
    }

    [Theory]
    [InlineData("0%")]
    [InlineData("0")]
    public void ParseChaosAmount_Zero_ReturnsZero(string zeroAmount)
    {
        // Currently "0%" returns 1 (Math.Max(1, ...)), "0" returns 0
        // Should be consistent: both should return 0
        int count = ChaosSampler.ParseChaosAmount(zeroAmount, 100);
        Assert.Equal(0, count);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("-1%")]
    [InlineData("-10")]
    public void ParseChaosAmount_Negative_ThrowsArgumentException(string negativeAmount)
    {
        // Currently "-5" is accepted by engine constructor (CLI rejects it)
        // Should throw ArgumentException
        Assert.Throws<ArgumentException>(() => ChaosSampler.ParseChaosAmount(negativeAmount, 100));
    }

    [Fact]
    public void ParseChaosAmount_PercentageWithDecimal_ReturnsCorrectCount()
    {
        // "10.5%" should parse as 10.5% of totalLines
        int count = ChaosSampler.ParseChaosAmount("10.5%", 100);
        Assert.Equal(10, count); // 10.5% of 100 = 10.5, truncated to 10
    }
}
