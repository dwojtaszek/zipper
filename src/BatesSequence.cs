using System.Globalization;

namespace Zipper;

public record BatesNumberConfig
{
    public string Prefix { get; init; } = "DOC";
    public long Start { get; init; } = 1;
    public int Digits { get; init; } = 8;
    public long Increment { get; init; } = 1;
}

public record BatesNumber(string Prefix, string FormattedNumber)
{
    public override string ToString() => $"{Prefix}{FormattedNumber}";
    public string WithoutPrefix() => FormattedNumber;
}

public sealed class BatesSequence
{
    private readonly BatesNumberConfig _config;
    private long _currentIndex;

    private BatesSequence(BatesNumberConfig config)
    {
        _config = config;
        _currentIndex = 0;
    }

    public static BatesSequence FromConfig(BatesNumberConfig config)
    {
        if (!TryCreate(config, out var sequence, out var error))
        {
            throw new ArgumentException(error);
        }
        return sequence;
    }

    public static bool TryCreate(BatesNumberConfig config, out BatesSequence sequence, out string errorMessage)
    {
        sequence = null!;
        errorMessage = string.Empty;

        if (config == null)
        {
            errorMessage = "Config cannot be null.";
            return false;
        }

        if (config.Start < 0)
        {
            errorMessage = "Bates start number must be non-negative.";
            return false;
        }

        if (config.Digits < 1 || config.Digits > 20)
        {
            errorMessage = "Bates digits must be between 1 and 20.";
            return false;
        }

        if (!string.IsNullOrEmpty(config.Prefix))
        {
            if (config.Prefix.Contains('/', StringComparison.Ordinal) || config.Prefix.Contains('\\', StringComparison.Ordinal))
            {
                errorMessage = "--bates-prefix must not contain path separators.";
                return false;
            }

            if (string.Equals(config.Prefix, "..", StringComparison.Ordinal) || config.Prefix.Contains("../", StringComparison.Ordinal) || config.Prefix.Contains("..\\", StringComparison.Ordinal))
            {
                errorMessage = "--bates-prefix must not contain directory traversal sequences.";
                return false;
            }

            if (!config.Prefix.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
            {
                errorMessage = "--bates-prefix must only contain letters, digits, underscores, and hyphens.";
                return false;
            }
        }

        sequence = new BatesSequence(config);
        return true;
    }

    public BatesNumber Next()
    {
        var result = Format(_currentIndex);
        _currentIndex++;
        return result;
    }

    public BatesNumber Format(long index)
    {
        checked
        {
            var number = _config.Start + (index * _config.Increment);
            var formattedNumber = number.ToString($"D{_config.Digits}", CultureInfo.InvariantCulture);
            return new BatesNumber(_config.Prefix ?? string.Empty, formattedNumber);
        }
    }
}
