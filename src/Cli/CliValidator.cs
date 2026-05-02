using Zipper.Profiles;

namespace Zipper.Cli;

public static class CliValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        if (parsed == null)
        {
            throw new ArgumentNullException(nameof(parsed));
        }

        if (!ValidateRequired(parsed))
        {
            return false;
        }

        return ValidateOptional(parsed);
    }

    private static bool ValidateRequired(ParsedArguments parsed)
    {
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

        if (parsed.OutputDirectory == null)
        {
            Console.Error.WriteLine("Error: --output-path is required or was invalid.");
            return false;
        }

        return true;
    }

    private static bool ValidateOptional(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.TargetZipSize) && !parsed.Count.HasValue)
        {
            Console.Error.WriteLine("Error: --target-zip-size requires --count to be specified.");
            return false;
        }

        if (parsed.Folders < 1 || parsed.Folders > 100)
        {
            Console.Error.WriteLine("Error: Number of folders must be between 1 and 100.");
            return false;
        }

        if (parsed.AttachmentRate < 0 || parsed.AttachmentRate > 100)
        {
            Console.Error.WriteLine("Error: Attachment rate must be between 0 and 100.");
            return false;
        }

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

        if (!string.IsNullOrEmpty(parsed.TargetZipSize))
        {
            if (RequestBuilder.ParseSize(parsed.TargetZipSize) == null)
            {
                Console.Error.WriteLine("Error: Invalid format for --target-zip-size. Use KB, MB, GB, etc. (e.g., 500MB, 10GB).");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.LoadFileFormat))
        {
            if (RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat) == null)
            {
                Console.Error.WriteLine("Error: Invalid load file format. Supported values are dat, opt, csv, xml, concordance.");
                return false;
            }
        }

        if (parsed.BatesStart.HasValue && parsed.BatesStart.Value < 0)
        {
            Console.Error.WriteLine("Error: Bates start number must be non-negative.");
            return false;
        }

        if (parsed.BatesDigits.HasValue && (parsed.BatesDigits.Value < 1 || parsed.BatesDigits.Value > 20))
        {
            Console.Error.WriteLine("Error: Bates digits must be between 1 and 20.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.BatesPrefix))
        {
            if (parsed.BatesPrefix.Contains('/') || parsed.BatesPrefix.Contains('\\'))
            {
                Console.Error.WriteLine("Error: --bates-prefix must not contain path separators.");
                return false;
            }

            if (parsed.BatesPrefix == ".." || parsed.BatesPrefix.Contains("../") || parsed.BatesPrefix.Contains("..\\"))
            {
                Console.Error.WriteLine("Error: --bates-prefix must not contain directory traversal sequences.");
                return false;
            }

            if (!parsed.BatesPrefix.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '-'))
            {
                Console.Error.WriteLine("Error: --bates-prefix must only contain letters, digits, underscores, and hyphens.");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.TiffPagesRange))
        {
            if (TiffMultiPageGenerator.ParsePageRange(parsed.TiffPagesRange) == null)
            {
                Console.Error.WriteLine("Error: Invalid TIFF pages range. Use format: <min>-<max> (e.g., 1-20).");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.ColumnProfile))
        {
            if (!ColumnProfileLoader.IsBuiltInProfile(parsed.ColumnProfile) && !File.Exists(parsed.ColumnProfile))
            {
                Console.Error.WriteLine($"Error: Column profile '{parsed.ColumnProfile}' is not a valid built-in profile or file path.");
                Console.Error.WriteLine($"       Built-in profiles: {string.Join(", ", BuiltInProfiles.ProfileNames)}");
                return false;
            }
        }

        if (parsed.EmptyPercentage.HasValue && (parsed.EmptyPercentage.Value < 0 || parsed.EmptyPercentage.Value > 100))
        {
            Console.Error.WriteLine("Error: Empty percentage must be between 0 and 100.");
            return false;
        }

        if (parsed.CustodianCount.HasValue && (parsed.CustodianCount.Value < 1 || parsed.CustodianCount.Value > 1000))
        {
            Console.Error.WriteLine("Error: Custodian count must be between 1 and 1000.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.LoadFileFormats))
        {
            var formats = parsed.LoadFileFormats.Split(',');
            foreach (var fmt in formats)
            {
                if (RequestBuilder.GetLoadFileFormat(fmt.Trim()) == null)
                {
                    Console.Error.WriteLine($"Error: Invalid load file format '{fmt}'. Supported: dat, opt, csv, edrm-xml.");
                    return false;
                }
            }
        }

        if (!string.IsNullOrEmpty(parsed.DatDelimiters))
        {
            var delim = parsed.DatDelimiters.ToLowerInvariant();
            if (delim != "standard" && delim != "csv")
            {
                Console.Error.WriteLine("Error: DAT delimiters must be 'standard' or 'csv'.");
                return false;
            }
        }

        if (parsed.WithMetadata && !string.IsNullOrEmpty(parsed.ColumnProfile))
        {
            Console.Error.WriteLine("Warning: --column-profile takes precedence over --with-metadata. --with-metadata will be ignored.");
            parsed.WithMetadata = false;
        }

        var loadfileOnlyArgs = new[] { parsed.Eol, parsed.ColDelim, parsed.QuoteDelim, parsed.NewlineDelim, parsed.MultiDelim, parsed.NestedDelim };
        var loadfileOnlyNames = new[] { "--eol", "--col-delim", "--quote-delim", "--newline-delim", "--multi-delim", "--nested-delim" };
        for (int idx = 0; idx < loadfileOnlyArgs.Length; idx++)
        {
            if (!string.IsNullOrEmpty(loadfileOnlyArgs[idx]) && !parsed.LoadfileOnly)
            {
                Console.Error.WriteLine($"Error: {loadfileOnlyNames[idx]} requires --loadfile-only.");
                return false;
            }
        }

        if (parsed.ChaosMode && !parsed.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --chaos-mode requires --loadfile-only.");
            return false;
        }

        if (parsed.ChaosMode)
        {
            var currentFormat = RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat;
            if (currentFormat != LoadFileFormat.Dat && currentFormat != LoadFileFormat.Opt)
            {
                Console.Error.WriteLine("Error: --chaos-mode is only supported for dat and opt load file formats.");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.ChaosAmount) && !parsed.ChaosMode)
        {
            Console.Error.WriteLine("Error: --chaos-amount requires --chaos-mode.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ChaosTypes) && !parsed.ChaosMode)
        {
            Console.Error.WriteLine("Error: --chaos-types requires --chaos-mode.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ChaosScenario) && !parsed.ChaosMode)
        {
            Console.Error.WriteLine("Error: --chaos-scenario requires --chaos-mode.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ChaosScenario) && !string.IsNullOrEmpty(parsed.ChaosTypes))
        {
            Console.Error.WriteLine("Error: --chaos-scenario conflicts with --chaos-types. Use one or the other.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ChaosScenario))
        {
            var scenario = ChaosScenarios.GetByName(parsed.ChaosScenario);
            if (scenario == null)
            {
                Console.Error.WriteLine($"Error: Unknown chaos scenario '{parsed.ChaosScenario}'.");
                Console.Error.WriteLine($"       Available scenarios: {string.Join(", ", ChaosScenarios.ScenarioNames)}");
                return false;
            }

            var currentFormat = RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat;
            if (scenario.RequiredFormat.HasValue && scenario.RequiredFormat.Value != currentFormat)
            {
                Console.Error.WriteLine($"Error: Chaos scenario '{parsed.ChaosScenario}' requires --loadfile-format {scenario.RequiredFormat.Value.ToString().ToLowerInvariant()} but got {currentFormat.ToString().ToLowerInvariant()}.");
                return false;
            }
        }

        if (parsed.LoadfileOnly && !string.IsNullOrEmpty(parsed.TargetZipSize))
        {
            Console.Error.WriteLine("Error: --loadfile-only conflicts with --target-zip-size.");
            return false;
        }

        if (parsed.LoadfileOnly && parsed.IncludeLoadFile)
        {
            Console.Error.WriteLine("Error: --loadfile-only conflicts with --include-load-file.");
            return false;
        }

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

        if (!string.IsNullOrEmpty(parsed.Eol))
        {
            var eolUpper = parsed.Eol.ToUpperInvariant();
            if (eolUpper != "CRLF" && eolUpper != "LF" && eolUpper != "CR")
            {
                Console.Error.WriteLine("Error: --eol must be CRLF, LF, or CR.");
                return false;
            }
        }

        var strictDelimArgs = new[] { parsed.ColDelim, parsed.NewlineDelim, parsed.MultiDelim, parsed.NestedDelim };
        var strictDelimNames = new[] { "--col-delim", "--newline-delim", "--multi-delim", "--nested-delim" };
        for (int idx = 0; idx < strictDelimArgs.Length; idx++)
        {
            if (!string.IsNullOrEmpty(strictDelimArgs[idx]) && !IsValidStrictDelimiter(strictDelimArgs[idx]!))
            {
                Console.Error.WriteLine($"Error: {strictDelimNames[idx]} must use 'ascii:<N>' or 'char:<c>' prefix.");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.QuoteDelim) &&
            !parsed.QuoteDelim.Equals("none", StringComparison.OrdinalIgnoreCase) &&
            !IsValidStrictDelimiter(parsed.QuoteDelim))
        {
            Console.Error.WriteLine("Error: --quote-delim must use 'ascii:<N>', 'char:<c>', or 'none'.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ChaosAmount))
        {
            if (!IsValidChaosAmount(parsed.ChaosAmount))
            {
                Console.Error.WriteLine("Error: --chaos-amount must be a percentage (e.g., '1%') or an exact count (e.g., '500').");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.ChaosTypes))
        {
            var validDat = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "mixed-delimiters", "quotes", "columns", "eol", "encoding" };
            var validOpt = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "opt-boundary", "opt-columns", "opt-pagecount", "opt-path", "opt-batesid" };
            var format = RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat ?? "dat") ?? LoadFileFormat.Dat;
            var validTypes = format == LoadFileFormat.Opt ? validOpt : validDat;
            var types = parsed.ChaosTypes.Split(',');
            foreach (var t in types)
            {
                if (!validTypes.Contains(t.Trim()))
                {
                    Console.Error.WriteLine($"Error: Invalid chaos type '{t.Trim()}'. Valid types for {format}: {string.Join(", ", validTypes)}");
                    return false;
                }
            }
        }

        if (parsed.LoadfileOnly && !string.IsNullOrEmpty(parsed.Encoding))
        {
            var enc = EncodingHelper.GetEncoding(parsed.Encoding);
            if (enc == null)
            {
                Console.Error.WriteLine($"Error: Invalid encoding '{parsed.Encoding}'. Supported: UTF-8, UTF-16LE, Windows-1252, ASCII.");
                return false;
            }
        }

        return true;
    }

    internal static bool IsValidStrictDelimiter(string value)
    {
        if (value.StartsWith("ascii:", StringComparison.OrdinalIgnoreCase))
        {
            var numPart = value.Substring(6);
            return int.TryParse(numPart, out var code) && code >= 0 && code <= 255;
        }

        if (value.StartsWith("char:", StringComparison.OrdinalIgnoreCase))
        {
            return value.Length >= 6;
        }

        return false;
    }

    internal static bool IsValidChaosAmount(string value)
    {
        if (value.EndsWith('%'))
        {
            return double.TryParse(value.TrimEnd('%'), out var pct) && pct > 0;
        }

        return int.TryParse(value, out var count) && count > 0;
    }
}
