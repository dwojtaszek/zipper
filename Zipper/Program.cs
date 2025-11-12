using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Zipper
{
    public class Program
    {
        /// <summary>
        /// Entry point for the Zipper CLI application.
        /// Validates command line arguments and initiates file generation.
        /// </summary>
        /// <param name="args">Command line arguments</param>
        /// <returns>Exit code (0 for success, 1 for error)</returns>
        public static async Task<int> Main(string[] args)
        {
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
            Console.WriteLine($"Zipper v{version} https://github.com/dwojtaszek/zipper/");
            Console.WriteLine();

            // Validate and parse command line arguments
            var request = CommandLineValidator.ValidateAndParseArguments(args);
            if (request == null)
            {
                return 1; // Error already displayed by CommandLineValidator
            }

            await GenerateFiles(request);
            return 0;
        }

        static async Task GenerateFiles(FileGenerationRequest request)
        {
            Console.WriteLine("Starting parallel file generation...");
            Console.WriteLine(string.Format("  File Type: {0}", request.FileType));
            Console.WriteLine(string.Format("  Count: {0:N0}", request.FileCount));
            Console.WriteLine(string.Format("  Output Path: {0}", request.OutputPath));
            Console.WriteLine(string.Format("  Folders: {0}", request.Folders));
            Console.WriteLine(string.Format("  Encoding: {0}", request.Encoding));
            Console.WriteLine(string.Format("  Distribution: {0}", request.Distribution));
            if (request.WithMetadata) Console.WriteLine("  Metadata: Enabled");
            if (request.WithText) Console.WriteLine("  Extracted Text: Enabled");
            if (request.TargetZipSize.HasValue) Console.WriteLine(string.Format("  Target ZIP Size: {0} MB", request.TargetZipSize.Value / (1024 * 1024)));
            if (request.IncludeLoadFile) Console.WriteLine("  Load File: Will be included in zip archive.");

            try
            {
                // Use parallel file generator for improved performance
                using var generator = new ParallelFileGenerator();

                var result = await generator.GenerateFilesAsync(request);

                Console.WriteLine(string.Format("\n\nGeneration complete in {0:F1} seconds.", result.GenerationTime.TotalSeconds));
                Console.WriteLine(string.Format("  Archive created: {0}", result.ZipFilePath));
                Console.WriteLine(string.Format("  Performance: {0:F1} files/second", result.FilesPerSecond));
                if (!request.IncludeLoadFile)
                {
                    Console.WriteLine(string.Format("  Load file created: {0}", result.LoadFilePath));
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(string.Format("\nAn error occurred: {0}", ex.Message));
                return;
            }
        }

        static long EstimateCompressedSize(byte[] content, long count, bool withText)
        {
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                var entry = archive.CreateEntry("temp." + "txt");
                using var entryStream = entry.Open();
                entryStream.Write(content, 0, content.Length);

                if (withText)
                {
                    var textEntry = archive.CreateEntry("temp.txt");
                    using var textEntryStream = textEntry.Open();
                    textEntryStream.Write(PlaceholderFiles.ExtractedText, 0, PlaceholderFiles.ExtractedText.Length);
                }
            }
            return ms.Length * count;
        }
    }
}