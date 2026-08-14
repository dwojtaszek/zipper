using Zipper.Cli.Modules;

namespace Zipper.Cli.Validation;

internal static class CrossCuttingValidator
{
    public static bool Validate(CliModuleSet modules)
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

        if (modules.Production.SourcePathMode is not null)
        {
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
        }

        if (modules.SourceInput.HasSourceInput)
        {
            if (!string.IsNullOrEmpty(modules.Output.FileType))
            {
                Console.Error.WriteLine("Error: --type cannot be used with --input-csv/--directory-template. File Types come from the Source Records.");
                return false;
            }

            if (modules.Output.FileTypes is not null)
            {
                Console.Error.WriteLine("Error: --types cannot be used with --input-csv/--directory-template. File Types come from the Source Records.");
                return false;
            }
        }

        if (modules.Metadata.HasColumnProfile && modules.Production.ProductionSet)
        {
            Console.Error.WriteLine("Error: --column-profile is not supported with --production-set.");
            return false;
        }

        return true;
    }
}
