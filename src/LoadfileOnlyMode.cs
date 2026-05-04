namespace Zipper
{
    /// <summary>
    /// Loadfile-Only generation mode — emits a load file plus its properties sidecar without producing native files.
    /// </summary>
    internal class LoadfileOnlyMode : IGenerationMode
    {
        public async Task RunAsync(FileGenerationRequest request)
        {
            Console.WriteLine("Starting loadfile-only generation...");
            Console.WriteLine(string.Format("  Format: {0}", request.LoadFile.LoadFileFormat));
            Console.WriteLine(string.Format("  Count: {0:N0}", request.Output.FileCount));
            Console.WriteLine(string.Format("  Output Path: {0}", request.Output.OutputPath));
            Console.WriteLine(string.Format("  Encoding: {0}", request.LoadFile.Encoding));
            Console.WriteLine(string.Format("  EOL: {0}", request.Delimiters.EndOfLine));

            if (request.Chaos.ChaosMode)
            {
                Console.WriteLine(string.Format("  Chaos Mode: Enabled (amount: {0})", request.Chaos.ChaosAmount ?? "1%"));
                if (!string.IsNullOrEmpty(request.Chaos.ChaosTypes))
                {
                    Console.WriteLine(string.Format("  Chaos Types: {0}", request.Chaos.ChaosTypes));
                }
            }

            var result = await LoadfileOnlyGenerator.GenerateAsync(request);

            Console.WriteLine(string.Format("\n\nGeneration complete in {0:F1} seconds.", result.GenerationTime.TotalSeconds));
            Console.WriteLine(string.Format("  Load file: {0}", result.LoadFilePath));
            Console.WriteLine(string.Format("  Properties: {0}", result.PropertiesFilePath));
            Console.WriteLine(string.Format("  Records: {0:N0}", result.TotalRecords));
        }
    }
}
