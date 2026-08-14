using Zipper.Cli.Modules;

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
        var parsedArgs = CliParser.Parse(args, modules.All);
        if (parsedArgs is null)
            return null;

        if (!CliValidator.Validate(parsedArgs, modules))
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

        return RequestBuilder.Build(
            output, metadata, loadFile, delimiters, bates, tiff, chaos, hash, production, sourceRecords,
            modules.LoadFile.LoadfileOnly, modules.LoadFile.IsLoadFileFormatExplicit);
    }
}
