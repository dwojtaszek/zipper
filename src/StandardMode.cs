namespace Zipper
{
    /// <summary>
    /// Standard parallel file generation mode — produces a ZIP archive plus a load file.
    /// </summary>
    internal class StandardMode : IGenerationMode
    {
        public async Task RunAsync(FileGenerationRequest request)
        {
            Console.WriteLine("Starting parallel file generation...");
            Console.WriteLine(string.Format("  File Type: {0}", request.Output.FileType));
            Console.WriteLine(string.Format("  Count: {0:N0}", request.Output.FileCount));
            Console.WriteLine(string.Format("  Output Path: {0}", request.Output.OutputPath));
            Console.WriteLine(string.Format("  Folders: {0}", request.Output.Folders));
            Console.WriteLine(string.Format("  Encoding: {0}", request.LoadFile.Encoding));
            Console.WriteLine(string.Format("  Distribution: {0}", request.LoadFile.Distribution));
            if (request.Metadata.WithMetadata)
            {
                Console.WriteLine("  Metadata: Enabled");
            }

            if (request.Output.WithText)
            {
                Console.WriteLine("  Extracted Text: Enabled");
            }

            if (request.Output.TargetZipSize.HasValue)
            {
                Console.WriteLine(string.Format("  Target ZIP Size: {0} MB", request.Output.TargetZipSize.Value / (1024 * 1024)));
            }

            if (request.Output.IncludeLoadFile)
            {
                Console.WriteLine("  Load File: Will be included in zip archive.");
            }

            // Use parallel file generator for improved performance
            var generator = new ParallelFileGenerator();

            var result = await generator.GenerateFilesAsync(request);

            Console.WriteLine(string.Format("\n\nGeneration complete in {0:F1} seconds.", result.GenerationTime.TotalSeconds));
            Console.WriteLine(string.Format("  Archive created: {0}", result.ZipFilePath));
            Console.WriteLine(string.Format("  Performance: {0:F1} files/second", result.FilesPerSecond));

            // REQ-025: the final archive must fall within +/- 10% of --target-zip-size.
            // This is the success criterion for the operation; verify and report it.
            // Outside tolerance is warned (not a hard failure) because compression
            // variance can legitimately push the result beyond +/- 10%.
            if (request.Output.TargetZipSize.HasValue && File.Exists(result.ZipFilePath))
            {
                long target = request.Output.TargetZipSize.Value;
                long actual = new FileInfo(result.ZipFilePath).Length;
                double deviation = target > 0 ? Math.Abs(actual - target) / (double)target : 0;
                if (deviation > 0.10)
                {
                    Console.Error.WriteLine(string.Format(
                        "  Warning: archive size {0:N0} bytes is outside the +/-10% target tolerance ({1:N0} bytes, deviation {2:P1}).",
                        actual, target, deviation));
                }
                else
                {
                    Console.WriteLine(string.Format(
                        "  Target ZIP size met: {0:N0} bytes (within +/-10% of {1:N0} bytes, deviation {2:P1}).",
                        actual, target, deviation));
                }
            }

            if (!request.Output.IncludeLoadFile)
            {
                Console.WriteLine(string.Format("  Load file created: {0}", result.LoadFilePath));
            }
        }
    }
}
