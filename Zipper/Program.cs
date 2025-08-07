using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Zipper
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
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
                }
            }

            if (fileType is null || count is null || outputPath is null)
            {
                var exeName = Process.GetCurrentProcess().ProcessName;
                Console.Error.WriteLine("Error: Missing required arguments.");
                Console.Error.WriteLine($"Usage: {exeName} --type <pdf|jpg|tiff|eml> --count <number> --output-path <directory> [--folders <number>] [--encoding <UTF-8|UTF-16|ANSI>] [--distribution <proportional|gaussian|exponential>] [--attachment-rate <percentage>] [--target-zip-size <size>]");
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
                await GenerateEmlFiles(count.Value, outputPath, folders, encoding, distributionType.Value, attachmentRate);
            }
            else
            {
                await GenerateFiles(fileType, count.Value, outputPath, folders, encoding, distributionType.Value, withMetadata, withText, targetSizeInBytes);
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

        static async Task GenerateFiles(string fileType, long count, DirectoryInfo outputDir, int numFolders, Encoding encoding, DistributionType distributionType, bool withMetadata, bool withText, long? targetZipSize)
        {
            Console.WriteLine("Starting file generation...");
            Console.WriteLine(string.Format("  File Type: {0}", fileType));
            Console.WriteLine(string.Format("  Count: {0:N0}", count));
            Console.WriteLine(string.Format("  Output Path: {0}", outputDir.FullName));
            Console.WriteLine(string.Format("  Folders: {0}", numFolders));
            Console.WriteLine(string.Format("  Encoding: {0}", encoding.EncodingName));
            Console.WriteLine(string.Format("  Distribution: {0}", distributionType));
            if (withMetadata) Console.WriteLine("  Metadata: Enabled");
            if (withText) Console.WriteLine("  Extracted Text: Enabled");
            if (targetZipSize.HasValue) Console.WriteLine(string.Format("  Target ZIP Size: {0} MB", targetZipSize.Value / (1024 * 1024)));

            var lowerFileType = fileType.ToLower();

            outputDir.Create();

            var baseFileName = string.Format("archive_{0:yyyyMMdd_HHmmss}", DateTime.Now);
            var zipFilePath = Path.Combine(outputDir.FullName, string.Format("{0}.zip", baseFileName));
            var loadFilePath = Path.Combine(outputDir.FullName, string.Format("{0}.dat", baseFileName));

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var placeholderContent = PlaceholderFiles.GetContent(lowerFileType);
            if (placeholderContent.Length == 0)
            {
                Console.Error.WriteLine("Error: Could not retrieve placeholder content.");
                return;
            }

            long paddingPerFile = 0;
            if (targetZipSize.HasValue)
            {
                long estimatedBaseSize = EstimateCompressedSize(placeholderContent, count, withText);
                if (estimatedBaseSize >= targetZipSize.Value)
                {
                    Console.Error.WriteLine(string.Format("Error: Estimated minimum size ({0} MB) already exceeds the target size ({1} MB).", estimatedBaseSize / (1024 * 1024), targetZipSize.Value / (1024 * 1024)));
                    return;
                }
                paddingPerFile = (targetZipSize.Value - estimatedBaseSize) / count;
            }

            try
            {
                using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
                using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);
                using var loadFileWriter = new StreamWriter(loadFilePath, false, encoding);

                const char colDelim = (char)20;
                const char quote = (char)254;

                var header = string.Format("{0}Control Number{0}{1}{0}File Path{0}", quote, colDelim);
                if (withMetadata)
                {
                    header += string.Format("{1}{0}Custodian{0}{1}{0}Date Sent{0}{1}{0}Author{0}{1}{0}File Size{0}", quote, colDelim);
                }
                if (withText)
                {
                    header += string.Format("{1}{0}Extracted Text{0}", quote, colDelim);
                }
                await loadFileWriter.WriteLineAsync(header);

                for (long i = 1; i <= count; i++)
                {
                    var folderNumber = FileDistributionHelper.GetFolderNumber(i, count, numFolders, distributionType);
                    var folderName = string.Format("folder_{0:D3}", folderNumber);
                    
                    var docId = string.Format("DOC{0:D8}", i);
                    var fileName = string.Format("{0:D8}.{1}", i, lowerFileType);
                    var filePathInZip = string.Format("{0}/{1}", folderName, fileName);

                    var entry = archive.CreateEntry(filePathInZip, CompressionLevel.Optimal);
                    using (var entryStream = entry.Open())
                    {
                        await entryStream.WriteAsync(placeholderContent, 0, placeholderContent.Length);
                        if (paddingPerFile > 0)
                        {
                            var padding = new byte[paddingPerFile];
                            RandomNumberGenerator.Fill(padding);
                            await entryStream.WriteAsync(padding, 0, padding.Length);
                        }
                    }

                    var line = string.Format("{0}{1}{0}{2}{0}{3}{0}", quote, docId, colDelim, filePathInZip);
                    if (withMetadata)
                    {
                        var custodian = numFolders > 1 ? string.Format("Custodian {0}", folderNumber) : "Custodian 1";
                        var dateSent = GetRandomDate(DateTime.Now.AddYears(-5), DateTime.Now).ToString("yyyy-MM-dd");
                        var author = GetRandomAuthor();
                        var fileSize = placeholderContent.Length + paddingPerFile;
                        line += string.Format("{5}{0}{1}{0}{5}{0}{2}{0}{5}{0}{3}{0}{5}{0}{4}{0}", quote, custodian, dateSent, author, fileSize, colDelim);
                    }

                    if (withText)
                    {
                        var textFileName = string.Format("{0:D8}.txt", i);
                        var textFilePathInZip = string.Format("{0}/{1}", folderName, textFileName);
                        var textEntry = archive.CreateEntry(textFilePathInZip, CompressionLevel.Optimal);
                        using (var entryStream = textEntry.Open())
                        {
                            await entryStream.WriteAsync(PlaceholderFiles.ExtractedText, 0, PlaceholderFiles.ExtractedText.Length);
                        }
                        line += string.Format("{1}{0}{2}{0}", quote, colDelim, textFilePathInZip);
                    }

                    await loadFileWriter.WriteLineAsync(line);

                    if (i % 1000 == 0)
                    {
                        Console.Write(string.Format("\rProgress: {0:N0} / {1:N0} files created...", i, count));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(string.Format("\nAn error occurred: {0}", ex.Message));
                return;
            }

            Console.WriteLine(string.Format("\n\nGeneration complete."));
            Console.WriteLine(string.Format("  Archive created: {0}", zipFilePath));
            Console.WriteLine(string.Format("  Load file created: {0}", loadFilePath));
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

        static async Task GenerateEmlFiles(long count, DirectoryInfo outputDir, int numFolders, Encoding encoding, DistributionType distributionType, int attachmentRate)
        {
            Console.WriteLine("Starting EML file generation...");
            Console.WriteLine(string.Format("  Count: {0:N0}", count));
            Console.WriteLine(string.Format("  Output Path: {0}", outputDir.FullName));
            Console.WriteLine(string.Format("  Folders: {0}", numFolders));
            Console.WriteLine(string.Format("  Encoding: {0}", encoding.EncodingName));
            Console.WriteLine(string.Format("  Distribution: {0}", distributionType));
            Console.WriteLine(string.Format("  Attachment Rate: {0}%", attachmentRate));

            outputDir.Create();

            var baseFileName = string.Format("archive_{0:yyyyMMdd_HHmmss}", DateTime.Now);
            var zipFilePath = Path.Combine(outputDir.FullName, string.Format("{0}.zip", baseFileName));
            var loadFilePath = Path.Combine(outputDir.FullName, string.Format("{0}.dat", baseFileName));

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
                using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
                using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);
                using var loadFileWriter = new StreamWriter(loadFilePath, false, encoding);

                const char colDelim = (char)20;
                const char quote = (char)254;

                var header = string.Format("{0}Control Number{0}{1}{0}File Path{0}{1}{0}To{0}{1}{0}From{0}{1}{0}Subject{0}{1}{0}Sent Date{0}{1}{0}Attachment{0}", quote, colDelim);
                await loadFileWriter.WriteLineAsync(header);

                for (long i = 1; i <= count; i++)
                {
                    var folderNumber = FileDistributionHelper.GetFolderNumber(i, count, numFolders, distributionType);
                    var folderName = string.Format("folder_{0:D3}", folderNumber);

                    var docId = string.Format("DOC{0:D8}", i);
                    var fileName = string.Format("{0:D8}.eml", i);
                    var filePathInZip = string.Format("{0}/{1}", folderName, fileName);

                    var to = string.Format("recipient{0}@example.com", i);
                    var from = GetRandomAuthor() + "@example.com";
                    var subject = string.Format("Test Email {0}", i);
                    var sentDate = GetRandomDate(DateTime.Now.AddYears(-1), DateTime.Now);
                    var body = string.Format("This is the body of test email {0}.", i);

                    (string filename, byte[] content)? attachment = null;
                    string attachmentName = "";
                    if (Rng.Next(100) < attachmentRate)
                    {
                        attachment = PlaceholderFiles.GetRandomAttachment();
                        if (attachment.HasValue)
                        {
                            attachmentName = attachment.Value.filename;
                        }
                    }

                    var emlContent = EmlFile.CreateEmlContent(to, from, subject, sentDate, body, attachment);

                    var entry = archive.CreateEntry(filePathInZip, CompressionLevel.Optimal);
                    using (var entryStream = entry.Open())
                    {
                        await entryStream.WriteAsync(emlContent, 0, emlContent.Length);
                    }

                    var line = string.Format("{0}{1}{0}{2}{0}{3}{0}{2}{0}{4}{0}{2}{0}{5}{0}{2}{0}{6:yyyy-MM-dd HH:mm:ss}{0}{2}{0}{7}{0}", quote, docId, colDelim, to, from, subject, sentDate, attachmentName);
                    await loadFileWriter.WriteLineAsync(line);

                    if (i % 1000 == 0)
                    {
                        Console.Write(string.Format("\rProgress: {0:N0} / {1:N0} files created...", i, count));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(string.Format("\nAn error occurred: {0}", ex.Message));
                return;
            }

            Console.WriteLine(string.Format("\n\nGeneration complete."));
            Console.WriteLine(string.Format("  Archive created: {0}", zipFilePath));
            Console.WriteLine(string.Format("  Load file created: {0}", loadFilePath));
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