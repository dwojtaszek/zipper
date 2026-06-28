namespace Zipper
{
    /// <summary>
    /// Production Set generation mode — emits a Bates-numbered, volumed production with DAT, OPT, and manifest sidecars.
    /// </summary>
    internal class ProductionSetMode : IGenerationMode
    {
        private readonly Func<FileGenerationRequest, CancellationToken, Task<ProductionSetResult>> _generate;

        public ProductionSetMode(Func<FileGenerationRequest, CancellationToken, Task<ProductionSetResult>> generate)
        {
            ArgumentNullException.ThrowIfNull(generate);
            _generate = generate;
        }

        public async Task RunAsync(FileGenerationRequest request, CancellationToken cancellationToken = default)
        {
            Console.WriteLine("Starting production set generation...");
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  File Type: {0}", request.Output.FileType));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Count: {0:N0}", request.Output.FileCount));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Output Path: {0}", request.Output.OutputPath));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Volume Size: {0:N0} files/volume", request.Production.VolumeSize));
            var batesPrefix = request.Bates?.Prefix ?? string.Empty;
            var batesStart = request.Bates?.Start ?? 1;
            var batesDigits = request.Bates?.Digits ?? 8;
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Bates: {0}{1}", batesPrefix, batesStart.ToString($"D{batesDigits}", System.Globalization.CultureInfo.InvariantCulture)));
            if (request.Production.ProductionZip)
            {
                Console.WriteLine("  ZIP Output: Enabled");
            }

            var result = await _generate(request, cancellationToken).ConfigureAwait(false);

            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "\n\nProduction set complete in {0:F1} seconds.", result.GenerationTime.TotalSeconds));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Production: {0}", result.ProductionPath));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Documents: {0:N0}", result.TotalDocuments));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Bates Range: {0}", result.BatesRange));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Volumes: {0}", result.VolumeCount));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  DAT: {0}", result.DatFilePath));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  OPT: {0}", result.OptFilePath));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Manifest: {0}", result.ManifestPath));
            if (result.ZipFilePath is not null)
            {
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  ZIP: {0}", result.ZipFilePath));
            }
        }
    }
}
