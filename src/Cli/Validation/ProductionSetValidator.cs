namespace Zipper.Cli.Validation;

internal static class ProductionSetValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        return ValidateDependencies(parsed) && ValidateRollingConfig(parsed);
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

        if (parsed.RedactedProduction && parsed.LoadfileOnly)
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

            // Validate Bates prefixes and starts lengths if lists are provided
            if (parsed.BatesPrefixes is not null)
            {
                if (parsed.BatesPrefixes.Count > 1 && parsed.BatesPrefixes.Count != parsed.RollingCount)
                {
                    Console.Error.WriteLine("Error: Number of bates prefixes must match rolling count.");
                    return false;
                }
                if (parsed.BatesPrefixes.Any(string.IsNullOrWhiteSpace))
                {
                    Console.Error.WriteLine("Error: Bates prefix cannot be empty or whitespace.");
                    return false;
                }
            }

            if (parsed.BatesStarts is not null && parsed.BatesStarts.Count > 1 && parsed.BatesStarts.Count != parsed.RollingCount)
            {
                Console.Error.WriteLine("Error: Number of bates starts must match rolling count.");
                return false;
            }

            // Calculate bates ranges and check for overlaps if prefixes match
            var ranges = new List<(string Prefix, long Start, long End)>();
            long currentStart = parsed.BatesStart ?? 1;
            long fileCount = parsed.Count ?? 0;

            for (int i = 0; i < parsed.RollingCount; i++)
            {
                string prefix = parsed.BatesPrefixes is not null && parsed.BatesPrefixes.Count > i
                    ? parsed.BatesPrefixes[i]
                    : parsed.BatesPrefix ?? string.Empty;

                long start;
                var mode = parsed.RollingBatesMode?.ToLowerInvariant() ?? "continuous";
                if (mode == "restart")
                {
                    start = parsed.BatesStarts is not null && parsed.BatesStarts.Count > i
                        ? parsed.BatesStarts[i]
                        : parsed.BatesStart ?? 1;
                }
                else // continuous
                {
                    if (parsed.BatesStarts is not null && parsed.BatesStarts.Count > i)
                    {
                        start = parsed.BatesStarts[i];
                    }
                    else
                    {
                        start = currentStart;
                    }
                    currentStart = start + fileCount;
                }

                long end = start + fileCount - 1;
                ranges.Add((prefix, start, end));
            }

            // Check for overlaps when prefixes match in continuous mode
            var modeStr = parsed.RollingBatesMode?.ToLowerInvariant() ?? "continuous";
            if (modeStr == "continuous")
            {
                for (int i = 0; i < ranges.Count; i++)
                {
                    for (int j = i + 1; j < ranges.Count; j++)
                    {
                        if (string.Equals(ranges[i].Prefix, ranges[j].Prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            long maxStart = Math.Max(ranges[i].Start, ranges[j].Start);
                            long minEnd = Math.Min(ranges[i].End, ranges[j].End);
                            if (maxStart <= minEnd)
                            {
                                Console.Error.WriteLine(
                                    $"Error: Bates ranges overlap for prefix '{ranges[i].Prefix}': " +
                                    $"Set {i + 1} ({ranges[i].Start}-{ranges[i].End}) and " +
                                    $"Set {j + 1} ({ranges[j].Start}-{ranges[j].End}).");
                                return false;
                            }
                        }
                    }
                }
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
