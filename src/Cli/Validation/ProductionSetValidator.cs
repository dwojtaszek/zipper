namespace Zipper.Cli.Validation;

internal static class ProductionSetValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        return ValidateDependencies(parsed);
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

        if (parsed.SupplementalProduction)
        {
            if (!parsed.ProductionSet)
            {
                Console.Error.WriteLine("Error: --supplemental-production requires --production-set.");
                return false;
            }

            if (string.IsNullOrEmpty(parsed.PriorManifests))
            {
                Console.Error.WriteLine("Error: --supplemental-production requires --prior-manifest.");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.PriorManifests) && !parsed.SupplementalProduction)
        {
            Console.Error.WriteLine("Error: --prior-manifest requires --supplemental-production.");
            return false;
        }

        if (parsed.SupplementalGapPolicy is not null)
        {
            if (!parsed.SupplementalProduction)
            {
                Console.Error.WriteLine("Error: --supplemental-gap-policy requires --supplemental-production.");
                return false;
            }

            if (!string.Equals(parsed.SupplementalGapPolicy, "reject", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(parsed.SupplementalGapPolicy, "allow", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Error: --supplemental-gap-policy must be 'reject' or 'allow'.");
                return false;
            }
        }

        return true;
    }

}
