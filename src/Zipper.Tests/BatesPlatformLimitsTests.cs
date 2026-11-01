using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class BatesPlatformLimitsTests
{
    [Fact]
    public void Evaluate_WithDefaultConfigAndCount_ReturnsNoWarnings()
    {
        var warnings = BatesPlatformLimits.Evaluate(new BatesNumberConfig { Prefix = "DOC" }, 1000);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithPrefixAtLimit_ReturnsNoWarnings()
    {
        var warnings = BatesPlatformLimits.Evaluate(new BatesNumberConfig { Prefix = new string('A', 36) }, null);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithPrefixOverLimit_ReturnsPrefixWarning()
    {
        var warnings = BatesPlatformLimits.Evaluate(new BatesNumberConfig { Prefix = new string('A', 37) }, null);

        var warning = Assert.Single(warnings);
        Assert.Contains("is 37 characters", warning, StringComparison.Ordinal);
        Assert.Contains("36 characters", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithSecondListPrefixOverLimit_WarnsForThatPrefix()
    {
        var overLong = new string('B', 40);
        var config = new BatesNumberConfig
        {
            Prefix = "OK",
            Prefixes = new List<string> { "OK", overLong },
        };

        var warning = Assert.Single(BatesPlatformLimits.Evaluate(config, null));
        Assert.Contains($"'{overLong}' is 40 characters", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithDigitsAtNumberLimit_ReturnsNoWarnings()
    {
        var warnings = BatesPlatformLimits.Evaluate(new BatesNumberConfig { Prefix = "DOC", Digits = 16 }, null);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithDigitsOverNumberLimit_ReturnsNumberWarning()
    {
        var warnings = BatesPlatformLimits.Evaluate(new BatesNumberConfig { Prefix = "DOC", Digits = 17 }, null);

        var warning = Assert.Single(warnings);
        Assert.Contains("Bates numbers are 17 characters;", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("page suffix", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithPageSuffixLeavingRoomForSuffix_ReturnsNoWarnings()
    {
        var warnings = BatesPlatformLimits.Evaluate(new BatesNumberConfig { Prefix = "DOC", Digits = 12, PageSuffix = true }, null);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithPageSuffixOverNumberLimit_CountsSuffixInWidth()
    {
        var warnings = BatesPlatformLimits.Evaluate(new BatesNumberConfig { Prefix = "DOC", Digits = 13, PageSuffix = true }, null);

        var warning = Assert.Single(warnings);
        Assert.Contains("Bates numbers are 17 characters including the page suffix;", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithLastNumberWiderThanDigits_UsesLastNumberWidth()
    {
        // Digits pads to 8, but the second number (10000000000000000) is 17 characters.
        var config = new BatesNumberConfig { Prefix = "DOC", Start = 9999999999999999L, Digits = 8 };

        var warning = Assert.Single(BatesPlatformLimits.Evaluate(config, 2));

        Assert.Contains("Bates numbers are 17 characters;", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithCountOverDiscoLimit_ReturnsRangeWarning()
    {
        var warnings = BatesPlatformLimits.Evaluate(new BatesNumberConfig { Prefix = "DOC" }, 30001);

        var warning = Assert.Single(warnings);
        Assert.Contains("Bates range 1 through 30001 spans 30,001 Bates numbers", warning, StringComparison.Ordinal);
        Assert.Contains("30,000", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithCountAtDiscoLimit_ReturnsNoRangeWarning()
    {
        var warnings = BatesPlatformLimits.Evaluate(new BatesNumberConfig { Prefix = "DOC" }, 30000);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithIncrementSpreadingRangeOverDiscoLimit_ReturnsRangeWarning()
    {
        // Increment 2 over 15,001 numbers spans 1 through 30001.
        var config = new BatesNumberConfig { Prefix = "DOC", Increment = 2 };

        var warning = Assert.Single(BatesPlatformLimits.Evaluate(config, 15001));

        Assert.Contains("spans 30,001 Bates numbers", warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void Evaluate_WithoutPositiveCount_SkipsRangeCheck(long? count)
    {
        var warnings = BatesPlatformLimits.Evaluate(new BatesNumberConfig { Prefix = "DOC", Increment = 1_000_000 }, count);

        Assert.Empty(warnings);
    }
}
