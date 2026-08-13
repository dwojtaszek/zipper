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
        ParsedArguments parsed,
        Action<CliModuleSet>? configureModules = null,
        CliModuleSet? modules = null)
    {
        modules ??= CliModules.Create();
        configureModules?.Invoke(modules);
        if (!modules.Bates.TryBuild(parsed, out var bates) ||
            !modules.Metadata.TryBuild(parsed, out var metadata) ||
            !modules.LoadFile.TryBuild(parsed, modules.Metadata.AttachmentRate, out var loadFile) ||
            !modules.Delimiter.TryBuild(parsed, modules.LoadFile.LoadfileOnly, out var delimiters) ||
            !modules.Tiff.TryBuild(parsed, out var tiff) ||
            !modules.Chaos.TryBuild(parsed, modules.LoadFile.LoadfileOnly, modules.LoadFile.CurrentFormat, out var chaos) ||
            !modules.Hash.TryBuild(parsed, modules.LoadFile.LoadfileOnly, out var hash))
        {
            return null;
        }

        return RequestBuilder.Build(
            parsed, delimiters, tiff, chaos, hash, bates, metadata, loadFile,
            modules.LoadFile.LoadfileOnly, modules.LoadFile.IsLoadFileFormatExplicit);
    }
}
