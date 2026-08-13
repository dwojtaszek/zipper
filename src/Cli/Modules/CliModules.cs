namespace Zipper.Cli.Modules;

/// <summary>
/// One constructed set of CLI modules. Pipeline must parse and TryBuild against
/// the same instances (TryApply mutates fields). Do not new the modules twice.
/// </summary>
public sealed class CliModuleSet
{
    public required BatesModule Bates { get; init; }
    public required MetadataModule Metadata { get; init; }
    public required LoadFileModule LoadFile { get; init; }
    public required DelimiterModule Delimiter { get; init; }
    public required TiffModule Tiff { get; init; }
    public required ChaosModule Chaos { get; init; }
    public required HashModule Hash { get; init; }
    public IReadOnlyList<CliModule> All => new CliModule[] { Bates, Metadata, LoadFile, Delimiter, Tiff, Chaos, Hash };
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
            Bates = new BatesModule(),
            Metadata = new MetadataModule(),
            LoadFile = new LoadFileModule(),
            Delimiter = new DelimiterModule(),
            Tiff = new TiffModule(),
            Chaos = new ChaosModule(),
            Hash = new HashModule(),
        };
    }
}
