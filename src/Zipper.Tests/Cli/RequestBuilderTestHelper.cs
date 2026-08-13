using Zipper.Cli;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

internal static class RequestBuilderTestHelper
{
    public static FileGenerationRequest? Build(ParsedArguments parsed)
    {
        var modules = CliModules.Create();
        if (!modules.Delimiter.TryBuild(parsed, out var delimiters) ||
            !modules.Tiff.TryBuild(parsed, out var tiff) ||
            !modules.Chaos.TryBuild(parsed, out var chaos) ||
            !modules.Hash.TryBuild(parsed, out var hash))
        {
            return null;
        }

        return RequestBuilder.Build(parsed, delimiters, tiff, chaos, hash);
    }
}
