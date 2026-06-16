namespace Zipper.Cli.Validation;

internal static class ProductionSetValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        return ValidateDependencies(parsed) &&
               ValidateBatesSequence(parsed);
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

    private static bool ValidateBatesSequence(ParsedArguments parsed)
    {
        if (string.IsNullOrEmpty(parsed.BatesPrefix))
        {
            return true; // Validated in dependencies if production-set, otherwise ignored
        }

        var config = new Zipper.BatesNumberConfig
        {
            Prefix = parsed.BatesPrefix,
            Start = parsed.BatesStart ?? 1,
            Digits = parsed.BatesDigits ?? 8,
        };

        try
        {
            Zipper.BatesSequence.FromConfig(config);
            return true;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return false;
        }
    }
}
