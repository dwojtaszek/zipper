using System.Diagnostics;
using System.IO.Compression;
using Zipper.Profiles.Data;


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
    public static async Task<ProductionSetResult> GenerateAsync(FileGenerationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Chaos.ChaosMode && !request.LoadfileOnly)
        {
            throw new InvalidOperationException("Chaos mode requires loadfile-only mode at the generation layer.");
        }

        var stopwatch = Stopwatch.StartNew();

        var productionName = $"PRODUCTION_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        var productionPath = Path.Combine(request.Output.OutputPath, productionName);

        try
        {
            return await GenerateCoreAsync(request, productionPath, productionName, stopwatch, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Clean up partial output on failure
            if (Directory.Exists(productionPath))
            {
                try
                {
                    Directory.Delete(productionPath, true);
                }
                catch
                {
                    // Best-effort cleanup; don't mask the original exception
                }
            }

            throw;
        }
    }

    private static async Task<ProductionSetResult> GenerateCoreAsync(
        FileGenerationRequest request, string productionPath, string productionName, Stopwatch stopwatch, CancellationToken cancellationToken)
    {
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
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value) : new Random();
#pragma warning restore S2245

        // Plan document layout (no I/O)
        var plans = ProductionSetPlanner.Plan(request);
        int volumeCount = (int)Math.Ceiling((double)request.Output.FileCount / request.Production.VolumeSize);

        // Pre-create volume subdirectories
        for (int v = 1; v <= volumeCount; v++)
        {
            var volName = $"VOL{v:D3}";
            Directory.CreateDirectory(Path.Combine(nativesDir, volName));
            Directory.CreateDirectory(Path.Combine(textDir, volName));
            Directory.CreateDirectory(Path.Combine(imagesDir, volName));
        }

        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);

        var fileGenerator = FileGeneratorFactory.Create(request.Output.FileType, request)
            ?? throw new InvalidOperationException($"Unknown file type: {request.Output.FileType}");

        // Generate files using the plan
        var fileDataList = new List<FileData>();

        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var workItem = new FileWorkItem
            {
                Index = plan.Index + 1,
                FolderNumber = plan.VolumeIndex,
                FolderName = plan.VolumeName,
                FileName = $"{plan.BatesNumber}.{request.Output.FileTypeLower}",
                FilePathInZip = plan.NativeRelPath,
            };
            var generated = fileGenerator.Generate(workItem, request);
            var nativeContent = generated.Content;

            await File.WriteAllBytesAsync(Path.Combine(productionPath, plan.NativeRelPath), nativeContent).ConfigureAwait(false);

            var textContent = $"Extracted text for document {plan.BatesNumber}. " +
                              LoremIpsum.GetParagraph(random);
            await File.WriteAllTextAsync(Path.Combine(productionPath, plan.TextRelPath), textContent, encoding).ConfigureAwait(false);

            // Write placeholder TIFF image (single-pixel stub)
            if (generated.PageCount > 1)
            {
                var imageExt = Path.GetExtension(plan.ImageRelPath) ?? string.Empty;
                var imagePathWithoutExt = plan.ImageRelPath[..^imageExt.Length];

                for (int pageIdx = 1; pageIdx <= generated.PageCount; pageIdx++)
                {
                    var pageImageRelPath = $"{imagePathWithoutExt}_{pageIdx:D3}{imageExt}";
                    await File.WriteAllBytesAsync(Path.Combine(productionPath, pageImageRelPath), PlaceholderFiles.GetContent("tiff")).ConfigureAwait(false);
                }
            }
            else
            {
                await File.WriteAllBytesAsync(Path.Combine(productionPath, plan.ImageRelPath), PlaceholderFiles.GetContent("tiff")).ConfigureAwait(false);
            }

            fileDataList.Add(new FileData
            {
                WorkItem = workItem,
                DataLength = nativeContent.Length,
                Attachment = generated.Attachment,
                PageCount = generated.PageCount,
                Email = generated.Email,
            });

            if (request.Metadata.WithFamilies && request.Output.IsEml && generated.Attachment.HasValue)
            {
                var attach = generated.Attachment.Value;
                var childBates = $"{plan.BatesNumber}_A001";
                var childExt = Path.GetExtension(attach.filename) ?? string.Empty;

                var childNativeRelPath = Path.Combine("NATIVES", plan.VolumeName, $"{childBates}{childExt}");
                var childTextRelPath = Path.Combine("TEXT", plan.VolumeName, $"{childBates}.txt");
                var childImageRelPath = Path.Combine("IMAGES", plan.VolumeName, $"{childBates}.tif");

                await File.WriteAllBytesAsync(Path.Combine(productionPath, childNativeRelPath), attach.content).ConfigureAwait(false);

                var childTextContent = $"Extracted text for attachment {childBates}.";
                await File.WriteAllTextAsync(Path.Combine(productionPath, childTextRelPath), childTextContent, encoding).ConfigureAwait(false);

                await File.WriteAllBytesAsync(Path.Combine(productionPath, childImageRelPath), PlaceholderFiles.GetContent("tiff")).ConfigureAwait(false);
            }

            // Progress reporting
            if ((plan.Index + 1) % 1000 == 0 || plan.Index == request.Output.FileCount - 1)
            {
                Console.Write($"\r  Progress: {plan.Index + 1:N0} / {request.Output.FileCount:N0} documents");
            }
        }

        Console.WriteLine();

        // Write DAT load file — build a fresh ChaosEngine per format (engines are stateful)
        var datPath = Path.Combine(dataDir, "loadfile.dat");
        var datWriter = LoadFiles.LoadFileWriterFactory.CreateWriter(LoadFileFormat.Dat, LoadFiles.WriterMode.ProductionSet);
        var datStream = new FileStream(datPath, FileMode.Create, FileAccess.Write, FileShare.None, PerformanceConstants.DefaultBufferSize, true);
        await using (datStream.ConfigureAwait(false))
        {
            await datWriter.WriteAsync(datStream, request, fileDataList, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var datAuditJson = LoadFileAuditWriter.GenerateAuditJson(datPath, request, fileDataList, null, LoadFileFormat.Dat);
        await File.WriteAllTextAsync(Path.Combine(dataDir, "loadfile_properties.json"), datAuditJson).ConfigureAwait(false);

        // Write OPT load file
        var optPath = Path.Combine(dataDir, "loadfile.opt");
        var optWriter = LoadFiles.LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt, LoadFiles.WriterMode.ProductionSet);
        var optStream = new FileStream(optPath, FileMode.Create, FileAccess.Write, FileShare.None, PerformanceConstants.DefaultBufferSize, true);
        await using (optStream.ConfigureAwait(false))
        {
            await optWriter.WriteAsync(optStream, request, fileDataList, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        var optAuditJson = LoadFileAuditWriter.GenerateAuditJson(optPath, request, fileDataList, null, LoadFileFormat.Opt);

        // Save OPT properties with a slightly different name to avoid overwriting DAT properties
        await File.WriteAllTextAsync(Path.Combine(dataDir, "loadfile.opt_properties.json"), optAuditJson).ConfigureAwait(false);

        // Write manifest
        var batesStart = plans[0].BatesNumber;
        var batesEnd = plans[^1].BatesNumber;
        var manifestPath = await ProductionManifestWriter.WriteAsync(
            productionPath, request, batesStart, batesEnd, volumeCount, stopwatch.Elapsed, fileDataList).ConfigureAwait(false);

        // Optionally wrap in ZIP
        string? zipPath = null;
        if (request.Production.ProductionZip)
        {
            zipPath = Path.Combine(request.Output.OutputPath, $"{productionName}.zip");
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
            TotalDocuments = request.Output.FileCount,
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
