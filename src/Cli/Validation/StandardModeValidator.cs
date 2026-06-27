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

        if (parsed.WithFamilies)
        {
            if (parsed.LoadfileOnly)
            {
                Console.Error.WriteLine("Warning: --with-families has no effect in --loadfile-only mode.");
            }
            else if (!string.Equals(parsed.FileType, "eml", StringComparison.OrdinalIgnoreCase) || parsed.AttachmentRate <= 0)
            {
                Console.Error.WriteLine("Warning: --with-families is only meaningful when --type eml and --attachment-rate > 0 are specified.");
            }
        }

        return true;
    }
}
