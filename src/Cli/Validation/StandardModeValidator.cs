namespace Zipper.Cli.Validation;

internal static class StandardModeValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        if (string.IsNullOrWhiteSpace(parsed.OutputPathStr))
        {
            Console.Error.WriteLine("Error: Output path is required.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.FileType) && !FileGeneratorFactory.IsKnownType(parsed.FileType))
        {
            Console.Error.WriteLine($"Error: Unsupported file type '{parsed.FileType}'. Supported types: pdf, jpg, tiff, eml, docx, xlsx.");
            return false;
        }

        var hasSourceInput = !string.IsNullOrEmpty(parsed.InputCsv) || !string.IsNullOrEmpty(parsed.DirectoryTemplate);

        if (!string.IsNullOrEmpty(parsed.TargetZipSize) && !parsed.Count.HasValue && !hasSourceInput)
        {
            Console.Error.WriteLine("Error: --target-zip-size requires --count to be specified.");
            return false;
        }

        if (parsed.Folders < 1 || parsed.Folders > 100)
        {
            Console.Error.WriteLine("Error: Number of folders must be between 1 and 100.");
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.TargetZipSize))
        {
            var parsedSize = RequestBuilder.ParseSize(parsed.TargetZipSize);
            if (parsedSize is null)
            {
                Console.Error.WriteLine("Error: Invalid format for --target-zip-size. Use KB, MB, GB, etc. (e.g., 500MB, 10GB).");
                return false;
            }
            if (parsedSize.Value <= 0)
            {
                Console.Error.WriteLine("Error: --target-zip-size must be positive.");
                return false;
            }
        }

        return true;
    }
}
