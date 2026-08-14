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
        bool hasSourceInput = modules.SourceInput.HasSourceInput;

        if (string.IsNullOrEmpty(modules.Output.FileType) && modules.Output.FileTypes is null && !modules.LoadFile.LoadfileOnly && !modules.Production.ProductionSet && !hasSourceInput)
        {
            Console.Error.WriteLine("Error: --type is required.");
            return false;
        }

        if (!modules.Output.Count.HasValue && !hasSourceInput)
        {
            Console.Error.WriteLine("Error: --count is required.");
            return false;
        }

        if (!StandardModeValidator.Validate(modules) ||
            !ProductionSetValidator.Validate(modules) ||
            !CrossCuttingValidator.Validate(modules))
        {
            return false;
        }


        return true;
    }
}
