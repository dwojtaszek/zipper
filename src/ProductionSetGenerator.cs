using System.Diagnostics;
using System.IO.Compression;

namespace Zipper;

/// <summary>
/// Generates a structured production set with DATA, NATIVES, TEXT, and IMAGES directories,
/// cross-referenced DAT and OPT load files, and an optional ZIP wrapper.
/// </summary>
internal static class ProductionSetGenerator
{
    /// <summary>
    /// Generates a complete production set.
    /// </summary>
    /// <param name="request">File generation request with production set settings.</param>
    /// <returns>Result containing paths and performance metrics.</returns>
    public static async Task<ProductionSetResult> GenerateAsync(FileGenerationRequest request)
    {
        var stopwatch = Stopwatch.StartNew();

        var productionName = $"PRODUCTION_{DateTime.Now:yyyyMMdd_HHmmss}";
        var productionPath = Path.Combine(request.OutputPath, productionName);

        // Create directory structure
        var dataDir = Path.Combine(productionPath, "DATA");
        var nativesDir = Path.Combine(productionPath, "NATIVES");
        var textDir = Path.Combine(productionPath, "TEXT");
        var imagesDir = Path.Combine(productionPath, "IMAGES");

        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(nativesDir);
        Directory.CreateDirectory(textDir);
        Directory.CreateDirectory(imagesDir);

#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        var random = request.Seed.HasValue ? new Random(request.Seed.Value) : new Random();
#pragma warning restore S2245
        var builder = new MetadataRowBuilder(request, random, DateTime.UtcNow);
        var batesConfig = request.BatesConfig
            ?? throw new InvalidOperationException("Production set requires Bates configuration. Specify --bates-prefix.");

        // Calculate volume distribution
        int volumeCount = (int)Math.Ceiling((double)request.FileCount / request.VolumeSize);

        // Pre-create volume subdirectories
        for (int v = 1; v <= volumeCount; v++)
        {
            var volName = $"VOL{v:D3}";
            Directory.CreateDirectory(Path.Combine(nativesDir, volName));
            Directory.CreateDirectory(Path.Combine(textDir, volName));
            Directory.CreateDirectory(Path.Combine(imagesDir, volName));
        }

        var encoding = EncodingHelper.GetEncodingOrDefault(request.Encoding);

        var fileGenerator = FileGeneratorFactory.Create(request.FileType, request)
            ?? throw new InvalidOperationException($"Unknown file type: {request.FileType}");

        // Generate files and collect records for load files
        var fileDataList = new List<FileData>();

        for (long i = 0; i < request.FileCount; i++)
        {
            int volumeIndex = (int)(i / request.VolumeSize) + 1;
            var volName = $"VOL{volumeIndex:D3}";
            var batesNumber = BatesNumberGenerator.Generate(batesConfig, i);

            var nativeExt = request.FileType.ToLowerInvariant();
            var nativeRelPath = Path.Combine("NATIVES", volName, $"{batesNumber}.{nativeExt}");
            var textRelPath = Path.Combine("TEXT", volName, $"{batesNumber}.txt");
            var imageRelPath = Path.Combine("IMAGES", volName, $"{batesNumber}.tif");

            // Write native file
            var nativeFullPath = Path.Combine(productionPath, nativeRelPath);
            var workItem = new FileWorkItem
            {
                Index = i + 1,
                FolderNumber = volumeIndex,
                FolderName = volName,
                FileName = $"{batesNumber}.{nativeExt}",
                FilePathInZip = nativeRelPath,
            };
            var generated = fileGenerator.Generate(workItem, request);
            var nativeContent = generated.Content;

            await File.WriteAllBytesAsync(nativeFullPath, nativeContent);

            // Write text file
            var textFullPath = Path.Combine(productionPath, textRelPath);
            var textContent = $"Extracted text for document {batesNumber}. " +
                              Profiles.LoremIpsum.GetParagraph(random);
            await File.WriteAllTextAsync(textFullPath, textContent, encoding);

            // Write placeholder TIFF image (single-pixel stub)
            var imageFullPath = Path.Combine(productionPath, imageRelPath);
            await File.WriteAllBytesAsync(imageFullPath, PlaceholderFiles.GetContent("tiff"));

            fileDataList.Add(new FileData
            {
                WorkItem = workItem,
                DataLength = nativeContent.Length,
            });

            // Progress reporting
            if ((i + 1) % 1000 == 0 || i == request.FileCount - 1)
            {
                Console.Write($"\r  Progress: {i + 1:N0} / {request.FileCount:N0} documents");
            }
        }

        Console.WriteLine();

        // Write DAT load file
        var datPath = Path.Combine(dataDir, "loadfile.dat");
        var datWriter = new LoadFiles.ProductionSetDatWriter();
        await using (var datStream = new FileStream(datPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            await datWriter.WriteAsync(datStream, request, fileDataList);
        }

        // Write OPT load file
        var optPath = Path.Combine(dataDir, "loadfile.opt");
        var optWriter = new LoadFiles.ProductionSetOptWriter();
        await using (var optStream = new FileStream(optPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            await optWriter.WriteAsync(optStream, request, fileDataList);
        }

        // Write manifest
        var batesStart = BatesNumberGenerator.Generate(batesConfig, 0);
        var batesEnd = BatesNumberGenerator.Generate(batesConfig, request.FileCount - 1);
        var manifestPath = await ProductionManifestWriter.WriteAsync(
            productionPath, request, batesStart, batesEnd, volumeCount, stopwatch.Elapsed);

        // Optionally wrap in ZIP
        string? zipPath = null;
        if (request.ProductionZip)
        {
            zipPath = Path.Combine(request.OutputPath, $"{productionName}.zip");
            Console.Write("  Creating ZIP archive...");
            ZipFile.CreateFromDirectory(productionPath, zipPath, CompressionLevel.Optimal, true);
            Console.WriteLine(" done.");
        }

        stopwatch.Stop();

        return new ProductionSetResult
        {
            ProductionPath = productionPath,
            ZipFilePath = zipPath,
            DatFilePath = datPath,
            OptFilePath = optPath,
            ManifestPath = manifestPath,
            TotalDocuments = request.FileCount,
            BatesRange = $"{batesStart} - {batesEnd}",
            VolumeCount = volumeCount,
            GenerationTime = stopwatch.Elapsed,
        };
    }
}

/// <summary>
/// Result of a production set generation operation.
/// </summary>
internal class ProductionSetResult
{
    public string ProductionPath { get; set; } = string.Empty;

    public string? ZipFilePath { get; set; }

    public string DatFilePath { get; set; } = string.Empty;

    public string OptFilePath { get; set; } = string.Empty;

    public string ManifestPath { get; set; } = string.Empty;

    public long TotalDocuments { get; set; }

    public string BatesRange { get; set; } = string.Empty;

    public int VolumeCount { get; set; }

    public TimeSpan GenerationTime { get; set; }
}
