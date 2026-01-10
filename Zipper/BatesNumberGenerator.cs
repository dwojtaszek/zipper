namespace Zipper;

/// <summary>
/// Configuration for Bates number generation
/// </summary>
public record BatesNumberConfig
{
    /// <summary>
    /// Prefix for Bates numbers (e.g., "CLIENT001")
    /// </summary>
    public string Prefix { get; init; } = "DOC";

    /// <summary>
    /// Starting number for the sequence
    /// </summary>
    public long Start { get; init; } = 1;

    /// <summary>
    /// Number of digits for zero-padding
    /// </summary>
    public int Digits { get; init; } = 8;

    /// <summary>
    /// Increment between consecutive numbers
    /// </summary>
    public long Increment { get; init; } = 1;
}

/// <summary>
/// Generates Bates numbers for legal document identification
/// </summary>
public static class BatesNumberGenerator
{
    /// <summary>
    /// Generates a Bates number with prefix for the given index
    /// </summary>
    /// <param name="config">Bates number configuration</param>
    /// <param name="currentIndex">Zero-based index of the current document</param>
    /// <returns>Formatted Bates number (e.g., "CLIENT00100000001")</returns>
    public static string Generate(BatesNumberConfig config, long currentIndex)
    {
        var number = config.Start + (currentIndex * config.Increment);
        var formattedNumber = number.ToString($"D{config.Digits}");
        return $"{config.Prefix}{formattedNumber}";
    }

    /// <summary>
    /// Generates a Bates number without prefix for the given index
    /// </summary>
    /// <param name="config">Bates number configuration</param>
    /// <param name="currentIndex">Zero-based index of the current document</param>
    /// <returns>Formatted number only (e.g., "00000001")</returns>
    public static string GenerateWithoutPrefix(BatesNumberConfig config, long currentIndex)
    {
        var number = config.Start + (currentIndex * config.Increment);
        return number.ToString($"D{config.Digits}");
    }
}
