using Zipper.Cli.Modules;
using Zipper.Cli.Validation;

namespace Zipper.Cli;

public static class CliValidator
{
    public static bool Validate(ParsedArguments parsed, CliModuleSet modules)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(modules);

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

        // Source-Driven Generation (--input-csv/--directory-template) supplies File Types and
        // the File Count from Source Records, so --type and --count are not required with it.
        bool hasSourceInput = !string.IsNullOrEmpty(parsed.InputCsv) || !string.IsNullOrEmpty(parsed.DirectoryTemplate);

        if (string.IsNullOrEmpty(parsed.FileType) && parsed.FileTypes is null && !modules.LoadFile.LoadfileOnly && !parsed.ProductionSet && !hasSourceInput)
        {
            Console.Error.WriteLine("Error: --type is required.");
            return false;
        }

        if (!parsed.Count.HasValue && !hasSourceInput)
        {
            Console.Error.WriteLine("Error: --count is required.");
            return false;
        }

        if (parsed.Count.HasValue && parsed.Count.Value <= 0)
        {
            Console.Error.WriteLine("Error: --count must be a positive number.");
            return false;
        }

        if (parsed.Count.HasValue && parsed.Count.Value > int.MaxValue - 1)
        {
            Console.Error.WriteLine($"Error: --count must not exceed {int.MaxValue - 1}.");
            return false;
        }

        if (!StandardModeValidator.Validate(parsed) ||
            !ProductionSetValidator.Validate(parsed, modules) ||
            !CrossCuttingValidator.Validate(parsed, modules))
        {
            return false;
        }


        return true;
    }
}
