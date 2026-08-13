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
        {
            return null;
        }

        if (!CliValidator.Validate(parsedArgs))
        {
            return null;
        }

        if (!modules.Delimiter.TryBuild(parsedArgs, out var delimiters) ||
            !modules.Tiff.TryBuild(parsedArgs, out var tiff) ||
            !modules.Chaos.TryBuild(parsedArgs, out var chaos) ||
            !modules.Hash.TryBuild(parsedArgs, out var hash))
        {
            return null;
        }

        return RequestBuilder.Build(parsedArgs, delimiters, tiff, chaos, hash);
    }
}
