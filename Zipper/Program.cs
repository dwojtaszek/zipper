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
        public static async Task<int> Main(string[] args)
        {
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
            Console.WriteLine($"Zipper v{version} https://github.com/dwojtaszek/zipper/");
            Console.WriteLine();

            string? fileType = null;
            long? count = null;
            DirectoryInfo? outputPath = null;
            int folders = 1;
            string encodingName = "UTF-8";
            string distributionName = "proportional";
            bool withMetadata = false;
            bool withText = false;
            int attachmentRate = 0;
            string? targetZipSize = null;
            bool includeLoadFile = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--type":
                        if (i + 1 < args.Length) fileType = args[++i];
                        break;
                    case "--count":
                        if (i + 1 < args.Length && long.TryParse(args[++i], out var c)) count = c;
                        break;
                    case "--output-path":
                        // TODO: Validate the output path to prevent path traversal vulnerabilities.
                        if (i + 1 < args.Length) outputPath = new DirectoryInfo(args[++i]);
                        break;
                    case "--folders":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var f)) folders = f;
                        break;
                    case "--encoding":
                        if (i + 1 < args.Length) encodingName = args[++i];
                        break;
                    case "--distribution":
                        if (i + 1 < args.Length) distributionName = args[++i];
                        break;
                    case "--with-metadata":
                        withMetadata = true;
                        break;
                    case "--with-text":
                        withText = true;
                        break;
                    case "--attachment-rate":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var ar)) attachmentRate = ar;
                        break;
                    case "--target-zip-size":
                        if (i + 1 < args.Length) targetZipSize = args[++i];
                        break;
                    case "--include-load-file":
                        includeLoadFile = true;
                        break;
                }
            }

            if (fileType is null || count is null || outputPath is null)
            {
                var exeName = Process.GetCurrentProcess().ProcessName;
                Console.Error.WriteLine("Error: Missing required arguments.");
                Console.Error.WriteLine($"Usage: {exeName} --type <pdf|jpg|tiff|eml> --count <number> --output-path <directory> [--folders <number>] [--encoding <UTF-8|UTF-16|ANSI>] [--distribution <proportional|gaussian|exponential>] [--attachment-rate <percentage>] [--target-zip-size <size>] [--include-load-file]");
                return 1;
            }

            if (!string.IsNullOrEmpty(targetZipSize) && count is null)
            {
                Console.Error.WriteLine("Error: --target-zip-size requires --count to be specified.");
                return 1;
            }

            if (folders < 1 || folders > 100)
            {
                Console.Error.WriteLine("Error: Number of folders must be between 1 and 100.");
                return 1;
            }

            if (attachmentRate < 0 || attachmentRate > 100)
            {
                Console.Error.WriteLine("Error: Attachment rate must be between 0 and 100.");
                return 1;
            }

            Encoding? encoding = GetEncodingFromName(encodingName);
            if (encoding is null)
            {
                Console.Error.WriteLine(string.Format("Error: Invalid encoding '{0}'. Supported values are UTF-8, UTF-16, ANSI.", encodingName));
                return 1;
            }

            DistributionType? distributionType = GetDistributionFromName(distributionName);
            if (distributionType is null)
            {
                Console.Error.WriteLine(string.Format("Error: Invalid distribution '{0}'. Supported values are proportional, gaussian, exponential.", distributionName));
                return 1;
            }

            long? targetSizeInBytes = null;
            if (!string.IsNullOrEmpty(targetZipSize))
            {
                targetSizeInBytes = ParseSize(targetZipSize);
                if (targetSizeInBytes is null)
                {
                    Console.Error.WriteLine("Error: Invalid format for --target-zip-size. Use KB, MB, GB, etc. (e.g., 500MB, 10GB).");
                    return 1;
                }
            }

            if (fileType.ToLower() == "eml")
            {
                await GenerateEmlFiles(count.Value, outputPath, folders, encoding, distributionType.Value, attachmentRate, withMetadata, withText, includeLoadFile);
            }
            else
            {
                await GenerateFiles(fileType, count.Value, outputPath, folders, encoding, distributionType.Value, withMetadata, withText, targetSizeInBytes, includeLoadFile, attachmentRate);
            }
            return 0;
        }

        static long? ParseSize(string size)
        {
            size = size.ToUpper().Trim();
            long multiplier = 1;
            if (size.EndsWith("KB"))
            {
                multiplier = 1024;
                size = size.Substring(0, size.Length - 2);
            }
            else if (size.EndsWith("MB"))
            {
                multiplier = 1024 * 1024;
                size = size.Substring(0, size.Length - 2);
            }
            else if (size.EndsWith("GB"))
            {
                multiplier = 1024 * 1024 * 1024;
                size = size.Substring(0, size.Length - 2);
            }

            if (long.TryParse(size, out long value))
            {
                return value * multiplier;
            }

            return null;
        }

        static DistributionType? GetDistributionFromName(string name)
        {
            return name.ToUpperInvariant() switch
            {
                "PROPORTIONAL" => DistributionType.Proportional,
                "GAUSSIAN" => DistributionType.Gaussian,
                "EXPONENTIAL" => DistributionType.Exponential,
                _ => null
            };
        }

        static Encoding? GetEncodingFromName(string name)
        {
            return name.ToUpperInvariant() switch
            {
                "UTF-8" => new UTF8Encoding(false),
                "ANSI" => CodePagesEncodingProvider.Instance.GetEncoding(1252),
                "UTF-16" => new UnicodeEncoding(false, false),
                _ => null
            };
        }

        static async Task GenerateFiles(string fileType, long count, DirectoryInfo outputDir, int numFolders, Encoding encoding, DistributionType distributionType, bool withMetadata, bool withText, long? targetZipSize, bool includeLoadFile, int attachmentRate = 0)
        {
            Console.WriteLine("Starting parallel file generation...");
            Console.WriteLine(string.Format("  File Type: {0}", fileType));
            Console.WriteLine(string.Format("  Count: {0:N0}", count));
            Console.WriteLine(string.Format("  Output Path: {0}", outputDir.FullName));
            Console.WriteLine(string.Format("  Folders: {0}", numFolders));
            Console.WriteLine(string.Format("  Encoding: {0}", encoding.EncodingName));
            Console.WriteLine(string.Format("  Distribution: {0}", distributionType));
            if (withMetadata) Console.WriteLine("  Metadata: Enabled");
            if (withText) Console.WriteLine("  Extracted Text: Enabled");
            if (targetZipSize.HasValue) Console.WriteLine(string.Format("  Target ZIP Size: {0} MB", targetZipSize.Value / (1024 * 1024)));
            if (includeLoadFile) Console.WriteLine("  Load File: Will be included in the zip archive.");

            try
            {
                // Use parallel file generator for improved performance
                using var generator = new ParallelFileGenerator();

                var request = new FileGenerationRequest
                {
                    OutputPath = outputDir.FullName,
                    FileCount = count,
                    FileType = fileType.ToLower(),
                    Folders = numFolders,
                    Concurrency = PerformanceConstants.DefaultConcurrency,
                    WithMetadata = withMetadata,
                    WithText = withText,
                    TargetZipSize = targetZipSize,
                    IncludeLoadFile = includeLoadFile,
                    Distribution = distributionType,
                    Encoding = encoding.EncodingName,
                    AttachmentRate = attachmentRate
                };

                var result = await generator.GenerateFilesAsync(request);

                Console.WriteLine(string.Format("\n\nGeneration complete in {0:F1} seconds.", result.GenerationTime.TotalSeconds));
                Console.WriteLine(string.Format("  Archive created: {0}", result.ZipFilePath));
                Console.WriteLine(string.Format("  Performance: {0:F1} files/second", result.FilesPerSecond));
                if (!includeLoadFile)
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

        static async Task GenerateEmlFiles(long count, DirectoryInfo outputDir, int numFolders, Encoding encoding, DistributionType distributionType, int attachmentRate, bool withMetadata, bool withText, bool includeLoadFile)
        {
            Console.WriteLine("Starting EML file generation...");
            Console.WriteLine($"  Count: {count:N0}");
            Console.WriteLine($"  Output Path: {outputDir.FullName}");
            Console.WriteLine($"  Folders: {numFolders}");
            Console.WriteLine($"  Encoding: {encoding.EncodingName}");
            Console.WriteLine($"  Distribution: {distributionType}");
            Console.WriteLine($"  Attachment Rate: {attachmentRate}%");
            if (withMetadata) Console.WriteLine("  Metadata: Will be included in load file.");
            if (withText) Console.WriteLine("  Text Files: Will be generated and included in load file.");
            if (includeLoadFile) Console.WriteLine("  Load File: Will be included in the zip archive.");

            outputDir.Create();

            var baseFileName = $"archive_{DateTime.Now:yyyyMMdd_HHmmss}";
            var zipFilePath = Path.Combine(outputDir.FullName, $"{baseFileName}.zip");
            var loadFileName = $"{baseFileName}.dat";
            var loadFilePath = Path.Combine(outputDir.FullName, loadFileName);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
                using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
                using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);
                var loadFileLines = new System.Collections.Concurrent.ConcurrentDictionary<long, string>();

                const char colDelim = (char)20;
                const char quote = (char)254;

                var headerBuilder = new System.Text.StringBuilder();
                headerBuilder.Append($"{quote}Control Number{quote}{colDelim}{quote}File Path{quote}{colDelim}{quote}To{quote}{colDelim}{quote}From{quote}{colDelim}{quote}Subject{quote}");
                if (withMetadata)
                {
                    headerBuilder.Append($"{colDelim}{quote}Custodian{quote}{colDelim}{quote}Author{quote}{colDelim}{quote}Sent Date{quote}{colDelim}{quote}Date Sent{quote}{colDelim}{quote}File Size{quote}");
                }
                else
                {
                    headerBuilder.Append($"{colDelim}{quote}Sent Date{quote}");
                }
                headerBuilder.Append($"{colDelim}{quote}Attachment{quote}");
                if (withText)
                {
                    headerBuilder.Append($"{colDelim}{quote}Extracted Text{quote}");
                }
                loadFileLines.TryAdd(0, headerBuilder.ToString());

                int concurrencyLevel = Math.Max(1, Environment.ProcessorCount / 2);
                using var semaphore = new System.Threading.SemaphoreSlim(concurrencyLevel);
                var tasks = new System.Collections.Generic.List<Task>();

                for (long i = 1; i <= count; i++)
                {
                    long currentIndex = i;
                    await semaphore.WaitAsync();
                    tasks.Add(Task.Run(() =>
                    {
                        try
                        {
                            var folderNumber = FileDistributionHelper.GetFolderNumber(currentIndex, count, numFolders, distributionType);
                            var folderName = $"folder_{folderNumber:D3}";

                            var docId = $"DOC{currentIndex:D8}";
                            var fileName = $"{currentIndex:D8}.eml";
                            var filePathInZip = $"{folderName}/{fileName}";

                            var to = $"recipient{currentIndex}@example.com";
                            var from = GetRandomAuthor() + "@example.com";
                            var subject = $"Test Email {currentIndex}";
                            var sentDate = GetRandomDate(DateTime.Now.AddYears(-1), DateTime.Now);
                            var body = $"This is the body of test email {currentIndex}.";

                            (string filename, byte[] content)? attachment = null;
                            if (Rng.Next(100) < attachmentRate)
                            {
                                attachment = PlaceholderFiles.GetRandomAttachment();
                            }

                            var emlContent = EmlFile.CreateEmlContent(to, from, subject, sentDate, body, attachment);

                            lock (archive)
                            {
                                var entry = archive.CreateEntry(filePathInZip, CompressionLevel.Optimal);
                                using (var entryStream = entry.Open())
                                {
                                    entryStream.Write(emlContent, 0, emlContent.Length);
                                }

                                if (attachment.HasValue)
                                {
                                    var attachmentFileName = $"{currentIndex:D8}_attachment_{attachment.Value.filename}";
                                    var attachmentFilePathInZip = $"{folderName}/{attachmentFileName}";

                                    var attachmentEntry = archive.CreateEntry(attachmentFilePathInZip, CompressionLevel.Optimal);
                                    using (var attachmentEntryStream = attachmentEntry.Open())
                                    {
                                        attachmentEntryStream.Write(attachment.Value.content, 0, attachment.Value.content.Length);
                                    }

                                    if (withText)
                                    {
                                        var attachmentTextFileName = $"{currentIndex:D8}_attachment_{Path.GetFileNameWithoutExtension(attachment.Value.filename)}.txt";
                                        var attachmentTextFilePathInZip = $"{folderName}/{attachmentTextFileName}";

                                        var attachmentTextEntry = archive.CreateEntry(attachmentTextFilePathInZip, CompressionLevel.Optimal);
                                        using (var attachmentTextEntryStream = attachmentTextEntry.Open())
                                        {
                                            attachmentTextEntryStream.Write(PlaceholderFiles.ExtractedText, 0, PlaceholderFiles.ExtractedText.Length);
                                        }
                                    }
                                }

                                if (withText)
                                {
                                    var textFileName = $"{currentIndex:D8}.txt";
                                    var textFilePathInZip = $"{folderName}/{textFileName}";
                                    var textEntry = archive.CreateEntry(textFilePathInZip, CompressionLevel.Optimal);
                                    using (var entryStream = textEntry.Open())
                                    {
                                        entryStream.Write(PlaceholderFiles.EmlExtractedText, 0, PlaceholderFiles.EmlExtractedText.Length);
                                    }
                                }
                            }
                            
                            var lineBuilder = new System.Text.StringBuilder();
                            lineBuilder.Append($"{quote}{docId}{quote}{colDelim}{quote}{filePathInZip}{quote}{colDelim}{quote}{to}{quote}{colDelim}{quote}{from}{quote}{colDelim}{quote}{subject}{quote}");

                            if (withMetadata)
                            {
                                var fileSize = emlContent.Length;
                                var author = from.Contains('@') ? from.Split('@')[0] : from;
                                var custodian = GetRandomAuthor();
                                lineBuilder.Append($"{colDelim}{quote}{custodian}{quote}{colDelim}{quote}{author}{quote}{colDelim}{quote}{sentDate:yyyy-MM-dd HH:mm:ss}{quote}{colDelim}{quote}{sentDate:yyyy-MM-dd}{quote}{colDelim}{quote}{fileSize}{quote}");
                            }
                            else
                            {
                                lineBuilder.Append($"{colDelim}{quote}{sentDate:yyyy-MM-dd HH:mm:ss}{quote}");
                            }

                            var attachmentName = attachment.HasValue ? Path.GetFileName(attachment.Value.filename) : "";
                            lineBuilder.Append($"{colDelim}{quote}{attachmentName}{quote}");

                            if (withText)
                            {
                                var textFilePath = $"{folderName}/{currentIndex:D8}.txt";
                                lineBuilder.Append($"{colDelim}{quote}{textFilePath}{quote}");
                            }
                            var line = lineBuilder.ToString();
                            loadFileLines.TryAdd(currentIndex, line);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);

                var sortedLines = loadFileLines.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value);
                if (includeLoadFile)
                {
                    var loadFileEntry = archive.CreateEntry(loadFileName, CompressionLevel.Optimal);
                    using var entryStream = loadFileEntry.Open();
                    using var writer = new StreamWriter(entryStream, encoding);
                    foreach (var line in sortedLines)
                    {
                        await writer.WriteLineAsync(line);
                    }
                }
                else
                {
                    await File.WriteAllLinesAsync(loadFilePath, sortedLines, encoding);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\nAn error occurred: {ex.Message}");
                return;
            }

            Console.WriteLine($"\n\nGeneration complete.");
            Console.WriteLine($"  Archive created: {zipFilePath}");
            if (!includeLoadFile)
            {
                Console.WriteLine($"  Load file created: {loadFilePath}");
            }
        }

        static readonly Random Rng = new Random();
        static readonly string[] Authors = { "Alice", "Bob", "Charlie", "Diana", "Eve", "Frank" };

        static DateTime GetRandomDate(DateTime start, DateTime end)
        {
            int range = (end - start).Days;
            return start.AddDays(Rng.Next(range));
        }

        static string GetRandomAuthor()
        {
            return Authors[Rng.Next(Authors.Length)];
        }
    }
}
