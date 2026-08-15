using Zipper.Cli.Modules;

namespace Zipper.Cli;

/// <summary>
/// Cross-domain CLI validation that no single domain module owns: required-flag gates
/// (--type/--count) plus the Standard / Production Set / cross-domain conflict checks.
/// Runs after parse, before any TryBuild. Successor of CliValidator + the three mode
/// validators (StandardModeValidator, ProductionSetValidator, CrossCuttingValidator).
/// Comparison-mode validation lives in ComparisonModule — it short-circuits before this
/// ever runs (REQ-179).
/// </summary>
internal static class CrossCuttingRules
{
    public static bool Validate(CliModuleSet modules)
    {
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

        if (!ValidateStandardMode(modules) ||
            !ValidateProductionSet(modules) ||
            !ValidateCrossCutting(modules))
        {
            return false;
        }

        return true;
    }

    private static bool ValidateStandardMode(CliModuleSet modules)
    {
        if (string.IsNullOrEmpty(modules.Output.TargetZipSize))
        {
            return true;
        }

        if (!modules.Output.Count.HasValue && !modules.SourceInput.HasSourceInput)
        {
            Console.Error.WriteLine("Error: --target-zip-size requires --count to be specified.");
            return false;
        }

        return true;
    }

    private static bool ValidateProductionSet(CliModuleSet modules)
    {
        if (modules.Production.ProductionSet && modules.LoadFile.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --production-set conflicts with --loadfile-only.");
            return false;
        }

        if (modules.Production.ProductionSet && !modules.Bates.HasBatesPrefix)
        {
            Console.Error.WriteLine("Error: --production-set requires --bates-prefix.");
            return false;
        }

        if (modules.Production.RedactedProduction && modules.LoadFile.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --redacted-production conflicts with --loadfile-only.");
            return false;
        }

        return true;
    }

    private static bool ValidateCrossCutting(CliModuleSet modules)
    {
        var fileTypes = modules.Output.FileTypes;
        if (fileTypes is not null && modules.LoadFile.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --types is not supported with --loadfile-only.");
            return false;
        }

        if (fileTypes is not null && modules.Metadata.HasColumnProfile)
        {
            Console.Error.WriteLine("Error: --types is not supported with --column-profile. Use --type for profile-driven generation.");
            return false;
        }

        return ValidateSourcePathMode(modules) && ValidateSourceInput(modules) && ValidateColumnProfile(modules);
    }

    private static bool ValidateSourcePathMode(CliModuleSet modules)
    {
        if (modules.Production.SourcePathMode is null)
        {
            return true;
        }

        if (!modules.Production.ProductionSet)
        {
            Console.Error.WriteLine("Error: --source-path-mode requires --production-set.");
            return false;
        }

        if (!modules.SourceInput.HasSourceInput)
        {
            Console.Error.WriteLine("Error: --source-path-mode requires --input-csv or --directory-template.");
            return false;
        }

        return true;
    }

    private static bool ValidateSourceInput(CliModuleSet modules)
    {
        if (modules.SourceInput.HasSourceInput && !string.IsNullOrEmpty(modules.Output.FileType))
        {
            Console.Error.WriteLine("Error: --type cannot be used with --input-csv/--directory-template. File Types come from the Source Records.");
            return false;
        }

        if (modules.SourceInput.HasSourceInput && modules.Output.FileTypes is not null)
        {
            Console.Error.WriteLine("Error: --types cannot be used with --input-csv/--directory-template. File Types come from the Source Records.");
            return false;
        }

        return true;
    }

    private static bool ValidateColumnProfile(CliModuleSet modules)
    {
        if (modules.Metadata.HasColumnProfile && modules.Production.ProductionSet)
        {
            Console.Error.WriteLine("Error: --column-profile is not supported with --production-set.");
            return false;
        }

        return true;
    }
}
