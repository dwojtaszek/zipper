using System.Diagnostics;
using System.IO.Compression;
using System.Text;

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
        var placeholder = PlaceholderFiles.GetContent(request.FileType.ToLowerInvariant());

        // Generate files and collect records for load files
        var datRecords = new List<ProductionRecord>();
        var optRecords = new List<string>();

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
            byte[] nativeContent;
            if (OfficeFileGenerator.IsOfficeFormat(request.FileType))
            {
                var workItem = new FileWorkItem
                {
                    Index = i + 1,
                    FolderNumber = volumeIndex,
                    FolderName = volName,
                    FileName = $"{batesNumber}.{nativeExt}",
                    FilePathInZip = nativeRelPath,
                };
                nativeContent = OfficeFileGenerator.GenerateContent(request.FileType, workItem);
            }
            else if (request.FileType.ToLowerInvariant() == "eml")
            {
                var emlResult = EmlGenerationService.GenerateEmlContent((int)(i + 1), request.AttachmentRate);
                nativeContent = emlResult.Content;
            }
            else
            {
                nativeContent = placeholder;
            }

            await File.WriteAllBytesAsync(nativeFullPath, nativeContent);

            // Write text file
            var textFullPath = Path.Combine(productionPath, textRelPath);
            var textContent = $"Extracted text for document {batesNumber}. " +
                              Profiles.LoremIpsum.GetParagraph(random);
            await File.WriteAllTextAsync(textFullPath, textContent, encoding);

            // Write placeholder TIFF image (single-pixel stub)
            var imageFullPath = Path.Combine(productionPath, imageRelPath);
            await File.WriteAllBytesAsync(imageFullPath, PlaceholderFiles.GetContent("tiff"));

            // Collect DAT record data
            var record = new ProductionRecord
            {
                BatesNumber = batesNumber,
                NativePath = nativeRelPath.Replace(Path.DirectorySeparatorChar, '\\'),
                TextPath = textRelPath.Replace(Path.DirectorySeparatorChar, '\\'),
                ImagePath = imageRelPath.Replace(Path.DirectorySeparatorChar, '\\'),
                Custodian = $"Custodian {random.Next(1, Math.Max(2, request.CustodianCountOverride ?? 10))}",
                DateCreated = DateTime.Now.AddDays(-random.Next(1, 730)).ToString("yyyy-MM-dd"),
                FileSize = nativeContent.Length,
                FileType = nativeExt.ToUpperInvariant(),
                VolumeName = volName,
            };
            datRecords.Add(record);

            // OPT line: Bates,VolumeName,ImagePath,DocBreak,BoxBreak,FolderBreak,PageCount
            var docBreak = "Y";
            optRecords.Add($"{batesNumber},{volName},{imageRelPath.Replace(Path.DirectorySeparatorChar, '\\')},{docBreak},,1");

            // Progress reporting
            if ((i + 1) % 1000 == 0 || i == request.FileCount - 1)
            {
                Console.Write($"\r  Progress: {i + 1:N0} / {request.FileCount:N0} documents");
            }
        }

        Console.WriteLine();

        // Write DAT load file
        var datPath = Path.Combine(dataDir, "loadfile.dat");
        await WriteDatLoadFile(datPath, request, datRecords, encoding);

        // Write OPT load file
        var optPath = Path.Combine(dataDir, "loadfile.opt");
        await WriteOptLoadFile(optPath, optRecords, encoding);

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

    private static async Task WriteDatLoadFile(
        string datPath,
        FileGenerationRequest request,
        List<ProductionRecord> records,
        Encoding encoding)
    {
        var col = request.ColumnDelimiter;
        var quote = request.QuoteDelimiter;

        await using var stream = new FileStream(datPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        await using var writer = new StreamWriter(stream, encoding);

        // Header
        var headers = new[]
        {
            "DOCID", "BATES_NUMBER", "VOLUME", "NATIVE_PATH", "TEXT_PATH",
            "IMAGE_PATH", "CUSTODIAN", "DATE_CREATED", "FILE_SIZE", "FILE_TYPE",
        };
        var headerLine = string.Join(col, headers.Select(h => $"{quote}{h}{quote}"));
        await writer.WriteLineAsync(headerLine);

        // Data rows
        foreach (var record in records)
        {
            var fields = new[]
            {
                record.BatesNumber,
                record.BatesNumber,
                record.VolumeName,
                record.NativePath,
                record.TextPath,
                record.ImagePath,
                record.Custodian,
                record.DateCreated,
                record.FileSize.ToString(),
                record.FileType,
            };
            var line = string.Join(col, fields.Select(f => $"{quote}{f}{quote}"));
            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync();
    }

    private static async Task WriteOptLoadFile(
        string optPath,
        List<string> optLines,
        Encoding encoding)
    {
        await using var stream = new FileStream(optPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true);
        await using var writer = new StreamWriter(stream, encoding);

        foreach (var line in optLines)
        {
            await writer.WriteLineAsync(line);
        }

        await writer.FlushAsync();
    }
}

/// <summary>
/// Data record for a single document in a production set DAT.
/// </summary>
internal class ProductionRecord
{
    public string BatesNumber { get; set; } = string.Empty;

    public string NativePath { get; set; } = string.Empty;

    public string TextPath { get; set; } = string.Empty;

    public string ImagePath { get; set; } = string.Empty;

    public string Custodian { get; set; } = string.Empty;

    public string DateCreated { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string FileType { get; set; } = string.Empty;

    public string VolumeName { get; set; } = string.Empty;
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
