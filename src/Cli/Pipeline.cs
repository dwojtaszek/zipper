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

        if (!modules.Production.TryBuild(out var production))
        {
            return null;
        }

        if (!modules.Bates.TryBuild(
            production.ProductionSet,
            production.RollingCount,
            production.RollingBatesMode.ToString(),
            modules.Output.Count,
            out var bates))
        {
            return null;
        }

        if (!modules.SourceInput.TryBuild(
            modules.Output.Count,
            production.ProductionSet,
            bates,
            modules.Chaos.RequiresSourcePaths(modules.LoadFile.Formats),
            out var sourceRecords,
            out var sourcePaths))
        {
            return null;
        }

        if (!modules.Output.TryBuild(
            sourceRecords,
            out var output,
            allowZeroCount: modules.Chaos.IsAdversarialValuesScenario))
        {
            return null;
        }

        if (!modules.Metadata.TryBuild(output.HasFileType("eml"), modules.SourceInput.HasSourceInput, out var metadata))
        {
            return null;
        }

        if (!modules.LoadFile.TryBuild(
            modules.Metadata.AttachmentRate,
            modules.Output.Encoding,
            modules.Output.IsEncodingExplicit,
            modules.Output.Distribution,
            modules.Output.TargetZipSize,
            modules.Output.IncludeLoadFile,
            out var loadFile))
        {
            return null;
        }

        if (!modules.Delimiter.TryBuild(modules.LoadFile.LoadfileOnly, production.ProductionSet, out var delimiters))
        {
            return null;
        }

        if (!modules.Tiff.TryBuild(out var tiff))
        {
            return null;
        }

        var requestedFormats = modules.LoadFile.Formats;
        if (requestedFormats is null && modules.Chaos.ChaosMode && !string.IsNullOrEmpty(modules.Chaos.ChaosScenario))
        {
            var scenario = ChaosScenarios.GetByName(modules.Chaos.ChaosScenario);
            if (scenario?.IsCrossFormat == true)
            {
                requestedFormats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt };
            }
        }

        if (!modules.Chaos.TryBuild(
            modules.LoadFile.LoadfileOnly,
            production.ProductionSet ? new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt } : (requestedFormats ?? new List<LoadFileFormat> { LoadFileFormat.Dat }),
            out var chaos,
            delimiters,
            loadFile.Encoding,
            production.ProductionSet,
            output.HasFileType("eml")))
        {
            return null;
        }

        if (!modules.Hash.TryBuild(modules.LoadFile.LoadfileOnly, out var hash))
        {
            return null;
        }

        if (!TrySelectAdversarialValueColumn(modules, output, metadata, sourceRecords, out var adversarialValueColumnName))
        {
            return null;
        }

        var resolvedLoadFile = ApplyImageTypeLoadFileOverride(loadFile, modules.LoadFile.IsLoadFileFormatExplicit, output, sourceRecords);
        if (ChaosScenarios.IsCrossFormat(modules.Chaos.ChaosScenario) && !modules.LoadFile.IsLoadFileFormatExplicit)
        {
            resolvedLoadFile = resolvedLoadFile with { Formats = new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt } };
        }

        if (modules.Chaos.IsAdversarialValuesScenario && resolvedLoadFile.Formats.Any(format => format != LoadFileFormat.Dat))
        {
            Console.Error.WriteLine("Error: Chaos scenario 'adversarial-values' requires DAT-only output. Image file types default to DAT and OPT; pass --loadfile-format dat explicitly.");
            return null;
        }

        if (bates is not null)
        {
            BatesModule.WarnPlatformLimits(bates, production, output.FileCount, tiff.PageRange?.Max ?? 1);
        }

        return new FileGenerationRequest
        {
            Output = output,
            Metadata = metadata,
            LoadFile = resolvedLoadFile,
            Delimiters = delimiters,
            Bates = bates,
            Tiff = tiff,
            Chaos = chaos,
            Production = production,
            LoadfileOnly = modules.LoadFile.LoadfileOnly,
            Hash = hash,
            AdversarialValueColumnName = adversarialValueColumnName,
            SourceRecords = sourceRecords,
            SourcePaths = sourcePaths,
        };
    }

    private static bool TrySelectAdversarialValueColumn(
        CliModuleSet modules,
        OutputConfig output,
        MetadataConfig metadata,
        IReadOnlyList<SourceInput.SourceRecord>? sourceRecords,
        out string? adversarialValueColumnName)
    {
        adversarialValueColumnName = null;
        if (!modules.Chaos.IsAdversarialValuesScenario)
        {
            return true;
        }

        var columnProfile = metadata.ColumnProfile;
        if (sourceRecords is not null && columnProfile is null && !metadata.ShouldIncludeMetadataColumns(output))
        {
            Console.Error.WriteLine("Error: The adversarial-values scenario requires --with-metadata or a --column-profile when used with Source-Driven Generation.");
            return false;
        }

        if (columnProfile is null && metadata.ShouldIncludeMetadataColumns(output))
        {
            columnProfile = output.HasFileType("eml") ? BuiltInProfiles.LegacyEml : BuiltInProfiles.LegacyWithMetadata;
        }

        if (columnProfile is null)
        {
            return true;
        }

        var eligibleColumn = AdversarialValueTailPlan.FindColumn(columnProfile.Columns);
        if (eligibleColumn is null)
        {
            Console.Error.WriteLine("Error: The adversarial-values scenario requires a text or long-text Column Profile column.");
            return false;
        }

        var sourceMetadataKeys = metadata.ColumnProfile is null || modules.SourceInput.InputCsv is null
            ? null
            : modules.SourceInput.SourceMetadataKeys;
        var adversarialValueColumn = sourceMetadataKeys is null
            ? eligibleColumn
            : AdversarialValueTailPlan.FindColumn(columnProfile.Columns, sourceMetadataKeys);
        if (adversarialValueColumn is null)
        {
            Console.Error.WriteLine("Error: The adversarial-values scenario requires a text or long-text Column Profile column that is not mapped to Source Metadata.");
            return false;
        }

        adversarialValueColumnName = metadata.ColumnProfile is null ? null : adversarialValueColumn.Name;
        return true;
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
