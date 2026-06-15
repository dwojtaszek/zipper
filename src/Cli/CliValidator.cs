using Zipper.Cli.Validation;

namespace Zipper.Cli;

public static class CliValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        ArgumentNullException.ThrowIfNull(parsed);

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

        if (parsed.OutputDirectory == null)
        {
            Console.Error.WriteLine("Error: --output-path is required or was invalid.");
            return false;
        }

        return true;
    }

    internal static bool IsValidStrictDelimiter(string value) => CrossCuttingValidator.IsValidStrictDelimiter(value);

    internal static bool IsValidChaosAmount(string value) => CrossCuttingValidator.IsValidChaosAmount(value);
}
