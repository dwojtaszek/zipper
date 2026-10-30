using Zipper.Cli.Modules;
using Zipper.Config;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

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
            !modules.SourceInput.TryBuild(
                modules.Output.Count,
                production.ProductionSet,
                bates,
                modules.Chaos.RequiresSourcePaths(modules.LoadFile.Formats),
                out var sourceRecords,
                out var sourcePaths) ||
            !modules.Output.TryBuild(sourceRecords, out var output, allowZeroCount: modules.Chaos.IsAdversarialValuesScenario) ||
            !modules.Metadata.TryBuild(output.HasFileType("eml"), modules.SourceInput.HasSourceInput, out var metadata) ||
            !modules.LoadFile.TryBuild(modules.Metadata.AttachmentRate, modules.Output.Encoding, modules.Output.IsEncodingExplicit, modules.Output.Distribution, modules.Output.TargetZipSize, modules.Output.IncludeLoadFile, out var loadFile) ||
            !modules.Delimiter.TryBuild(modules.LoadFile.LoadfileOnly, production.ProductionSet, out var delimiters) ||
            !modules.Tiff.TryBuild(out var tiff) ||
            !modules.Chaos.TryBuild(
                modules.LoadFile.LoadfileOnly,
                modules.LoadFile.Formats ?? new List<LoadFileFormat> { LoadFileFormat.Dat },
                out var chaos,
                delimiters) ||
            !modules.Hash.TryBuild(modules.LoadFile.LoadfileOnly, out var hash))
        {
            return null;
        }

        if (modules.Chaos.IsAdversarialValuesScenario)
        {
            var columnProfile = metadata.ColumnProfile;
            if (sourceRecords is not null && columnProfile is null && !metadata.ShouldIncludeMetadataColumns(output))
            {
                Console.Error.WriteLine("Error: The adversarial-values scenario requires --with-metadata or a --column-profile when used with Source-Driven Generation.");
                return null;
            }

            if (columnProfile is null && metadata.ShouldIncludeMetadataColumns(output))
            {
                columnProfile = output.HasFileType("eml") ? BuiltInProfiles.LegacyEml : BuiltInProfiles.LegacyWithMetadata;
            }

            if (columnProfile is not null && AdversarialValueTailPlan.FindColumn(columnProfile.Columns) is null)
            {
                Console.Error.WriteLine("Error: The adversarial-values scenario requires a text or long-text Column Profile column.");
                return null;
            }
        }

        return new FileGenerationRequest
        {
            Output = output,
            Metadata = metadata,
            LoadFile = ApplyImageTypeLoadFileOverride(loadFile, modules.LoadFile.IsLoadFileFormatExplicit, output, sourceRecords),
            Delimiters = delimiters,
            Bates = bates,
            Tiff = tiff,
            Chaos = chaos,
            Production = production,
            LoadfileOnly = modules.LoadFile.LoadfileOnly,
            Hash = hash,
            SourceRecords = sourceRecords,
            SourcePaths = sourcePaths,
        };
    }

    // The image-type override (image-only runs get both DAT and OPT load files) keys off
    // whether the user explicitly chose formats. hasImageType reads output.FileType /
    // output.FileTypeRatios / sourceRecords — it cannot move to LoadFileModule.
    private static LoadFileConfig ApplyImageTypeLoadFileOverride(
        LoadFileConfig loadFile,
        bool isLoadFileFormatExplicit,
        OutputConfig output,
        IReadOnlyList<SourceInput.SourceRecord>? sourceRecords)
    {
        if (isLoadFileFormatExplicit)
        {
            return loadFile;
        }

        var hasImageType = output.FileType is "tiff" or "jpg"
            || (output.FileTypeRatios?.Any(r => r.Type is "tiff" or "jpg") ?? false)
            || (sourceRecords?.Any(r => r.FileType is "tiff" or "jpg") ?? false);

        return hasImageType
            ? loadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt } }
            : loadFile;
    }
}
