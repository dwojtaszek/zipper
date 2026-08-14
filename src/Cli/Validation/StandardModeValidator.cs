using Zipper.Cli.Modules;

namespace Zipper.Cli.Validation;

internal static class StandardModeValidator
{
    public static bool Validate(CliModuleSet modules)
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
}
