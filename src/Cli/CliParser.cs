using System.Globalization;
using Zipper.Cli.Modules;

namespace Zipper.Cli;

public static class CliParser
{
    private static readonly Dictionary<string, Action<ParsedArguments>> ParameterlessFlags = new()
    {
        ["--with-text"] = p => p.WithText = true,
        ["--include-load-file"] = p => p.IncludeLoadFile = true,
        ["--production-set"] = p => p.ProductionSet = true,
        ["--production-zip"] = p => p.ProductionZip = true,
        ["--supplemental-production"] = p => p.SupplementalProduction = true,
        ["--redacted-production"] = p => p.RedactedProduction = true,
    };

    public static ParsedArguments? Parse(string[] args) => Parse(args, CliModules.Create().All);

    public static ParsedArguments? Parse(string[] args, IReadOnlyList<CliModule> modules)
    {
        ArgumentNullException.ThrowIfNull(args);

        var parsed = new ParsedArguments();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToLowerInvariant();

            var module = modules.FirstOrDefault(m => m.Owns(arg));
            if (module is not null)
            {
                string? value = null;
                if (module.TakesValue(arg))
                {
                    if (!TryGetValue(args, i, out value))
                    {
                        Console.Error.WriteLine($"Error: {arg} requires a value.");
                        return null;
                    }
                    i++;
                }

                if (!module.TryApply(arg, value))
                {
                    return null;
                }
                continue;
            }

            if (ParameterlessFlags.TryGetValue(arg, out var flagAction))
            {
                flagAction(parsed);
                continue;
            }

            switch (arg)
            {
                // --- Output args ---
                case "--type":
                    if (!ReadStringArg(args, ref i, "--type", out var fileType)) return null;
                    parsed.FileType = fileType;
                    break;
                case "--types":
                    if (!ReadStringArg(args, ref i, "--types", out var fileTypes)) return null;
                    parsed.FileTypes = fileTypes;
                    break;
                case "--input-csv":
                    if (!ReadStringArg(args, ref i, "--input-csv", out var inputCsv)) return null;
                    parsed.InputCsv = inputCsv;
                    break;
                case "--directory-template":
                    if (!ReadStringArg(args, ref i, "--directory-template", out var dirTemplate)) return null;
                    parsed.DirectoryTemplate = dirTemplate;
                    break;
                case "--count":
                    if (!ReadLongArg(args, ref i, "--count", out var count)) return null;
                    parsed.Count = count;
                    break;
                case "--output-path":
                    if (!ReadStringArg(args, ref i, "--output-path", out var pathArg)) return null;
                    parsed.OutputPathStr = pathArg;
                    break;
                case "--folders":
                    if (!ReadIntArg(args, ref i, "--folders", out var folders)) return null;
                    parsed.Folders = folders;
                    break;
                case "--encoding":
                    if (!ReadStringArg(args, ref i, "--encoding", out var encoding)) return null;
                    parsed.Encoding = encoding;
                    parsed.IsEncodingExplicit = true;
                    break;
                case "--distribution":
                    if (!ReadStringArg(args, ref i, "--distribution", out var dist)) return null;
                    parsed.Distribution = dist;
                    break;

                // --- Output args ---
                case "--target-zip-size":
                    if (!ReadStringArg(args, ref i, "--target-zip-size", out var zipSize)) return null;
                    parsed.TargetZipSize = zipSize;
                    break;
                case "--volume-size":
                    if (!ReadIntArg(args, ref i, "--volume-size", out var volumeSize)) return null;
                    parsed.VolumeSize = volumeSize;
                    break;
                case "--prior-manifest":
                    if (!ReadStringArg(args, ref i, "--prior-manifest", out var priorManifestVal)) return null;
                    parsed.PriorManifests = priorManifestVal;
                    break;
                case "--supplemental-gap-policy":
                    if (!ReadStringArg(args, ref i, "--supplemental-gap-policy", out var gapPolicyVal)) return null;
                    parsed.SupplementalGapPolicy = gapPolicyVal;
                    break;
                case "--production-id":
                    if (!ReadStringArg(args, ref i, "--production-id", out var prodIdVal)) return null;
                    parsed.ProductionId = prodIdVal;
                    break;
                case "--rolling-count":
                    if (!ReadIntArg(args, ref i, "--rolling-count", out var rollingCount)) return null;
                    parsed.RollingCount = rollingCount;
                    break;
                case "--rolling-bates-mode":
                    if (!ReadStringArg(args, ref i, "--rolling-bates-mode", out var batesModeVal)) return null;
                    parsed.RollingBatesMode = batesModeVal;
                    break;
                case "--source-path-mode":
                    if (!ReadStringArg(args, ref i, "--source-path-mode", out var sourcePathModeVal)) return null;
                    parsed.SourcePathMode = sourcePathModeVal;
                    break;
                case "--withheld-native-policy":
                    if (!ReadStringArg(args, ref i, "--withheld-native-policy", out var withheldVal)) return null;
                    parsed.WithheldNativePolicy = withheldVal;
                    break;

                // --- Comparison args ---
                case "--compare-production-manifests":
                    if (!ReadStringArg(args, ref i, "--compare-production-manifests", out var compareManifestsVal)) return null;
                    parsed.CompareProductionManifests = compareManifestsVal;
                    break;
                case "--comparison-mode":
                    if (!ReadStringArg(args, ref i, "--comparison-mode", out var compModeVal)) return null;
                    parsed.ComparisonMode = compModeVal;
                    break;
                case "--comparison-output":
                    if (!ReadStringArg(args, ref i, "--comparison-output", out var compOutVal)) return null;
                    parsed.ComparisonOutput = compOutVal;
                    break;

                default:
                    Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{args[i]}'");
                    return null;
            }
        }

        return parsed;
    }

    private static bool ReadStringArg(string[] args, ref int i, string flagName, out string value)
    {
        if (TryGetValue(args, i, out value))
        {
            i++;
            return true;
        }

        Console.Error.WriteLine($"Error: {flagName} requires a value.");
        return false;
    }

    private static bool ReadIntArg(string[] args, ref int i, string flagName, out int value)
    {
        if (TryGetValue(args, i, out var str))
        {
            if (int.TryParse(str, CultureInfo.InvariantCulture, out value))
            {
                i++;
                return true;
            }

            Console.Error.WriteLine($"Error: Invalid value for {flagName}: '{str}'");
            value = 0;
            return false;
        }

        Console.Error.WriteLine($"Error: {flagName} requires a value.");
        value = 0;
        return false;
    }

    private static bool ReadLongArg(string[] args, ref int i, string flagName, out long value)
    {
        if (TryGetValue(args, i, out var str))
        {
            if (long.TryParse(str, CultureInfo.InvariantCulture, out value))
            {
                i++;
                return true;
            }

            Console.Error.WriteLine($"Error: Invalid value for {flagName}: '{str}'");
            value = 0;
            return false;
        }

        Console.Error.WriteLine($"Error: {flagName} requires a value.");
        value = 0;
        return false;
    }

    private static bool TryGetValue(string[] args, int currentIndex, out string value)
    {
        if (currentIndex + 1 < args.Length && !args[currentIndex + 1].StartsWith("--", StringComparison.Ordinal))
        {
            value = args[currentIndex + 1];
            return true;
        }

        value = string.Empty;
        return false;
    }
}
