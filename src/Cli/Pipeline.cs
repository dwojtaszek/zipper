using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Cli;

public static class Pipeline
{
    public static FileGenerationRequest? Build(string[] args)
    {
        if (args is null || args.Length is 0)
        {
            HelpTextGenerator.Show();
            return null;
        }

        var modules = CliModules.Create();
        if (!modules.Parse(args))
            return null;

        return Build(modules);
    }

    internal static FileGenerationRequest? Build(CliModuleSet modules)
    {
        if (!CrossCuttingRules.Validate(modules))
            return null;

        if (!modules.Production.TryBuild(out var production) ||
            !modules.Bates.TryBuild(production.ProductionSet, production.RollingCount, production.RollingBatesMode.ToString(), modules.Output.Count, out var bates) ||
            !modules.SourceInput.TryBuild(modules.Output.Count, production.ProductionSet, bates, out var sourceRecords) ||
            !modules.Output.TryBuild(sourceRecords, out var output) ||
            !modules.Metadata.TryBuild(output.HasFileType("eml"), modules.SourceInput.HasSourceInput, out var metadata) ||
            !modules.LoadFile.TryBuild(modules.Metadata.AttachmentRate, modules.Output.Encoding, modules.Output.IsEncodingExplicit, modules.Output.Distribution, modules.Output.TargetZipSize, modules.Output.IncludeLoadFile, out var loadFile) ||
            !modules.Delimiter.TryBuild(modules.LoadFile.LoadfileOnly, production.ProductionSet, out var delimiters) ||
            !modules.Tiff.TryBuild(out var tiff) ||
            !modules.Chaos.TryBuild(modules.LoadFile.LoadfileOnly, modules.LoadFile.CurrentFormat, out var chaos) ||
            !modules.Hash.TryBuild(modules.LoadFile.LoadfileOnly, out var hash))
        {
            return null;
        }

        return AssembleRequest(
            output, metadata, loadFile, delimiters, bates, tiff, chaos, hash, production, sourceRecords,
            modules.LoadFile.LoadfileOnly, modules.LoadFile.IsLoadFileFormatExplicit);
    }

    private static FileGenerationRequest AssembleRequest(
        OutputConfig output,
        MetadataConfig metadata,
        LoadFileConfig loadFile,
        DelimiterConfig delimiters,
        BatesNumberConfig? bates,
        TiffConfig tiff,
        ChaosConfig chaos,
        HashConfig hash,
        ProductionConfig production,
        IReadOnlyList<SourceInput.SourceRecord>? sourceRecords,
        bool loadfileOnly,
        bool isLoadFileFormatExplicit)
    {
        // The image-type override (image-only runs get both DAT and OPT load files) keys off
        // whether the user explicitly chose formats. hasImageType reads output.FileType /
        // output.FileTypeRatios / sourceRecords — it cannot move to LoadFileModule.
        if (!isLoadFileFormatExplicit)
        {
            var hasImageType = output.FileType is "tiff" or "jpg"
                || (output.FileTypeRatios?.Any(r => r.Type is "tiff" or "jpg") ?? false)
                || (sourceRecords?.Any(r => r.FileType is "tiff" or "jpg") ?? false);
            if (hasImageType)
            {
                loadFile = loadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt } };
            }
        }

        return new FileGenerationRequest
        {
            Output = output,
            Metadata = metadata,
            LoadFile = loadFile,
            Delimiters = delimiters,
            Bates = bates,
            Tiff = tiff,
            Chaos = chaos,
            Production = production,
            LoadfileOnly = loadfileOnly,
            Hash = hash,
            SourceRecords = sourceRecords,
        };
    }
}
