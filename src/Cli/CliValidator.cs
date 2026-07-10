using Zipper.Cli.Validation;

namespace Zipper.Cli;

public static class CliValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        bool isComparisonMode = !string.IsNullOrEmpty(parsed.CompareProductionManifests);
        if (!isComparisonMode)
        {
            if (!string.IsNullOrEmpty(parsed.ComparisonMode) || !string.IsNullOrEmpty(parsed.ComparisonOutput))
            {
                Console.Error.WriteLine("Error: --comparison-mode and --comparison-output require --compare-production-manifests to be specified.");
                return false;
            }
        }

        if (isComparisonMode)
        {
            if (string.IsNullOrEmpty(parsed.ComparisonMode))
            {
                Console.Error.WriteLine("Error: --comparison-mode is required when using --compare-production-manifests.");
                return false;
            }

            if (!string.Equals(parsed.ComparisonMode, "replacement", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parsed.ComparisonMode, "supplemental", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parsed.ComparisonMode, "reproduction", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Error: --comparison-mode must be 'replacement', 'supplemental', or 'reproduction'.");
                return false;
            }

            if (string.IsNullOrEmpty(parsed.ComparisonOutput))
            {
                Console.Error.WriteLine("Error: --comparison-output is required when using --compare-production-manifests.");
                return false;
            }

            return true;
        }

        if (string.IsNullOrEmpty(parsed.FileType) && !parsed.LoadfileOnly && !parsed.ProductionSet)
        {
            Console.Error.WriteLine("Error: --type is required.");
            return false;
        }

        if (!parsed.Count.HasValue)
        {
            Console.Error.WriteLine("Error: --count is required.");
            return false;
        }

        if (parsed.Count.Value <= 0)
        {
            Console.Error.WriteLine("Error: --count must be a positive number.");
            return false;
        }

        if (parsed.Count.Value > int.MaxValue - 1)
        {
            Console.Error.WriteLine($"Error: --count must not exceed {int.MaxValue - 1}.");
            return false;
        }

        if (!StandardModeValidator.Validate(parsed) ||
            !LoadfileOnlyValidator.Validate(parsed) ||
            !ProductionSetValidator.Validate(parsed) ||
            !CrossCuttingValidator.Validate(parsed))
        {
            return false;
        }


        return true;
    }

    internal static bool IsValidStrictDelimiter(string value) => CrossCuttingValidator.IsValidStrictDelimiter(value);

    internal static bool IsValidChaosAmount(string value) => CrossCuttingValidator.IsValidChaosAmount(value);
}
