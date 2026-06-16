using Xunit;

namespace Zipper.Tests;

public class BatesSequenceTests
{
    [Fact]
    public void FromConfig_ValidConfig_CreatesSequence()
    {
        var config = new BatesNumberConfig { Prefix = "ABC", Start = 1, Digits = 5, Increment = 1 };
        var seq = BatesSequence.FromConfig(config);

        Assert.Equal("ABC00001", seq.Next());
        Assert.Equal("ABC00002", seq.Next());
    }

    [Fact]
    public void FromConfig_NegativeStart_ThrowsArgumentException()
    {
        var config = new BatesNumberConfig { Start = -1 };
        var ex = Assert.Throws<ArgumentException>(() => BatesSequence.FromConfig(config));
        Assert.Contains("must be non-negative", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfig_InvalidDigits_ThrowsArgumentException()
    {
        var config = new BatesNumberConfig { Digits = 0 };
        var ex = Assert.Throws<ArgumentException>(() => BatesSequence.FromConfig(config));
        Assert.Contains("must be between 1 and 20", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfig_InvalidPrefixPathSeparator_ThrowsArgumentException()
    {
        var config = new BatesNumberConfig { Prefix = "ABC/DEF" };
        var ex = Assert.Throws<ArgumentException>(() => BatesSequence.FromConfig(config));
        Assert.Contains("must not contain path separators", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfig_InvalidPrefixInvalidChars_ThrowsArgumentException()
    {
        var config = new BatesNumberConfig { Prefix = "ABC@DEF" };
        var ex = Assert.Throws<ArgumentException>(() => BatesSequence.FromConfig(config));
        Assert.Contains("must only contain letters, digits", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfig_StartExceedsDigits_ThrowsArgumentException()
    {
        var config = new BatesNumberConfig { Start = 100, Digits = 2 };
        var ex = Assert.Throws<ArgumentException>(() => BatesSequence.FromConfig(config));
        Assert.Contains("exceeds the maximum value allowed by the configured digits", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FromConfig_ZeroOrNegativeIncrement_ThrowsArgumentException()
    {
        var config = new BatesNumberConfig { Increment = 0 };
        var ex = Assert.Throws<ArgumentException>(() => BatesSequence.FromConfig(config));
        Assert.Contains("increment must be positive", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Next_Overflow_ThrowsOverflowException()
    {
        var config = new BatesNumberConfig { Start = long.MaxValue, Increment = 1, Digits = 19 };
        var seq = BatesSequence.FromConfig(config);
        seq.Next(); // Returns max value
        Assert.Throws<OverflowException>(() => seq.Next());
    }
}
