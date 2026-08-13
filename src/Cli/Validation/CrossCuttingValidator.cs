using Zipper.Config;
using Zipper.Profiles;

namespace Zipper.Cli.Validation;

internal static class CrossCuttingValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        return ValidateFileTypeMix(parsed) &&
               ValidateSourceInput(parsed) &&
               ValidateFormattingAndProfiles(parsed) &&
               ValidateBates(parsed);
    }

    private static bool ValidateSourceInput(ParsedArguments parsed)
    {
        var hasCsv = !string.IsNullOrEmpty(parsed.InputCsv);
        var hasDirectory = !string.IsNullOrEmpty(parsed.DirectoryTemplate);

        if (!string.IsNullOrEmpty(parsed.SourcePathMode))
        {
            if (!parsed.ProductionSet)
            {
                Console.Error.WriteLine("Error: --source-path-mode requires --production-set.");
                return false;
            }

            if (!hasCsv && !hasDirectory)
            {
                Console.Error.WriteLine("Error: --source-path-mode requires --input-csv or --directory-template.");
                return false;
            }
        }

        if (!hasCsv && !hasDirectory)
        {
            return true;
        }

        if (hasCsv && hasDirectory)
        {
            Console.Error.WriteLine("Error: --input-csv and --directory-template cannot be used together.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.FileType))
        {
            Console.Error.WriteLine("Error: --type cannot be used with --input-csv/--directory-template. File Types come from the Source Records.");
            return false;
        }

        if (parsed.FileTypes is not null)
        {
            Console.Error.WriteLine("Error: --types cannot be used with --input-csv/--directory-template. File Types come from the Source Records.");
            return false;
        }

        var sourcePath = hasCsv ? parsed.InputCsv! : parsed.DirectoryTemplate!;
        if (!PathValidator.IsPathSafe(sourcePath, Directory.GetCurrentDirectory()))
        {
            Console.Error.WriteLine($"Error: Path traversal detected in source input path '{sourcePath}'. Source input must reside within working directory.");
            return false;
        }

        var exists = hasCsv ? File.Exists(sourcePath) : Directory.Exists(sourcePath);
        if (!exists)
        {
            Console.Error.WriteLine(hasCsv
                ? $"Error: Source CSV '{sourcePath}' does not exist."
                : $"Error: Directory template '{sourcePath}' does not exist.");
            return false;
        }

        return true;
    }

    private static bool ValidateFileTypeMix(ParsedArguments parsed)
    {
        if (parsed.FileTypes is null)
        {
            return true;
        }

        if (!string.IsNullOrEmpty(parsed.FileType))
        {
            Console.Error.WriteLine("Error: --type and --types cannot be used together. Use --types for a File Type mix.");
            return false;
        }

        if (parsed.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --types is not supported with --loadfile-only.");
            return false;
        }

        // Rejected because column profiles bypass per-record File Type gating and would
        // silently mislabel mixed rows. Use --type for profile-driven generation instead.
        if (!string.IsNullOrEmpty(parsed.ColumnProfile))
        {
            Console.Error.WriteLine("Error: --types is not supported with --column-profile. Use --type for profile-driven generation.");
            return false;
        }

        if (!Config.FileTypeRatioParser.TryParse(parsed.FileTypes, out _, out var error))
        {
            Console.Error.WriteLine($"Error: {error}");
            return false;
        }

        return true;
    }

    private static bool ValidateFormattingAndProfiles(ParsedArguments parsed)
    {
        return ValidateEncodingAndDistribution(parsed) &&
               ValidateLoadFileFormats(parsed) &&
               ValidateColumnProfile(parsed);
    }

    private static bool ValidateEncodingAndDistribution(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.Encoding) && RequestBuilder.GetEncodingFromName(parsed.Encoding) is null)
        {
            Console.Error.WriteLine($"Error: Invalid encoding '{parsed.Encoding}'. Supported values are UTF-8, UTF-16, ANSI.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.Distribution) && RequestBuilder.GetDistributionFromName(parsed.Distribution) is null)
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
            if (RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat) is null)
            {
                Console.Error.WriteLine("Error: Invalid load file format. Supported values are dat, opt, csv, edrm-xml, xml, concordance.");
                return false;
            }
        }

        if (!string.IsNullOrEmpty(parsed.LoadFileFormats))
        {
            foreach (var fmt in parsed.LoadFileFormats.Split(','))
            {
                if (RequestBuilder.GetLoadFileFormat(fmt.Trim()) is null)
                {
                    Console.Error.WriteLine($"Error: Invalid load file format '{fmt}'. Supported: dat, opt, csv, edrm-xml, xml, concordance.");
                    return false;
                }
            }
        }
        return true;
    }

    private static bool ValidateColumnProfile(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.ColumnProfile) && parsed.ProductionSet)
        {
            Console.Error.WriteLine("Error: --column-profile is not supported with --production-set.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.ColumnProfile) && !ColumnProfileLoader.IsBuiltInProfile(parsed.ColumnProfile))
        {
            if (!PathValidator.IsPathSafe(parsed.ColumnProfile, Directory.GetCurrentDirectory()))
            {
                Console.Error.WriteLine($"Error: Path traversal detected in column profile path '{parsed.ColumnProfile}'. Profile file must reside within working directory.");
                return false;
            }

            if (!File.Exists(parsed.ColumnProfile))
            {
                Console.Error.WriteLine($"Error: Column profile '{parsed.ColumnProfile}' is not a valid built-in profile or file path.\n       Built-in profiles: {string.Join(", ", BuiltInProfiles.ProfileNames)}");
                return false;
            }
        }

        if (parsed.WithMetadata && !string.IsNullOrEmpty(parsed.ColumnProfile))
        {
            Console.Error.WriteLine("Warning: --column-profile takes precedence over --with-metadata. --with-metadata will be ignored.");
            parsed.WithMetadata = false;
        }
        return true;
    }

    private static bool ValidateBates(ParsedArguments parsed)
    {
        if (parsed.BatesPrefix is not null || parsed.BatesStart is not null || parsed.BatesDigits is not null)
        {
            var config = new BatesNumberConfig
            {
                Prefix = parsed.BatesPrefix ?? "DOC",
                Start = parsed.BatesStart ?? 1,
                Digits = parsed.BatesDigits ?? 8
            };
            if (!BatesSequence.TryCreate(config, out _, out var error))
            {
                Console.Error.WriteLine($"Error: {error}");
                return false;
            }
        }
        return true;
    }
}
