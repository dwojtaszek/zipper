// <copyright file="BatesNumberGenerator.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

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
/// Generates Bates numbers for legal document identification.
/// </summary>
public static class BatesNumberGenerator
{
    /// <summary>
    /// Calculates the numeric value for a Bates number at the given index.
    /// </summary>
    /// <param name="config">Bates number configuration.</param>
    /// <param name="currentIndex">Zero-based index of the current document.</param>
    /// <returns>The numeric Bates value.</returns>
    private static long CalculateValue(BatesNumberConfig config, long currentIndex)
    {
        return config.Start + (currentIndex * config.Increment);
    }

    /// <summary>
    /// Generates a Bates number with prefix for the given index.
    /// </summary>
    /// <param name="config">Bates number configuration.</param>
    /// <param name="currentIndex">Zero-based index of the current document.</param>
    /// <returns>Formatted Bates number (e.g., "CLIENT00100000001").</returns>
    public static string Generate(BatesNumberConfig config, long currentIndex)
    {
        var number = CalculateValue(config, currentIndex);
        var formattedNumber = number.ToString($"D{config.Digits}");
        return $"{config.Prefix}{formattedNumber}";
    }

    /// <summary>
    /// Generates a Bates number without prefix for the given index.
    /// </summary>
    /// <param name="config">Bates number configuration.</param>
    /// <param name="currentIndex">Zero-based index of the current document.</param>
    /// <returns>Formatted number only (e.g., "00000001").</returns>
    public static string GenerateWithoutPrefix(BatesNumberConfig config, long currentIndex)
    {
        return CalculateValue(config, currentIndex).ToString($"D{config.Digits}");
    }
}
