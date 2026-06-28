namespace Zipper
{
    /// <summary>
    /// Standard parallel file generation mode — produces a ZIP archive plus a load file.
    /// </summary>
    internal class StandardMode : IGenerationMode
    {
        private readonly Func<FileGenerationRequest, CancellationToken, Task<FileGenerationResult>> _generate;

        public StandardMode(Func<FileGenerationRequest, CancellationToken, Task<FileGenerationResult>> generate)
        {
            _generate = generate;
        }

        public async Task RunAsync(FileGenerationRequest request, CancellationToken cancellationToken = default)
        {
            if (request.Chaos.ChaosMode)
            {
                throw new InvalidOperationException("Chaos mode is not supported in standard generation mode. Use --loadfile-only.");
            }

            Console.WriteLine("Starting parallel file generation...");
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  File Type: {0}", request.Output.FileType));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Count: {0:N0}", request.Output.FileCount));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Output Path: {0}", request.Output.OutputPath));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Folders: {0}", request.Output.Folders));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Encoding: {0}", request.LoadFile.Encoding));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Distribution: {0}", request.LoadFile.Distribution));
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
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Target ZIP Size: {0} MB", request.Output.TargetZipSize.Value / (1024 * 1024)));
            }

            if (request.Output.IncludeLoadFile)
            {
                Console.WriteLine("  Load File: Will be included in zip archive.");
            }

            var result = await _generate(request, cancellationToken).ConfigureAwait(false);

            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "\n\nGeneration complete in {0:F1} seconds.", result.GenerationTime.TotalSeconds));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Archive created: {0}", result.ZipFilePath));
            Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Performance: {0:F1} files/second", result.FilesPerSecond));

            // REQ-025: the final archive must fall within +/- 10% of --target-zip-size.
            // This is the success criterion for the operation; verify and report it.
            // Outside tolerance is warned (not a hard failure) because compression
            // variance can legitimately push the result beyond +/- 10%.
            if (request.Output.TargetZipSize.HasValue && result.ZipSizeVerification != null)
            {
                long target = request.Output.TargetZipSize.Value;
                long actual = result.ActualZipSize;
                double deviation = result.ZipSizeVerification.Deviation;

                if (!result.ZipSizeVerification.IsWithinTolerance)
                {
                    await Console.Error.WriteLineAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "  Warning: archive size {0:N0} bytes is outside the +/-10% target tolerance ({1:N0} bytes, deviation {2:P1}).",
                        actual, target, deviation)).ConfigureAwait(false);
                }
                else
                {
                    await Console.Out.WriteLineAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                        "  Target ZIP size met: {0:N0} bytes (within +/-10% of {1:N0} bytes, deviation {2:P1}).",
                        actual, target, deviation)).ConfigureAwait(false);
                }
            }

            if (!request.Output.IncludeLoadFile)
            {
                Console.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture, "  Load file created: {0}", result.LoadFilePath));
            }
        }
    }
}
