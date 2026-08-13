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

        if (!modules.Bates.TryBuild(parsedArgs, out var bates) ||
            !modules.Metadata.TryBuild(parsedArgs, out var metadata) ||
            !modules.LoadFile.TryBuild(parsedArgs, modules.Metadata.AttachmentRate, out var loadFile) ||
            !modules.Delimiter.TryBuild(parsedArgs, modules.LoadFile.LoadfileOnly, out var delimiters) ||
            !modules.Tiff.TryBuild(parsedArgs, out var tiff) ||
            !modules.Chaos.TryBuild(parsedArgs, modules.LoadFile.LoadfileOnly, modules.LoadFile.CurrentFormat, out var chaos) ||
            !modules.Hash.TryBuild(parsedArgs, modules.LoadFile.LoadfileOnly, out var hash))
        {
            return null;
        }

        return RequestBuilder.Build(
            parsedArgs, delimiters, tiff, chaos, hash, bates, metadata, loadFile,
            modules.LoadFile.LoadfileOnly, modules.LoadFile.IsLoadFileFormatExplicit);
    }
}
