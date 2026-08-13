using Zipper.Cli.Modules;

namespace Zipper.Cli.Validation;

internal static class CrossCuttingValidator
{
    public static bool Validate(ParsedArguments parsed, CliModuleSet modules)
    {
        return ValidateFileTypeMix(parsed, modules) &&
               ValidateSourceInput(parsed) &&
               ValidateEncodingAndDistribution(parsed) &&
               ValidateColumnProfile(parsed, modules);
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

    private static bool ValidateFileTypeMix(ParsedArguments parsed, CliModuleSet modules)
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

        if (modules.LoadFile.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --types is not supported with --loadfile-only.");
            return false;
        }

        // Rejected because column profiles bypass per-record File Type gating and would
        // silently mislabel mixed rows. Use --type for profile-driven generation instead.
        if (modules.Metadata.HasColumnProfile)
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

    private static bool ValidateColumnProfile(ParsedArguments parsed, CliModuleSet modules)
    {
        if (modules.Metadata.HasColumnProfile && parsed.ProductionSet)
        {
            Console.Error.WriteLine("Error: --column-profile is not supported with --production-set.");
            return false;
        }
        return true;
    }
}
