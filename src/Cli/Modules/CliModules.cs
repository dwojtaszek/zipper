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
    public required ArchiveTestModule ArchiveTest { get; init; }
    public IReadOnlyList<CliModule> All => new CliModule[] { Production, SourceInput, Output, Bates, Metadata, LoadFile, Delimiter, Tiff, Chaos, Hash, Comparison, ArchiveTest };

    private readonly HashSet<string> _consumedFlags = new(StringComparer.Ordinal);

    /// <summary>
    /// The flags successfully applied by the last <see cref="Parse"/> (lowercase,
    /// "--"-prefixed). Presence-based on purpose: a flag explicitly set to its default
    /// value still counts as consumed, which the closed Archive Test flag set relies on.
    /// In comparison mode (REQ-179), ignored generation flags are recorded here even
    /// though their <c>TryApply</c> never ran.
    /// </summary>
    public IReadOnlyCollection<string> ConsumedFlags => _consumedFlags;

    /// <summary>
    /// Token reader + module dispatcher: for each token finds the owning module, pulls a
    /// value when the flag takes one, and delegates to the module's TryApply. Successor of
    /// the old CliParser loop (which also handled the comparison trio — now a module too).
    /// </summary>
    public bool Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _consumedFlags.Clear();

        // REQ-179: Production Manifest Comparison short-circuits before generation and
        // Production Set argument validation. When the trigger flag is present (anywhere
        // on the command line), comparison and Archive Test flags parse strictly while
        // every other registered flag is syntactically consumed — its value token is
        // swallowed but TryApply is never called, so no value parser or module validation
        // runs. Unknown flags still fail, and missing/invalid values on the comparison
        // flags still fail (REQ-176/177/178).
        var comparisonRequested = args.Any(a => string.Equals(a, "--compare-production-manifests", StringComparison.OrdinalIgnoreCase));

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

            var strict = !comparisonRequested || module is ComparisonModule or ArchiveTestModule;

            string? value = null;
            if (module.TakesValue(arg))
            {
                if (!TryGetValue(args, i, out value))
                {
                    if (strict)
                    {
                        Console.Error.WriteLine($"Error: {arg} requires a value.");
                        return false;
                    }
                    // Lenient: consume the flag without a value token (TryApply is skipped
                    // below, so the unused `value` stays null).
                }
                else
                {
                    i++;
                }
            }

            if (strict && !module.TryApply(arg, value))
            {
                return false;
            }

            _consumedFlags.Add(arg);
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
            ArchiveTest = new ArchiveTestModule(),
        };
    }
}
