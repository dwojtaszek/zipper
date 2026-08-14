using Zipper.Cli;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

internal static class RequestBuilderTestHelper
{
    public static (ParsedArguments? Parsed, CliModuleSet Modules) Parse(string[] args)
    {
        var modules = CliModules.Create();
        return (CliParser.Parse(args, modules.All), modules);
    }

    public static FileGenerationRequest? Build(
        ParsedArguments? parsed,
        Action<CliModuleSet>? configureModules = null,
        CliModuleSet? modules = null)
        => Build(modules, configureModules);

    public static FileGenerationRequest? Build(
        CliModuleSet? modules = null,
        Action<CliModuleSet>? configureModules = null)
    {
        modules ??= CliModules.Create();
        configureModules?.Invoke(modules);
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

        return RequestBuilder.Build(
            output, metadata, loadFile, delimiters, bates, tiff, chaos, hash, production, sourceRecords,
            modules.LoadFile.LoadfileOnly, modules.LoadFile.IsLoadFileFormatExplicit);
    }

    public static FileGenerationRequest? Build(string[] args)
        => Cli.Pipeline.Build(args);
}
