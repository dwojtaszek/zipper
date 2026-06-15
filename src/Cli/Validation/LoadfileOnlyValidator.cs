namespace Zipper.Cli.Validation;

internal static class LoadfileOnlyValidator
{
    public static bool Validate(ParsedArguments parsed)
    {
        if (!string.IsNullOrEmpty(parsed.Eol) && !parsed.LoadfileOnly)
        {
            Console.Error.WriteLine("Error: --eol requires --loadfile-only.");
            return false;
        }

        if (parsed.LoadfileOnly)
        {
            if (!string.IsNullOrEmpty(parsed.TargetZipSize))
            {
                Console.Error.WriteLine("Error: --loadfile-only conflicts with --target-zip-size.");
                return false;
            }

            if (parsed.IncludeLoadFile)
            {
                Console.Error.WriteLine("Error: --loadfile-only conflicts with --include-load-file.");
                return false;
            }

            // #357: This validation confirms Loadfile-Only is restricted to DAT/OPT formats.
            // Encoding reachability re-validation is intentionally removed because CrossCuttingValidator handles encoding.
            if (!string.IsNullOrEmpty(parsed.LoadFileFormat))
            {
                var currentFormat = RequestBuilder.GetLoadFileFormat(parsed.LoadFileFormat) ?? LoadFileFormat.Dat;
                if (currentFormat != LoadFileFormat.Dat && currentFormat != LoadFileFormat.Opt)
                {
                    Console.Error.WriteLine("Error: --loadfile-only mode is only supported for 'dat' and 'opt' load file formats.");
                    return false;
                }
            }

            if (!string.IsNullOrEmpty(parsed.LoadFileFormats))
            {
                var formats = parsed.LoadFileFormats.Split(',');
                foreach (var fmt in formats)
                {
                    var currentFormat = RequestBuilder.GetLoadFileFormat(fmt.Trim());
                    if (currentFormat.HasValue && currentFormat.Value != LoadFileFormat.Dat && currentFormat.Value != LoadFileFormat.Opt)
                    {
                        Console.Error.WriteLine("Error: --loadfile-only mode is only supported for 'dat' and 'opt' load file formats.");
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
