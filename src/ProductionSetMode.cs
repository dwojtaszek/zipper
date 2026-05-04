namespace Zipper
{
    /// <summary>
    /// Production Set generation mode — emits a Bates-numbered, volumed production with DAT, OPT, and manifest sidecars.
    /// </summary>
    internal class ProductionSetMode : IGenerationMode
    {
        public async Task RunAsync(FileGenerationRequest request)
        {
            Console.WriteLine("Starting production set generation...");
            Console.WriteLine(string.Format("  File Type: {0}", request.Output.FileType));
            Console.WriteLine(string.Format("  Count: {0:N0}", request.Output.FileCount));
            Console.WriteLine(string.Format("  Output Path: {0}", request.Output.OutputPath));
            Console.WriteLine(string.Format("  Volume Size: {0:N0} files/volume", request.Production.VolumeSize));
            var batesPrefix = request.Bates?.Prefix ?? string.Empty;
            var batesStart = request.Bates?.Start ?? 1;
            var batesDigits = request.Bates?.Digits ?? 8;
            Console.WriteLine(string.Format("  Bates: {0}{1}", batesPrefix, batesStart.ToString($"D{batesDigits}")));
            if (request.Production.ProductionZip)
            {
                Console.WriteLine("  ZIP Output: Enabled");
            }

            var result = await ProductionSetGenerator.GenerateAsync(request);

            Console.WriteLine(string.Format("\n\nProduction set complete in {0:F1} seconds.", result.GenerationTime.TotalSeconds));
            Console.WriteLine(string.Format("  Production: {0}", result.ProductionPath));
            Console.WriteLine(string.Format("  Documents: {0:N0}", result.TotalDocuments));
            Console.WriteLine(string.Format("  Bates Range: {0}", result.BatesRange));
            Console.WriteLine(string.Format("  Volumes: {0}", result.VolumeCount));
            Console.WriteLine(string.Format("  DAT: {0}", result.DatFilePath));
            Console.WriteLine(string.Format("  OPT: {0}", result.OptFilePath));
            Console.WriteLine(string.Format("  Manifest: {0}", result.ManifestPath));
            if (result.ZipFilePath != null)
            {
                Console.WriteLine(string.Format("  ZIP: {0}", result.ZipFilePath));
            }
        }
    }
}
