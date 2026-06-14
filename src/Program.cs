using System.Reflection;

namespace Zipper
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            args ??= [];

            if (args.Contains("--version", StringComparer.OrdinalIgnoreCase))
            {
                var version = System.Reflection.Assembly.GetEntryAssembly()?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
                Console.WriteLine($"Zipper v{version} https://github.com/dwojtaszek/zipper/");
                return 0;
            }

            if (args.Contains("--benchmark", StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await PerformanceBenchmarkRunner.RunBenchmarks().ConfigureAwait(false);
                    return 0;
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

            var request = Cli.Pipeline.Build(args);
            if (request == null)
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
        /// The CLI validator ensures LoadfileOnly and ProductionSet are mutually exclusive.
        /// </summary>
        internal static IGenerationMode SelectMode(FileGenerationRequest request)
        {
            return (request.LoadfileOnly, request.Production.ProductionSet) switch
            {
                (true, _) => new LoadfileOnlyMode(),
                (_, true) => new ProductionSetMode(),
                _ => new StandardMode(),
            };
        }
    }
}
