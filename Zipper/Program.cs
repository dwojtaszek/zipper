using System;
using System.IO;
using System.IO.Compression;
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
            }
        }

        if (fileType is null || count is null || outputPath is null)
        {
            Console.Error.WriteLine("Error: Missing required arguments.");
            Console.Error.WriteLine("Usage: dotnet run -- --type <pdf|jpg|tiff> --count <number> --output-path <directory> [--folders <number>] [--encoding <UTF-8|UTF-16|ANSI>] [--distribution <proportional|gaussian|exponential>]");
            return 1;
        }

        if (folders < 1 || folders > 100)
        {
            Console.Error.WriteLine("Error: Number of folders must be between 1 and 100.");
            return 1;
        }

        Encoding? encoding = GetEncodingFromName(encodingName);
        if (encoding is null)
        {
            Console.Error.WriteLine($"Error: Invalid encoding '{encodingName}'. Supported values are UTF-8, UTF-16, ANSI.");
            return 1;
        }

        DistributionType? distributionType = GetDistributionFromName(distributionName);
        if (distributionType is null)
        {
            Console.Error.WriteLine($"Error: Invalid distribution '{distributionName}'. Supported values are proportional, gaussian, exponential.");
            return 1;
        }

        await GenerateFiles(fileType, count.Value, outputPath, folders, encoding, distributionType.Value);
        return 0;
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
            "UTF-8" => Encoding.UTF8,
            "ANSI" => CodePagesEncodingProvider.Instance.GetEncoding(1252),
            "UTF-16" => Encoding.Unicode,
            _ => null
        };
    }

    static async Task GenerateFiles(string fileType, long count, DirectoryInfo outputDir, int numFolders, Encoding encoding, DistributionType distributionType)
    {
        Console.WriteLine("Starting file generation...");
        Console.WriteLine($"  File Type: {fileType}");
        Console.WriteLine($"  Count: {count:N0}");
        Console.WriteLine($"  Output Path: {outputDir.FullName}");
        Console.WriteLine($"  Folders: {numFolders}");
        Console.WriteLine($"  Encoding: {encoding.EncodingName}");
        Console.WriteLine($"  Distribution: {distributionType}");

        var lowerFileType = fileType.ToLower();
        if (lowerFileType is not ("pdf" or "jpg" or "tiff"))
        {
            Console.Error.WriteLine("Error: Invalid file type. Must be 'pdf', 'jpg', or 'tiff'.");
            return;
        }

        outputDir.Create();

        var baseFileName = $"archive_{DateTime.Now:yyyyMMdd_HHmmss}";
        var zipFilePath = Path.Combine(outputDir.FullName, $"{baseFileName}.zip");
        var loadFilePath = Path.Combine(outputDir.FullName, $"{baseFileName}.dat");
        
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var placeholderContent = PlaceholderFiles.GetContent(lowerFileType);
        if (placeholderContent.Length == 0)
        {
            Console.Error.WriteLine("Error: Could not retrieve placeholder content.");
            return;
        }

        try
        {
            using var archiveStream = new FileStream(zipFilePath, FileMode.Create);
            using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, true);
            using var loadFileWriter = new StreamWriter(loadFilePath, false, encoding);
            
            const char colDelim = (char)20;
            const char quote = (char)254;
            await loadFileWriter.WriteLineAsync($"{quote}Control Number{quote}{colDelim}{quote}File Path{quote}");

            for (long i = 1; i <= count; i++)
            {
                var folderNumber = FileDistributionHelper.GetFolderNumber(i, count, numFolders, distributionType);
                var folderName = $"folder_{folderNumber:D3}";
                
                var docId = $"DOC{i:D8}";
                var fileName = $"{i:D8}.{lowerFileType}";
                var filePathInZip = $"{folderName}/{fileName}";

                var entry = archive.CreateEntry(filePathInZip, CompressionLevel.Optimal);
                using (var entryStream = entry.Open())
                {
                    await entryStream.WriteAsync(placeholderContent, 0, placeholderContent.Length);
                }

                await loadFileWriter.WriteLineAsync($"{quote}{docId}{quote}{colDelim}{quote}{filePathInZip}{quote}");

                if (i % 1000 == 0)
                {
                    Console.Write($"\rProgress: {i:N0} / {count:N0} files created...");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nAn error occurred: {ex.Message}");
            return;
        }

        Console.WriteLine($"\n\nGeneration complete.");
        Console.WriteLine($"  Archive created: {zipFilePath}");
        Console.WriteLine($"  Load file created: {loadFilePath}");
    }
}
}