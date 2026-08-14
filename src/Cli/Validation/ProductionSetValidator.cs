using Zipper.Cli.Modules;

namespace Zipper.Cli.Validation;

internal static class ProductionSetValidator
{
    public static bool Validate(CliModuleSet modules)
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
}
