using System.Text;
using Zipper.Profiles.Data;

namespace Zipper;

/// <summary>
/// Orchestrates production set generation by delegating all disk I/O to an <see cref="IFileMaterializer"/>
/// and all hash computation to a <see cref="IHashComputer"/>. This makes the production set logic
/// testable without the filesystem and concentrates I/O bugs in a single seam.
/// </summary>
internal static class ProductionSetOrchestrator
{
    private static readonly System.Text.Json.JsonSerializerOptions ValidationReportSerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] RedactionReasons =
    [
        "Privileged",
        "Attorney Work Product",
        "Settlement Communication",
        "Trade Secret",
        "Personal Privacy",
    ];

    /// <summary>
    /// Generates a complete production set with injected seams for testing.
    /// </summary>
    public static async Task<ProductionSetResult> GenerateAsync(
        FileGenerationRequest request,
        IFileMaterializer materializer,
        IHashComputer hashComputer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(materializer);
        ArgumentNullException.ThrowIfNull(hashComputer);

        if (request.Chaos.ChaosMode && !request.LoadfileOnly)
        {
            throw new InvalidOperationException("Chaos mode requires loadfile-only mode at the generation layer.");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        int rollingCount = request.Production.RollingCount;
        if (rollingCount < 1)
        {
            rollingCount = 1;
        }

        var prodIds = Cli.Modules.ProductionModule.GenerateProductionIds(request.Production.ProductionId, rollingCount);
        ProductionSetResult? lastResult = null;
        long currentBatesStart = request.Bates?.Start ?? 1;

        // Upfront check: reject existing output before generation
        for (int i = 0; i < rollingCount; i++)
        {
            var prodName = prodIds[i];
            var prodPath = Path.Combine(request.Output.OutputPath, prodName);

            if (await materializer.DirectoryExistsAsync(prodPath, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"Production directory already exists: '{prodPath}'");
            }

            if (request.Production.ProductionZip)
            {
                var prodZip = Path.Combine(request.Output.OutputPath, $"{prodName}.zip");
                if (await materializer.FileExistsAsync(prodZip, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException($"Production zip already exists: '{prodZip}'");
                }
            }
        }

        var createdDirectories = new List<string>();
        var createdZips = new List<string>();

        for (int i = 0; i < rollingCount; i++)
        {
            var productionName = prodIds[i];
            var productionPath = Path.Combine(request.Output.OutputPath, productionName);

            if (await materializer.DirectoryExistsAsync(productionPath, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException($"Production directory already exists: '{productionPath}'");
            }

            if (request.Production.ProductionZip)
            {
                var zipPath = Path.Combine(request.Output.OutputPath, $"{productionName}.zip");
                if (await materializer.FileExistsAsync(zipPath, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException($"Production zip already exists: '{zipPath}'");
                }
            }

            try
            {
                long startToUse;
                if (request.Production.RollingBatesMode == Config.RollingBatesMode.Restart)
                {
                    startToUse = request.Bates?.Starts is not null && request.Bates.Starts.Count > i
                        ? request.Bates.Starts[i]
                        : request.Bates?.Start ?? 1;
                }
                else
                {
                    startToUse = request.Bates?.Starts is not null && request.Bates.Starts.Count > i
                        ? request.Bates.Starts[i]
                        : currentBatesStart;
                }

                var stepStopwatch = System.Diagnostics.Stopwatch.StartNew();
                int batesConsumed = 0;
                lastResult = await GenerateCoreAsync(
                    request,
                    materializer,
                    hashComputer,
                    productionPath,
                    productionName,
                    i,
                    startToUse,
                    count => batesConsumed = count,
                    stepStopwatch,
                    createdDirectories,
                    createdZips,
                    cancellationToken).ConfigureAwait(false);

                if (request.Production.RollingBatesMode == Config.RollingBatesMode.Continuous)
                {
                    currentBatesStart = startToUse + batesConsumed * (request.Bates?.Increment ?? 1);
                }
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, "Error: Rolling Production Set '{0}' ({1} of {2}) failed: {3}", productionName, i + 1, rollingCount, ex.Message)).ConfigureAwait(false);

                if (ex is not Validation.ValidationFailedException)
                {
                    foreach (var zipToDelete in createdZips)
                    {
                        try
                        {
                            await materializer.DeleteFileAsync(zipToDelete, cancellationToken).ConfigureAwait(false);
                        }
#pragma warning disable CA1031
#pragma warning disable RCS1075
                        catch (Exception)
                        {
                            // Suppress cleanup exceptions to preserve original error
                        }
#pragma warning restore RCS1075
#pragma warning restore CA1031
                    }

                    foreach (var dirToDelete in createdDirectories)
                    {
                        try
                        {
                            await materializer.DeleteDirectoryAsync(dirToDelete, cancellationToken).ConfigureAwait(false);
                        }
#pragma warning disable CA1031
#pragma warning disable RCS1075
                        catch (Exception)
                        {
                            // Suppress cleanup exceptions to preserve original error
                        }
#pragma warning restore RCS1075
#pragma warning restore CA1031
                    }
                }

                throw;
            }
        }

        stopwatch.Stop();
        if (lastResult is not null)
        {
            lastResult.GenerationTime = stopwatch.Elapsed;
            return lastResult;
        }

        throw new InvalidOperationException("No production sets were generated.");
    }

    private static async Task<ProductionSetResult> GenerateCoreAsync(
        FileGenerationRequest request,
        IFileMaterializer materializer,
        IHashComputer hashComputer,
        string productionPath,
        string productionName,
        int rollingIndex,
        long batesStartOverride,
        Action<int> onPlansGenerated,
        System.Diagnostics.Stopwatch stopwatch,
        List<string> createdDirectories,
        List<string> createdZips,
        CancellationToken cancellationToken)
    {
        // Plan document layout (no I/O)
        var plans = ProductionSetPlanner.Plan(request, rollingIndex, batesStartOverride);
        onPlansGenerated(plans.Count);

        // Run supplemental validation before any output is created
        Validation.SupplementalValidationReport? supplementalReport = null;
        if (request.Production.SupplementalProduction)
        {
            supplementalReport = await Validation.SupplementalValidator.ValidateAsync(
                request, plans[0].BatesNumber, plans[^1].BatesNumber).ConfigureAwait(false);
        }

        var dataDir = Path.Combine(productionPath, "DATA");
        int volumeCount = (int)Math.Ceiling((double)request.Output.FileCount / request.Production.VolumeSize);

        createdDirectories.Add(productionPath);

        // Create directory structure
        await SetupDirectoriesAsync(productionPath, request, volumeCount, plans, materializer, cancellationToken).ConfigureAwait(false);


        // Generate files using the plan
        var fileDataList = await GenerateVolumeFilesAsync(
            productionPath, request, plans, materializer, hashComputer, cancellationToken).ConfigureAwait(false);

        // Write DAT + OPT load files and their audits through the shared orchestrator
        var (datPath, optPath) = await EmitLoadFilesAsync(dataDir, request, fileDataList, materializer, cancellationToken).ConfigureAwait(false);

        // Write manifest
        var manifestPath = await WriteManifestAsync(
            productionPath,
            request,
            plans,
            volumeCount,
            stopwatch.Elapsed,
            fileDataList,
            supplementalReport,
            productionName,
            rollingIndex,
            materializer).ConfigureAwait(false);

        // Run validation when running against real file system
        await ValidateProductionSetAsync(productionPath, request, materializer, cancellationToken).ConfigureAwait(false);

        // Optionally wrap in ZIP
        var zipPath = await CreateZipArchiveAsync(productionPath, productionName, request, materializer, createdZips, cancellationToken).ConfigureAwait(false);

        stopwatch.Stop();

        return new ProductionSetResult
        {
            ProductionPath = productionPath,
            ZipFilePath = zipPath,
            DatFilePath = datPath,
            OptFilePath = optPath,
            ManifestPath = manifestPath,
            TotalDocuments = request.Output.FileCount,
            BatesRange = $"{plans[0].BatesNumber} - {plans[^1].BatesNumber}",
            VolumeCount = volumeCount,
            GenerationTime = stopwatch.Elapsed,
        };
    }

    private static async Task SetupDirectoriesAsync(
        string productionPath,
        FileGenerationRequest request,
        int volumeCount,
        IReadOnlyList<ProductionNativeFilePlan> plans,
        IFileMaterializer materializer,
        CancellationToken cancellationToken)
    {
        var dataDir = Path.Combine(productionPath, "DATA");
        var nativesDir = Path.Combine(productionPath, "NATIVES");
        var textDir = Path.Combine(productionPath, "TEXT");
        var imagesDir = Path.Combine(productionPath, "IMAGES");

        await materializer.CreateDirectoryAsync(dataDir, cancellationToken).ConfigureAwait(false);
        await materializer.CreateDirectoryAsync(nativesDir, cancellationToken).ConfigureAwait(false);
        await materializer.CreateDirectoryAsync(textDir, cancellationToken).ConfigureAwait(false);
        await materializer.CreateDirectoryAsync(imagesDir, cancellationToken).ConfigureAwait(false);

        bool isRedacted = request.Production.RedactedProduction;
        string? redactedImagesDir = null;
        string? redactedTextDir = null;
        if (isRedacted)
        {
            redactedImagesDir = Path.Combine(productionPath, "REDACTED", "IMAGES");
            redactedTextDir = Path.Combine(productionPath, "REDACTED", "TEXT");
            await materializer.CreateDirectoryAsync(redactedImagesDir, cancellationToken).ConfigureAwait(false);
            await materializer.CreateDirectoryAsync(redactedTextDir, cancellationToken).ConfigureAwait(false);
        }

        // Pre-create volume subdirectories
        for (int v = 1; v <= volumeCount; v++)
        {
            var volName = $"VOL{v:D3}";
            await materializer.CreateDirectoryAsync(Path.Combine(nativesDir, volName), cancellationToken).ConfigureAwait(false);
            await materializer.CreateDirectoryAsync(Path.Combine(textDir, volName), cancellationToken).ConfigureAwait(false);
            await materializer.CreateDirectoryAsync(Path.Combine(imagesDir, volName), cancellationToken).ConfigureAwait(false);
            if (isRedacted && redactedImagesDir is not null && redactedTextDir is not null)
            {
                await materializer.CreateDirectoryAsync(Path.Combine(redactedImagesDir, volName), cancellationToken).ConfigureAwait(false);
                await materializer.CreateDirectoryAsync(Path.Combine(redactedTextDir, volName), cancellationToken).ConfigureAwait(false);
            }
        }

        // Source-Driven Generation with preserve/originals path modes nests Native Files in
        // source-derived directories not covered by the pre-created Volume folders.
        if (request.Production.SourcePathMode != Config.SourcePathMode.Bates && request.SourceRecords is not null)
        {
            foreach (var dir in plans.Select(p => Path.GetDirectoryName(p.NativeRelPath)).Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.Ordinal))
            {
                await materializer.CreateDirectoryAsync(Path.Combine(productionPath, dir!), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<List<FileData>> GenerateVolumeFilesAsync(
        string productionPath,
        FileGenerationRequest request,
        IReadOnlyList<ProductionNativeFilePlan> plans,
        IFileMaterializer materializer,
        IHashComputer hashComputer,
        CancellationToken cancellationToken)
    {
        var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
        var fileGenerators = FileGeneratorFactory.CreateMap(request);

#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value) : new Random();
#pragma warning restore S2245

        var fileDataList = new List<FileData>(plans.Count);

        for (int i = 0; i < plans.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var plan = plans[i];
            var fileData = await GenerateDocumentFilesAsync(
                productionPath,
                request,
                plan,
                fileGenerators,
                encoding,
                random,
                materializer,
                hashComputer,
                cancellationToken).ConfigureAwait(false);

            materializer.AddFileData(fileData);
            fileDataList.Add(fileData);

            // Progress reporting
            if ((plan.Index + 1) % 1000 == 0 || plan.Index == request.Output.FileCount - 1)
            {
                Console.Write($"\r  Progress: {plan.Index + 1:N0} / {request.Output.FileCount:N0} documents");
            }
        }

        Console.WriteLine();
        return fileDataList;
    }

    private static async Task<FileData> GenerateDocumentFilesAsync(
        string productionPath,
        FileGenerationRequest request,
        ProductionNativeFilePlan plan,
        IReadOnlyDictionary<string, IFileGenerator> fileGenerators,
        Encoding encoding,
        Random random,
        IFileMaterializer materializer,
        IHashComputer hashComputer,
        CancellationToken cancellationToken)
    {
        var workItem = new FileWorkItem
        {
            Index = plan.Index + 1,
            FolderNumber = plan.VolumeIndex,
            FolderName = plan.VolumeName,
            FileName = Path.GetFileName(plan.NativeRelPath),
            FilePathInZip = plan.NativeRelPath,
            FileType = plan.FileType,
        };
        var generated = fileGenerators[plan.FileType].Generate(workItem, request);
        var nativeContent = generated.Content;

        await materializer.WriteBytesAsync(Path.Combine(productionPath, plan.NativeRelPath), nativeContent, cancellationToken).ConfigureAwait(false);

        var textContent = $"Extracted text for document {plan.BatesNumber}. " +
                          LoremIpsum.GetParagraph(random);
        await materializer.WriteTextAsync(Path.Combine(productionPath, plan.TextRelPath), textContent, encoding, cancellationToken).ConfigureAwait(false);

        // Write placeholder TIFF image (single-pixel stub)
        await WritePlaceholderImagesAsync(productionPath, plan.ImageRelPath, generated.PageCount, materializer, cancellationToken).ConfigureAwait(false);

        // Write redacted image/text files when redacted mode is on
        var (nativePathOverride, redactionReason) = await WriteRedactedDocumentFilesAsync(
            productionPath,
            request,
            plan,
            encoding,
            random,
            materializer,
            cancellationToken).ConfigureAwait(false);

        var (fileDataHash, fileDataHashes) = ComputeFileDataHashes(nativeContent, workItem, request, hashComputer);

        var fileData = new FileData
        {
            WorkItem = workItem,
            DataLength = nativeContent.Length,
            Attachment = generated.Attachment,
            PageCount = generated.PageCount,
            Email = generated.Email,
            Hash = fileDataHash,
            Hashes = fileDataHashes,
            RedactedImageRelPath = plan.RedactedImageRelPath,
            RedactedTextRelPath = plan.RedactedTextRelPath,

            // Only source-driven runs override composer path derivation; legacy runs keep
            // the historical NATIVES-rooted derivation byte-for-byte (Rule 6 quirk preservation).
            TextRelPath = request.SourceRecords is not null ? plan.TextRelPath : null,
            ImageRelPath = request.SourceRecords is not null ? plan.ImageRelPath : null,
            NativePathOverride = nativePathOverride,
            RedactionReason = redactionReason,
        };

        if (request.Metadata.WithFamilies && string.Equals(plan.FileType, "eml", StringComparison.Ordinal) && generated.Attachment.HasValue)
        {
            await WriteChildAttachmentFilesAsync(
                productionPath,
                request,
                plan,
                generated.Attachment.Value,
                encoding,
                materializer,
                cancellationToken).ConfigureAwait(false);
        }

        return fileData;
    }

    private static async Task WritePlaceholderImagesAsync(
        string productionPath,
        string imageRelPath,
        int pageCount,
        IFileMaterializer materializer,
        CancellationToken cancellationToken)
    {
        if (pageCount > 1)
        {
            var imageExt = Path.GetExtension(imageRelPath) ?? string.Empty;
            var imagePathWithoutExt = imageRelPath[..^imageExt.Length];

            for (int pageIdx = 1; pageIdx <= pageCount; pageIdx++)
            {
                var pageImageRelPath = $"{imagePathWithoutExt}_{pageIdx:D3}{imageExt}";
                await materializer.WriteBytesAsync(Path.Combine(productionPath, pageImageRelPath), PlaceholderFiles.GetContent("tiff"), cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await materializer.WriteBytesAsync(Path.Combine(productionPath, imageRelPath), PlaceholderFiles.GetContent("tiff"), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<(string? NativePathOverride, string? RedactionReason)> WriteRedactedDocumentFilesAsync(
        string productionPath,
        FileGenerationRequest request,
        ProductionNativeFilePlan plan,
        Encoding encoding,
        Random random,
        IFileMaterializer materializer,
        CancellationToken cancellationToken)
    {
        if (!request.Production.RedactedProduction)
        {
            return (null, null);
        }

        var redactionReason = RedactionReasons[plan.Index % RedactionReasons.Length];

        if (plan.RedactedImageRelPath is not null)
        {
            await materializer.WriteBytesAsync(Path.Combine(productionPath, plan.RedactedImageRelPath), PlaceholderFiles.GetContent("tiff"), cancellationToken).ConfigureAwait(false);
        }

        if (plan.RedactedTextRelPath is not null && request.Output.WithText)
        {
            var redactedText = $"Redacted text for document {plan.BatesNumber}. {LoremIpsum.GetParagraph(random)}";
            await materializer.WriteTextAsync(Path.Combine(productionPath, plan.RedactedTextRelPath), redactedText, encoding, cancellationToken).ConfigureAwait(false);
        }

        // Apply withheld native policy
        var nativePathOverride = request.Production.WithheldNativePolicy switch
        {
            "omit-native-path" => string.Empty,
            "replace-with-placeholder" => $"PLACEHOLDER/{plan.VolumeName}/{plan.BatesNumber}.{plan.FileType}",
            _ => null,
        };

        return (nativePathOverride, redactionReason);
    }

    private static (string Hash, IReadOnlyDictionary<Config.HashAlgorithm, string>? Hashes) ComputeFileDataHashes(
        byte[] nativeContent,
        FileWorkItem workItem,
        FileGenerationRequest request,
        IHashComputer hashComputer)
    {
        var hashConfig = request.Hash;
        if (!hashConfig.IsEnabled)
        {
            return (string.Empty, null);
        }

        var hashes = hashComputer.ComputeHashes(nativeContent, hashConfig, workItem, request);
        if (hashes is null)
        {
            return (string.Empty, null);
        }

        var fileDataHash = hashes.TryGetValue(Config.HashAlgorithm.MD5, out var md5) ? md5 : string.Empty;
        return (fileDataHash, hashes);
    }

    private static async Task WriteChildAttachmentFilesAsync(
        string productionPath,
        FileGenerationRequest request,
        ProductionNativeFilePlan plan,
        (string filename, byte[] content) attach,
        Encoding encoding,
        IFileMaterializer materializer,
        CancellationToken cancellationToken)
    {
        var childBates = $"{plan.BatesNumber}_A001";
        var childExt = Path.GetExtension(attach.filename) ?? string.Empty;

        var childNativeRelPath = Path.Combine("NATIVES", plan.VolumeName, $"{childBates}{childExt}");
        var childTextRelPath = Path.Combine("TEXT", plan.VolumeName, $"{childBates}.txt");
        var childImageRelPath = Path.Combine("IMAGES", plan.VolumeName, $"{childBates}.tif");

        await materializer.WriteChildAttachmentAsync(Path.Combine(productionPath, childNativeRelPath), attach, cancellationToken).ConfigureAwait(false);

        var childTextContent = $"Extracted text for attachment {childBates}.";
        await materializer.WriteTextAsync(Path.Combine(productionPath, childTextRelPath), childTextContent, encoding, cancellationToken).ConfigureAwait(false);

        await materializer.WriteBytesAsync(Path.Combine(productionPath, childImageRelPath), PlaceholderFiles.GetContent("tiff"), cancellationToken).ConfigureAwait(false);

        // Write redacted child files when redacted mode is on
        if (request.Production.RedactedProduction)
        {
            var childRedactedImageRelPath = Path.Combine("REDACTED", "IMAGES", plan.VolumeName, $"{childBates}.tif");
            await materializer.WriteBytesAsync(Path.Combine(productionPath, childRedactedImageRelPath), PlaceholderFiles.GetContent("tiff"), cancellationToken).ConfigureAwait(false);

            if (request.Output.WithText)
            {
                var childRedactedTextRelPath = Path.Combine("REDACTED", "TEXT", plan.VolumeName, $"{childBates}.txt");
                var childRedactedText = $"Redacted text for attachment {childBates}.";
                await materializer.WriteTextAsync(Path.Combine(productionPath, childRedactedTextRelPath), childRedactedText, encoding, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<(string DatPath, string OptPath)> EmitLoadFilesAsync(
        string dataDir,
        FileGenerationRequest request,
        IReadOnlyList<FileData> fileDataList,
        IFileMaterializer materializer,
        CancellationToken cancellationToken)
    {
        await LoadFiles.LoadFileOrchestrator.EmitAllAsync(
            request,
            fileDataList,
            new List<LoadFileFormat> { LoadFileFormat.Dat, LoadFileFormat.Opt },
            LoadFiles.WriterMode.ProductionSet,
            (format, writer) =>
            {
                var loadTarget = Path.Combine(dataDir, "loadfile" + writer.FileExtension);
                var auditTarget = format == LoadFileFormat.Dat
                    ? Path.Combine(dataDir, "loadfile_properties.json")
                    : loadTarget + "_properties.json";
                return (loadTarget, auditTarget);
            },
            target => materializer.OpenWriteStream(target),
            cancellationToken).ConfigureAwait(false);

        var datPath = Path.Combine(dataDir, "loadfile.dat");
        var optPath = Path.Combine(dataDir, "loadfile.opt");
        return (datPath, optPath);
    }

    private static async Task<string> WriteManifestAsync(
        string productionPath,
        FileGenerationRequest request,
        IReadOnlyList<ProductionNativeFilePlan> plans,
        int volumeCount,
        TimeSpan generationTime,
        IReadOnlyList<FileData> fileDataList,
        Validation.SupplementalValidationReport? supplementalReport,
        string productionName,
        int rollingIndex,
        IFileMaterializer materializer)
    {
        var batesStart = plans[0].BatesNumber;
        var batesEnd = plans[^1].BatesNumber;
        var batesConfig = request.Bates;
        string prefix = batesConfig?.Prefixes is not null && batesConfig.Prefixes.Count > rollingIndex
            ? batesConfig.Prefixes[rollingIndex]
            : batesConfig?.Prefix ?? string.Empty;

        return await ProductionManifestWriter.WriteAsync(
            productionPath,
            request,
            batesStart,
            batesEnd,
            volumeCount,
            generationTime,
            fileDataList,
            request.Production.PriorManifests,
            supplementalReport,
            productionId: productionName,
            rollingSequenceNumber: rollingIndex + 1,
            batesRangeMode: request.Production.RollingBatesMode.ToString().ToLowerInvariant(),
            batesPrefix: prefix,
            materializer: materializer).ConfigureAwait(false);
    }

    private static async Task ValidateProductionSetAsync(
        string productionPath,
        FileGenerationRequest request,
        IFileMaterializer materializer,
        CancellationToken cancellationToken)
    {
        if (materializer is ProductionFileMaterializer)
        {
            var report = Validation.ProductionSetPostValidator.Validate(productionPath, request);
            var reportPath = Path.Combine(productionPath, "_validation_report.json");
            var reportJson = System.Text.Json.JsonSerializer.Serialize(report, ValidationReportSerializerOptions);
            await materializer.WriteTextAsync(reportPath, reportJson, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);

            if (string.Equals(report.Status, "failed", StringComparison.OrdinalIgnoreCase))
            {
                throw new Validation.ValidationFailedException($"Production Set validation failed: {report.ErrorCount} error(s) found. See '_validation_report.json' for details.");
            }
        }
    }

    private static async Task<string?> CreateZipArchiveAsync(
        string productionPath,
        string productionName,
        FileGenerationRequest request,
        IFileMaterializer materializer,
        List<string> createdZips,
        CancellationToken cancellationToken)
    {
        if (!request.Production.ProductionZip)
        {
            return null;
        }

        var zipPath = Path.Combine(request.Output.OutputPath, $"{productionName}.zip");
        Console.Write("  Creating ZIP archive...");
        await materializer.CreateZipAsync(productionPath, zipPath, cancellationToken).ConfigureAwait(false);
        createdZips.Add(zipPath);
        Console.WriteLine(" done.");
        return zipPath;
    }
}

