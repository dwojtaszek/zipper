using System.Globalization;
using Zipper.Config;

namespace Zipper;

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
        PageSuffix = config.PageSuffix;
        _currentIndex = 0;
    }

    /// <summary>True when page-level Bates Numbers (see <see cref="FormatPageBates"/>) apply to every document (REQ-239).</summary>
    public bool PageSuffix { get; }

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

        if (config.Increment < 1)
        {
            errorMessage = "Bates increment must be at least 1.";
            return false;
        }

        if (!ValidatePrefix(config.Prefix, out errorMessage))
        {
            return false;
        }

        sequence = new BatesSequence(config);
        return true;
    }

    internal static bool ValidatePrefix(string? prefix, out string errorMessage)
    {
        errorMessage = string.Empty;
        if (!string.IsNullOrEmpty(prefix))
        {
            if (string.Equals(prefix, "..", StringComparison.Ordinal) || prefix.Contains("../", StringComparison.Ordinal) || prefix.Contains("..\\", StringComparison.Ordinal))
            {
                errorMessage = "--bates-prefix must not contain directory traversal sequences.";
                return false;
            }

            if (prefix.Contains('/', StringComparison.Ordinal) || prefix.Contains('\\', StringComparison.Ordinal))
            {
                errorMessage = "--bates-prefix must not contain path separators.";
                return false;
            }

            if (!prefix.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
            {
                errorMessage = "--bates-prefix must only contain letters, digits, underscores, and hyphens.";
                return false;
            }
        }

        return true;
    }

    public BatesNumber Next()
    {
        var index = Interlocked.Increment(ref _currentIndex) - 1;
        return Format(index);
    }

    public BatesNumber Format(long index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Index must be non-negative.");
        }

        checked
        {
            var number = _config.Start + (index * _config.Increment);
            var formattedNumber = number.ToString($"D{_config.Digits}", CultureInfo.InvariantCulture);
            return new BatesNumber(_config.Prefix ?? string.Empty, formattedNumber);
        }
    }

    /// <summary>
    /// Builds the page-level Bates Number for one page of a document (REQ-239): the document Bates
    /// Number plus a page suffix of at least three digits (more past page 999), for example <c>ABC00000001_001</c>.
    /// </summary>
    public static string FormatPageBates(string documentBates, int pageNumber)
    {
        if (pageNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be at least 1.");
        }

        return $"{documentBates}_{pageNumber:D3}";
    }
}
