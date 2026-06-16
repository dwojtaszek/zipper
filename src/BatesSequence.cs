namespace Zipper;

/// <summary>
/// Configuration for Bates number generation.
/// </summary>
public record BatesNumberConfig
{
    /// <summary>
    /// Gets prefix for Bates numbers (e.g., "CLIENT001").
    /// </summary>
    public string Prefix { get; init; } = "DOC";

    /// <summary>
    /// Gets starting number for the sequence.
    /// </summary>
    public long Start { get; init; } = 1;

    /// <summary>
    /// Gets number of digits for zero-padding.
    /// </summary>
    public int Digits { get; init; } = 8;

    /// <summary>
    /// Gets increment between consecutive numbers.
    /// </summary>
    public long Increment { get; init; } = 1;
}

/// <summary>
/// A deep module representing a validated sequence of Bates numbers.
/// </summary>
public sealed class BatesSequence
{
    private readonly string prefix;
    private readonly int digits;
    private readonly long increment;
    private long currentValue;
    private bool isOverflowed;

    private BatesSequence(string prefix, long start, int digits, long increment)
    {
        this.prefix = prefix;
        this.currentValue = start;
        this.digits = digits;
        this.increment = increment;
    }

    /// <summary>
    /// Creates a validated BatesSequence from the given configuration.
    /// </summary>
    /// <param name="config">The Bates number configuration.</param>
    /// <returns>A validated BatesSequence.</returns>
    /// <exception cref="ArgumentException">Thrown if the configuration is invalid.</exception>
    public static BatesSequence FromConfig(BatesNumberConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.Start < 0)
        {
            throw new ArgumentException("Bates start number must be non-negative.");
        }

        if (config.Digits < 1 || config.Digits > 20)
        {
            throw new ArgumentException("Bates digits must be between 1 and 20.");
        }

        if (config.Digits < 19 && config.Start >= (long)System.Math.Pow(10, config.Digits))
        {
            throw new ArgumentException("Bates start number exceeds the maximum value allowed by the configured digits.");
        }

        if (config.Increment <= 0)
        {
            throw new ArgumentException("Bates increment must be positive.");
        }

        if (!string.IsNullOrEmpty(config.Prefix))
        {
            if (config.Prefix.Contains('/', StringComparison.Ordinal) || config.Prefix.Contains('\\', StringComparison.Ordinal))
            {
                throw new ArgumentException("Bates prefix must not contain path separators.");
            }

            if (string.Equals(config.Prefix, "..", StringComparison.Ordinal) || config.Prefix.Contains("../", StringComparison.Ordinal) || config.Prefix.Contains("..\\", StringComparison.Ordinal))
            {
                throw new ArgumentException("Bates prefix must not contain directory traversal sequences.");
            }

            if (!config.Prefix.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
            {
                throw new ArgumentException("Bates prefix must only contain letters, digits, underscores, and hyphens.");
            }
        }

        return new BatesSequence(config.Prefix ?? string.Empty, config.Start, config.Digits, config.Increment);
    }

    /// <summary>
    /// Generates the next Bates number in the sequence.
    /// </summary>
    /// <returns>Formatted Bates number (e.g., "CLIENT00100000001").</returns>
    public string Next()
    {
        if (this.isOverflowed)
        {
            throw new OverflowException("Bates sequence overflowed.");
        }

        var formattedNumber = this.currentValue.ToString($"D{this.digits}", System.Globalization.CultureInfo.InvariantCulture);

        try
        {
            checked
            {
                this.currentValue += this.increment;
            }
        }
        catch (OverflowException)
        {
            this.isOverflowed = true;
        }

        return $"{this.prefix}{formattedNumber}";
    }

    /// <summary>
    /// Formats the given numeric value into a Bates number using the sequence's prefix and padding.
    /// </summary>
    public string Format(long value)
    {
        var formattedNumber = value.ToString($"D{this.digits}", System.Globalization.CultureInfo.InvariantCulture);
        return $"{this.prefix}{formattedNumber}";
    }
}
