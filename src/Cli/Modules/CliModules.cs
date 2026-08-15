namespace Zipper.Cli.Modules;

/// <summary>
/// One constructed set of CLI modules. Pipeline must parse and TryBuild against
/// the same instances (TryApply mutates fields). Do not new the modules twice.
/// </summary>
public sealed class CliModuleSet
{
    public required ProductionModule Production { get; init; }
    public required SourceInputModule SourceInput { get; init; }
    public required OutputModule Output { get; init; }
    public required BatesModule Bates { get; init; }
    public required MetadataModule Metadata { get; init; }
    public required LoadFileModule LoadFile { get; init; }
    public required DelimiterModule Delimiter { get; init; }
    public required TiffModule Tiff { get; init; }
    public required ChaosModule Chaos { get; init; }
    public required HashModule Hash { get; init; }
    public required ComparisonModule Comparison { get; init; }
    public IReadOnlyList<CliModule> All => new CliModule[] { Production, SourceInput, Output, Bates, Metadata, LoadFile, Delimiter, Tiff, Chaos, Hash, Comparison };

    /// <summary>
    /// Token reader + module dispatcher: for each token finds the owning module, pulls a
    /// value when the flag takes one, and delegates to the module's TryApply. Successor of
    /// the old CliParser loop (which also handled the comparison trio — now a module too).
    /// </summary>
    public bool Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var modules = All;
        int i = 0;
        while (i < args.Length)
        {
            var arg = args[i].ToLowerInvariant();

            var module = modules.FirstOrDefault(m => m.Owns(arg));
            if (module is null)
            {
                Console.Error.WriteLine($"Error: Unknown argument or unconsumed value '{args[i]}'");
                return false;
            }

            string? value = null;
            if (module.TakesValue(arg))
            {
                if (!TryGetValue(args, i, out value))
                {
                    Console.Error.WriteLine($"Error: {arg} requires a value.");
                    return false;
                }
                i++;
            }

            if (!module.TryApply(arg, value))
            {
                return false;
            }
            i++;
        }

        return true;
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

/// <summary>
/// Registry of all CLI modules. Extended one module per phase until every
/// sub-domain of FileGenerationRequest is owned by a module.
/// </summary>
public static class CliModules
{
    public static CliModuleSet Create()
    {
        return new CliModuleSet
        {
            Production = new ProductionModule(),
            SourceInput = new SourceInputModule(),
            Output = new OutputModule(),
            Bates = new BatesModule(),
            Metadata = new MetadataModule(),
            LoadFile = new LoadFileModule(),
            Delimiter = new DelimiterModule(),
            Tiff = new TiffModule(),
            Chaos = new ChaosModule(),
            Hash = new HashModule(),
            Comparison = new ComparisonModule(),
        };
    }
}
