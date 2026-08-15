using System.Reflection;

namespace Zipper;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        args ??= [];

        if (args.Length == 1 && string.Equals(args[0], "--version", StringComparison.OrdinalIgnoreCase))
        {
            var asm = System.Reflection.Assembly.GetEntryAssembly() ?? typeof(Program).Assembly;
            var infoVer = asm.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            var fileVer = asm.GetCustomAttribute<System.Reflection.AssemblyFileVersionAttribute>()?.Version;
            var asmVer = asm.GetName().Version?.ToString();
            var version = (!string.IsNullOrEmpty(infoVer) && infoVer != "1.0.0.0" && infoVer != "1.0.0") ? infoVer : (fileVer ?? asmVer ?? "2.0.0");
            Console.WriteLine($"Zipper v{version} https://github.com/dwojtaszek/zipper/");
            return 0;
        }

        if (args.Contains("--benchmark", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var report = await PerformanceBenchmarkRunner.RunBenchmarksAsync().ConfigureAwait(false);
                return report.OverallPassed ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\nBenchmark error: {ex.Message}");
                return 1;
            }
        }

        if (args.Contains("--chaos-list", StringComparer.OrdinalIgnoreCase))
        {
            ChaosScenarios.PrintScenarioList();
            return 0;
        }

        var modules = Cli.Modules.CliModules.Create();
        if (args.Length == 0)
        {
            Cli.HelpTextGenerator.Show();
            return 1;
        }

        if (!modules.Parse(args))
        {
            return 1;
        }

        if (!modules.Comparison.TryBuild(out var comparison))
        {
            return 1;
        }

        if (comparison is not null)
        {
            try
            {
                var success = await ManifestComparison.ProductionManifestComparer.CompareAndReportAsync(
                    comparison.ManifestPaths, comparison.Mode, comparison.OutputPath).ConfigureAwait(false);
                return success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }
        }

        var request = Cli.Pipeline.Build(modules);
        if (request is null)
        {
            return 1;
        }

        using var cts = new CancellationTokenSource();
        using var sigInt = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGINT,
            context =>
            {
                context.Cancel = true;
                cts.Cancel();
            });
        using var sigTerm = System.Runtime.InteropServices.PosixSignalRegistration.Create(
            System.Runtime.InteropServices.PosixSignal.SIGTERM,
            context =>
            {
                context.Cancel = true;
                cts.Cancel();
            });

        IGenerationMode mode = SelectMode(request);
        return await GenerationRunner.RunAsync(mode, request, cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Picks the appropriate generation mode based on flags on the request.
    /// CrossCuttingRules ensures LoadfileOnly and ProductionSet are mutually exclusive.
    /// </summary>
    internal static IGenerationMode SelectMode(FileGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (request.LoadfileOnly, request.Production?.ProductionSet ?? false) switch
        {
            (true, _) => new LoadFileOnlyMode((req, ct) => LoadFileOnlyGenerator.GenerateAsync(req, ct)),
            (_, true) => new ProductionSetMode((req, ct) => ProductionSetGenerator.GenerateAsync(req, ct)),
            _ => new StandardMode((req, ct) => new ParallelFileGenerator().GenerateFilesAsync(req, ct)),
        };
    }
}
