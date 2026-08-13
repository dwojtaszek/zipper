using Zipper.Cli.Modules;

namespace Zipper.Cli.Validation;

internal static class ProductionSetValidator
{
    public static bool Validate(ParsedArguments parsed, CliModuleSet modules)
    {
        return ValidateDependencies(parsed, modules) && ValidateRollingConfig(parsed);
    }

    private static bool ValidateDependencies(ParsedArguments parsed, CliModuleSet modules)
    {
        if (parsed.ProductionSet)
        {
            if (modules.LoadFile.LoadfileOnly)
            {
                Console.Error.WriteLine("Error: --production-set conflicts with --loadfile-only.");
                return false;
            }

            if (!modules.Bates.HasBatesPrefix)
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

        if (parsed.RedactedProduction && modules.LoadFile.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --redacted-production conflicts with --loadfile-only.");
            return false;
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

        if (parsed.RedactedProduction && !parsed.ProductionSet)
        {
            Console.Error.WriteLine("Error: --redacted-production requires --production-set.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.WithheldNativePolicy))
        {
            if (!parsed.RedactedProduction)
            {
                Console.Error.WriteLine("Error: --withheld-native-policy requires --redacted-production.");
                return false;
            }

            var policy = parsed.WithheldNativePolicy.ToLowerInvariant();
            if (policy != "keep-native" && policy != "omit-native-path" && policy != "replace-with-placeholder")
            {
                Console.Error.WriteLine("Error: --withheld-native-policy must be 'keep-native', 'omit-native-path', or 'replace-with-placeholder'.");
                return false;
            }
        }

        return true;
    }

    private static bool ValidateRollingConfig(ParsedArguments parsed)
    {
        if (parsed.ProductionSet)
        {
            if (parsed.RollingCount <= 0)
            {
                Console.Error.WriteLine("Error: --rolling-count must be a positive number.");
                return false;
            }

            if (!string.IsNullOrEmpty(parsed.RollingBatesMode))
            {
                var mode = parsed.RollingBatesMode.ToLowerInvariant();
                if (mode != "continuous" && mode != "restart")
                {
                    Console.Error.WriteLine("Error: --rolling-bates-mode must be 'continuous' or 'restart'.");
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(parsed.SourcePathMode))
            {
                var pathMode = parsed.SourcePathMode.ToLowerInvariant();
                if (pathMode is not ("bates" or "preserve" or "originals"))
                {
                    Console.Error.WriteLine("Error: --source-path-mode must be 'bates', 'preserve', or 'originals'.");
                    return false;
                }
            }

            // Parse and validate production IDs
            var prodIds = GenerateProductionIds(parsed.ProductionId, parsed.RollingCount);
            if (prodIds.Count != parsed.RollingCount)
            {
                Console.Error.WriteLine("Error: Number of production IDs must match rolling count.");
                return false;
            }

            if (prodIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != prodIds.Count)
            {
                Console.Error.WriteLine("Error: Duplicate production IDs are not allowed.");
                return false;
            }

            if (prodIds.Any(string.IsNullOrWhiteSpace))
            {
                Console.Error.WriteLine("Error: Production ID cannot be empty.");
                return false;
            }
        }

        return true;
    }

    public static List<string> GenerateProductionIds(string? baseId, int count)
    {
        if (string.IsNullOrEmpty(baseId))
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            if (count == 1)
            {
                return new List<string> { $"PRODUCTION_{timestamp}" };
            }
            var list = new List<string>();
            for (int i = 1; i <= count; i++)
            {
                list.Add($"PRODUCTION_{timestamp}_{i:D3}");
            }
            return list;
        }

        if (baseId.Contains(',', StringComparison.Ordinal))
        {
            return baseId.Split(',').Select(id => id.Trim()).ToList();
        }

        if (count == 1)
        {
            return new List<string> { baseId };
        }

        var result = new List<string> { baseId };
        int digitCount = 0;
        while (digitCount < baseId.Length && char.IsDigit(baseId[baseId.Length - 1 - digitCount]))
        {
            digitCount++;
        }

        if (digitCount > 0)
        {
            var prefix = baseId[..^digitCount];
            var numberStr = baseId[^digitCount..];
            var width = numberStr.Length;
            if (long.TryParse(numberStr, System.Globalization.CultureInfo.InvariantCulture, out var startNumber))
            {
                for (int i = 1; i < count; i++)
                {
                    var nextNum = startNumber + i;
                    result.Add($"{prefix}{nextNum.ToString($"D{width}", System.Globalization.CultureInfo.InvariantCulture)}");
                }
                return result;
            }
        }

        for (int i = 2; i <= count; i++)
        {
            result.Add($"{baseId}_{i}");
        }
        return result;
    }

}
