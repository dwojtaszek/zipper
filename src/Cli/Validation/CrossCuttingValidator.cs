using Zipper.Profiles;

namespace Zipper.Cli.Validation;

internal static class CrossCuttingValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        return ValidateFormattingAndProfiles(parsed) &&
               ValidateChaos(parsed) &&
               ValidateDelimiters(parsed);
    }

    private static bool ValidateFormattingAndProfiles(ParsedArguments parsed)
    {
        return ValidateEncodingAndDistribution(parsed) &&
               ValidateLoadFileFormats(parsed) &&
               ValidateTiffPagesRange(parsed) &&
               ValidateColumnProfile(parsed) &&
               ValidateDatDelimiters(parsed);
    }

    private static bool ValidateEncodingAndDistribution(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.Encoding) && RequestBuilder.GetEncodingFromName(parsed.Encoding) == null)
        {
            Console.Error.WriteLine($"Error: Invalid encoding '{parsed.Encoding}'. Supported values are UTF-8, UTF-16, ANSI.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.Distribution) && RequestBuilder.GetDistributionFromName(parsed.Distribution) == null)
        {
            Console.Error.WriteLine($"Error: Invalid distribution '{parsed.Distribution}'. Supported values are proportional, gaussian, exponential.");
            return false;
        }
        return true;
    }

    private static bool ValidateLoadFileFormats(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.LoadFileFormat))
        {
            if (RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat) == null)
            {
                Console.Error.WriteLine("Error: Invalid load file format. Supported values are dat, opt, csv, edrm-xml, xml, concordance.");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.LoadFileFormats))
        {
            foreach (var fmt in parsed.LoadFileFormats.Split(','))
            {
                if (RequestBuilder.GetLoadFileFormat(fmt.Trim()) == null)
                {
                    Console.Error.WriteLine($"Error: Invalid load file format '{fmt}'. Supported: dat, opt, csv, edrm-xml, xml, concordance.");
                    return false;
                }
            }
        }
        return true;
    }

    private static bool ValidateTiffPagesRange(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.TiffPagesRange) && TiffMultiPageGenerator.ParsePageRange(parsed.TiffPagesRange) == null)
        {
            Console.Error.WriteLine("Error: Invalid TIFF pages range. Use format: <min>-<max> (e.g., 1-20).");
            return false;
        }
        return true;
    }

    private static bool ValidateColumnProfile(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.ColumnProfile) && !ColumnProfileLoader.IsBuiltInProfile(parsed.ColumnProfile) && !File.Exists(parsed.ColumnProfile))
        {
            Console.Error.WriteLine($"Error: Column profile '{parsed.ColumnProfile}' is not a valid built-in profile or file path.\n       Built-in profiles: {string.Join(", ", BuiltInProfiles.ProfileNames)}");
            return false;
        }

        if (parsed.WithMetadata && !string.IsNullOrEmpty(parsed.ColumnProfile))
        {
            Console.Error.WriteLine("Warning: --column-profile takes precedence over --with-metadata. --with-metadata will be ignored.");
            parsed.WithMetadata = false;
        }
        return true;
    }

    private static bool ValidateDatDelimiters(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.DatDelimiters))
        {
            var delim = parsed.DatDelimiters.ToLowerInvariant();
            if (delim != "standard" && delim != "csv")
            {
                Console.Error.WriteLine("Error: DAT delimiters must be 'standard' or 'csv'.");
                return false;
            }
        }
        return true;
    }

    private static bool ValidateChaos(ParsedArguments parsed)
    {
        if (parsed.ChaosMode)
        {
            if (!parsed.LoadfileOnly)
            {
                Console.Error.WriteLine("Error: --chaos-mode requires --loadfile-only.");
                return false;
            }

            var fmt = RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat;
            if (fmt != LoadFileFormat.Dat && fmt != LoadFileFormat.Opt)
            {
                Console.Error.WriteLine("Error: --chaos-mode is only supported for dat and opt load file formats.");
                return false;
            }
        }
        else
        {
            if (!string.IsNullOrEmpty(parsed.ChaosAmount))
            {
                Console.Error.WriteLine("Error: --chaos-amount requires --chaos-mode.");
                return false;
            }
            if (!string.IsNullOrEmpty(parsed.ChaosTypes))
            {
                Console.Error.WriteLine("Error: --chaos-types requires --chaos-mode.");
                return false;
            }
            if (!string.IsNullOrEmpty(parsed.ChaosScenario))
            {
                Console.Error.WriteLine("Error: --chaos-scenario requires --chaos-mode.");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.ChaosScenario) && !string.IsNullOrEmpty(parsed.ChaosTypes))
        {
            Console.Error.WriteLine("Error: --chaos-scenario conflicts with --chaos-types. Use one or the other.");
            return false;
        }

        var currentFormat = RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat;

        if (!string.IsNullOrEmpty(parsed.ChaosScenario))
        {
            var scenario = ChaosScenarios.GetByName(parsed.ChaosScenario);
            if (scenario == null)
            {
                Console.Error.WriteLine($"Error: Unknown chaos scenario '{parsed.ChaosScenario}'.\n       Available scenarios: {string.Join(", ", ChaosScenarios.ScenarioNames)}");
                return false;
            }

            if (scenario.RequiredFormat.HasValue && scenario.RequiredFormat.Value != currentFormat)
            {
                Console.Error.WriteLine($"Error: Chaos scenario '{parsed.ChaosScenario}' requires --loadfile-format {scenario.RequiredFormat.Value.ToString().ToLowerInvariant()} but got {currentFormat.ToString().ToLowerInvariant()}.");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.ChaosAmount) && !IsValidChaosAmount(parsed.ChaosAmount))
        {
            Console.Error.WriteLine("Error: --chaos-amount must be a percentage (e.g., '1%') or an exact count (e.g., '500').");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ChaosTypes))
        {
            var validTypes = new HashSet<string>(ChaosAnomalyTypes.ForFormat(currentFormat), StringComparer.OrdinalIgnoreCase);
            foreach (var t in parsed.ChaosTypes.Split(','))
            {
                if (!validTypes.Contains(t.Trim()))
                {
                    Console.Error.WriteLine($"Error: Invalid chaos type '{t.Trim()}'. Valid types for {currentFormat}: {string.Join(", ", validTypes)}");
                    return false;
                }
            }
        }

        return true;
    }

    private static bool ValidateDelimiters(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.Eol))
        {
            var isValid = parsed.Eol.ToUpperInvariant() switch
            {
                "CRLF" or "LF" or "CR" => true,
                _ => false
            };
            if (!isValid)
            {
                Console.Error.WriteLine("Error: --eol must be CRLF, LF, or CR.");
                return false;
            }
        }

        var sArgs = new[] { parsed.ColDelim, parsed.NewlineDelim, parsed.MultiDelim, parsed.NestedDelim };
        var sNames = new[] { "--col-delim", "--newline-delim", "--multi-delim", "--nested-delim" };
        for (int idx = 0; idx < sArgs.Length; idx++)
        {
            if (!string.IsNullOrEmpty(sArgs[idx]) && !IsValidStrictDelimiter(sArgs[idx]!))
            {
                Console.Error.WriteLine($"Error: {sNames[idx]} must use 'ascii:<N>' or 'char:<c>' prefix.");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.QuoteDelim) && !parsed.QuoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase) && !IsValidStrictDelimiter(parsed.QuoteDelim))
        {
            Console.Error.WriteLine("Error: --quote-delim must use 'ascii:<N>', 'char:<c>', or 'none'.");
            return false;
        }

        try
        {
            if (!string.IsNullOrEmpty(parsed.DelimiterColumn)) ParseDelimiterArgument(parsed.DelimiterColumn!);
            if (!string.IsNullOrEmpty(parsed.DelimiterQuote)) ParseDelimiterArgument(parsed.DelimiterQuote!);
            if (!string.IsNullOrEmpty(parsed.DelimiterNewline)) ParseDelimiterArgument(parsed.DelimiterNewline!);
            if (!string.IsNullOrEmpty(parsed.ColDelim)) ParseStrictDelimiter(parsed.ColDelim!);
            if (!string.IsNullOrEmpty(parsed.QuoteDelim)) { if (!parsed.QuoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase)) ParseStrictDelimiter(parsed.QuoteDelim); }
            if (!string.IsNullOrEmpty(parsed.NewlineDelim)) ParseStrictDelimiter(parsed.NewlineDelim!);
            if (!string.IsNullOrEmpty(parsed.MultiDelim)) ParseStrictDelimiter(parsed.MultiDelim!);
            if (!string.IsNullOrEmpty(parsed.NestedDelim)) ParseStrictDelimiter(parsed.NestedDelim!);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return false;
        }

        return true;
    }

    public static bool IsValidStrictDelimiter(string value) => value switch
    {
        _ when value.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase) =>
            int.TryParse(value.Substring(6), System.Globalization.CultureInfo.InvariantCulture, out var code) && code is >= 0 and <= 255,
        _ when value.StartsWith("char:", StringComparison.OrdinalIgnoreCase) => value.Length >= 6,
        _ => false
    };

    public static bool IsValidChaosAmount(string value) => value switch
    {
        _ when value.EndsWith("%", StringComparison.Ordinal) => double.TryParse(value.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture, out var pct) && pct > 0,
        _ => int.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var count) && count > 0
    };

    internal static string ParseDelimiterArgument(string arg)
    {
        if (string.IsNullOrEmpty(arg)) throw new ArgumentException("Delimiter argument cannot be empty.");
        if (string.Equals(arg, "\\t", StringComparison.Ordinal)) return "\t";
        if (string.Equals(arg, "\\n", StringComparison.Ordinal)) return "\n";
        if (string.Equals(arg, "\\r", StringComparison.Ordinal)) return "\r";
        if (string.Equals(arg, "\\r\\n", StringComparison.Ordinal)) return "\r\n";
        if (int.TryParse(arg, System.Globalization.CultureInfo.InvariantCulture, out var asciiCode) && asciiCode >= 0 && asciiCode <= 255) return ((char)asciiCode).ToString();
        if (arg.Length > 1) Console.Error.WriteLine($"Warning: Delimiter argument '{arg}' is longer than 1 character. Using first character: '{arg[0]}'");
        return arg[0].ToString();
    }

    internal static string ParseStrictDelimiter(string arg)
    {
        if (arg.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = arg.Substring(6);
            if (int.TryParse(numPart, System.Globalization.CultureInfo.InvariantCulture, out var code) && code >= 0 && code <= 255) return ((char)code).ToString();
            throw new ArgumentException($"Invalid ASCII code in delimiter: '{arg}'");
        }
        if (arg.StartsWith("char:", StringComparison.OrdinalIgnoreCase))
        {
            var charPart = arg.Substring(5);
            if (charPart.Length >= 1) return charPart[0].ToString();
            throw new ArgumentException($"Missing character in delimiter: '{arg}'");
        }
        throw new ArgumentException($"Delimiter must use 'ascii:<N>' or 'char:<c>' prefix: '{arg}'");
    }
}
