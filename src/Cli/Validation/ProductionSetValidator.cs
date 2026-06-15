namespace Zipper.Cli.Validation;

internal static class ProductionSetValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        return ValidateDependencies(parsed) &&
               ValidateParameters(parsed) &&
               ValidatePrefix(parsed);
    }

    private static bool ValidateDependencies(ParsedArguments parsed)
    {
        if (parsed.ProductionSet)
        {
            if (parsed.LoadfileOnly)
            {
                Console.Error.WriteLine("Error: --production-set conflicts with --loadfile-only.");
                return false;
            }

            if (string.IsNullOrEmpty(parsed.BatesPrefix))
            {
                Console.Error.WriteLine("Error: --production-set requires --bates-prefix.");
                return false;
            }

            if (parsed.VolumeSize.HasValue && parsed.VolumeSize.Value < 1)
            {
                Console.Error.WriteLine("Error: --volume-size must be at least 1.");
                return false;
            }
        }

        if (parsed.ProductionZip && !parsed.ProductionSet)
        {
            Console.Error.WriteLine("Error: --production-zip requires --production-set.");
            return false;
        }

        if (parsed.VolumeSize.HasValue && !parsed.ProductionSet)
        {
            Console.Error.WriteLine("Error: --volume-size requires --production-set.");
            return false;
        }

        return true;
    }

    private static bool ValidateParameters(ParsedArguments parsed)
    {
        if (parsed.BatesStart.HasValue && parsed.BatesStart.Value < 0)
        {
            Console.Error.WriteLine("Error: Bates start number must be non-negative.");
            return false;
        }

        if (parsed.BatesDigits.HasValue && (parsed.BatesDigits.Value < 1 || parsed.BatesDigits.Value > 20))
        {
            Console.Error.WriteLine("Error: Bates digits must be between 1 and 20.");
            return false;
        }

        return true;
    }

    private static bool ValidatePrefix(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.BatesPrefix))
        {
            if (parsed.BatesPrefix.Contains('/', StringComparison.Ordinal) || parsed.BatesPrefix.Contains('\\', StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Error: --bates-prefix must not contain path separators.");
                return false;
            }

            if (string.Equals(parsed.BatesPrefix, "..", StringComparison.Ordinal) || parsed.BatesPrefix.Contains("../", StringComparison.Ordinal) || parsed.BatesPrefix.Contains("..\\", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Error: --bates-prefix must not contain directory traversal sequences.");
                return false;
            }

            if (!parsed.BatesPrefix.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
            {
                Console.Error.WriteLine("Error: --bates-prefix must only contain letters, digits, underscores, and hyphens.");
                return false;
            }
        }

        return true;
    }
}
