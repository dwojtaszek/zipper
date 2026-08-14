using Zipper.Cli.Modules;

namespace Zipper.Cli;

public static class CliParser
{
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

            switch (arg)
            {
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
