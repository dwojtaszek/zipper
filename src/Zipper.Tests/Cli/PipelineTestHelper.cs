using Zipper.Cli.Modules;

namespace Zipper.Tests;

internal static class PipelineTestHelper
{
    public static (bool Ok, CliModuleSet Modules) Parse(string[] args)
    {
        var modules = CliModules.Create();
        return (modules.Parse(args), modules);
    }

    public static FileGenerationRequest? Build(
        CliModuleSet? modules = null,
        Action<CliModuleSet>? configureModules = null)
    {
        modules ??= CliModules.Create();
        configureModules?.Invoke(modules);
        return Cli.Pipeline.Build(modules);
    }

    public static FileGenerationRequest? Build(string[] args)
        => Cli.Pipeline.Build(args);
}
