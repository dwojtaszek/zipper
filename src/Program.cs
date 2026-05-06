using System.Reflection;

namespace Zipper
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
            Console.WriteLine($"Zipper v{version} https://github.com/dwojtaszek/zipper/");
            Console.WriteLine();

            if (args.Contains("--benchmark", StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    await PerformanceBenchmarkRunner.RunBenchmarks();
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

            IGenerationMode mode = SelectMode(request);
            return await GenerationRunner.RunAsync(mode, request);
        }

        /// <summary>
        /// Picks the appropriate generation mode based on flags on the request.
        /// Production Set takes precedence over Loadfile-Only for backward compatibility
        /// with the previous dispatch order in <c>Main</c>.
        /// </summary>
        internal static IGenerationMode SelectMode(FileGenerationRequest request)
        {
            if (request.LoadfileOnly)
            {
                return new LoadfileOnlyMode();
            }

            if (request.Production.ProductionSet)
            {
                return new ProductionSetMode();
            }

            return new StandardMode();
        }
    }
}
