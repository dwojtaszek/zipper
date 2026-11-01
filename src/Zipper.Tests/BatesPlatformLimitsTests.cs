using Xunit;
using Zipper.Config;

namespace Zipper.Tests;

public class BatesPlatformLimitsTests
{
    [Fact]
    public void Evaluate_WithDefaultConfigAndCount_ReturnsNoWarnings()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC" }, 1000);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithPrefixAtLimit_ReturnsNoWarnings()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = new string('A', 36) }, 1);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithPrefixOverLimit_ReturnsPrefixWarning()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = new string('A', 37) }, 1);

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

        var warning = Assert.Single(BatesPlatformLimits.Evaluate(config, 1, rollingCount: 2, RollingBatesMode.Continuous));
        Assert.Contains($"'{overLong}' is 40 characters", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithListedPrefixUnusedByStandardRun_DoesNotWarn()
    {
        // A Standard run numbers with the primary prefix only.
        var config = new BatesNumberConfig
        {
            Prefix = "OK",
            Prefixes = new List<string> { "OK", new string('B', 40) },
        };

        Assert.Empty(Standard(config, 1));
    }

    [Fact]
    public void Evaluate_WithDigitsAtNumberLimit_ReturnsNoWarnings()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC", Digits = 16 }, 1);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithDigitsOverNumberLimit_ReturnsNumberWarning()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC", Digits = 17 }, 1);

        var warning = Assert.Single(warnings);
        Assert.Contains("Bates numbers are 17 characters;", warning, StringComparison.Ordinal);
        Assert.DoesNotContain("page suffix", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithPageSuffixLeavingRoomForSuffix_ReturnsNoWarnings()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC", Digits = 12, PageSuffix = true }, 1);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithPageSuffixOverNumberLimit_CountsSuffixInWidth()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC", Digits = 13, PageSuffix = true }, 1);

        var warning = Assert.Single(warnings);
        Assert.Contains("Bates numbers are 17 characters including the page suffix;", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithPageCountPast999AndPageSuffix_CountsFourDigitPageOrdinal()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC", Digits = 12, PageSuffix = true }, 1, maxPagesPerDocument: 1000);

        var warning = Assert.Single(warnings);
        Assert.Contains("Bates numbers are 17 characters including the page suffix;", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithPageCountAt999AndPageSuffix_KeepsThreeDigitPageOrdinal()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC", Digits = 12, PageSuffix = true }, 1, maxPagesPerDocument: 999);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithPageCountPast999WithoutPageSuffix_IgnoresPageOrdinal()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC", Digits = 12 }, 1, maxPagesPerDocument: 1000);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithLastNumberWiderThanDigits_UsesLastNumberWidth()
    {
        // Digits pads to 8, but the second number (10000000000000000) is 17 characters.
        var config = new BatesNumberConfig { Prefix = "DOC", Start = 9999999999999999L, Digits = 8 };

        var warning = Assert.Single(Standard(config, 2));

        Assert.Contains("Bates numbers are 17 characters;", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithCountOverDiscoLimit_ReturnsRangeWarning()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC" }, 30001);

        var warning = Assert.Single(warnings);
        Assert.Contains("Bates range 1 through 30001 spans 30,001 Bates numbers", warning, StringComparison.Ordinal);
        Assert.Contains("30,000", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithCountAtDiscoLimit_ReturnsNoRangeWarning()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC" }, 30000);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithIncrementSpreadingRangeOverDiscoLimit_ReturnsRangeWarning()
    {
        // Increment 2 over 15,001 numbers spans 1 through 30001.
        var config = new BatesNumberConfig { Prefix = "DOC", Increment = 2 };

        var warning = Assert.Single(Standard(config, 15001));

        Assert.Contains("spans 30,001 Bates numbers", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithZeroDocuments_SkipsRangeCheck()
    {
        var warnings = Standard(new BatesNumberConfig { Prefix = "DOC", Increment = 1_000_000 }, 0);

        Assert.Empty(warnings);
    }

    [Fact]
    public void Evaluate_WithListedRollingStartWiderThanLimit_WarnsForThatSetOnly()
    {
        var config = new BatesNumberConfig
        {
            Prefix = "DOC",
            Starts = new List<long> { 1, 9_999_999_999_999_999L },
        };

        Assert.Empty(BatesPlatformLimits.Evaluate(config, 2, rollingCount: 1, RollingBatesMode.Continuous));

        var warning = Assert.Single(BatesPlatformLimits.Evaluate(config, 2, rollingCount: 2, RollingBatesMode.Continuous));
        Assert.Contains("Bates numbers are 17 characters;", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithContinuousSetChainPastWidthLimit_WarnsForLaterSet()
    {
        // Set 1 runs 9999999999999990 through 9999999999999999; set 2 continues at 10000000000000000.
        var config = new BatesNumberConfig { Prefix = "DOC", Start = 9_999_999_999_999_990L };

        Assert.Empty(BatesPlatformLimits.Evaluate(config, 10, rollingCount: 1, RollingBatesMode.Continuous));

        var warning = Assert.Single(BatesPlatformLimits.Evaluate(config, 10, rollingCount: 2, RollingBatesMode.Continuous));
        Assert.Contains("Bates numbers are 17 characters;", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithContinuousSetsEachOverRangeLimit_WarnsForEachSet()
    {
        var config = new BatesNumberConfig { Prefix = "DOC" };

        var warnings = BatesPlatformLimits.Evaluate(config, 30001, rollingCount: 2, RollingBatesMode.Continuous);

        Assert.Equal(2, warnings.Count);
        Assert.Contains("Bates range 1 through 30001", warnings[0], StringComparison.Ordinal);
        Assert.Contains("Bates range 30002 through 60002", warnings[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithRestartSetsSharingPrefixAndRange_WarnsOnce()
    {
        var config = new BatesNumberConfig { Prefix = "DOC" };

        var warning = Assert.Single(BatesPlatformLimits.Evaluate(config, 30001, rollingCount: 3, RollingBatesMode.Restart));

        Assert.Contains("Bates range 1 through 30001", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Evaluate_WithNoRollingSet_ThrowsArgumentOutOfRange()
    {
        var config = new BatesNumberConfig { Prefix = "DOC" };

        Assert.Throws<ArgumentOutOfRangeException>(
            () => BatesPlatformLimits.Evaluate(config, 1, rollingCount: 0, RollingBatesMode.Continuous));
    }

    private static IReadOnlyList<string> Standard(BatesNumberConfig config, long documentCount, int maxPagesPerDocument = 1) =>
        BatesPlatformLimits.Evaluate(config, documentCount, rollingCount: 1, RollingBatesMode.Continuous, maxPagesPerDocument);
}
