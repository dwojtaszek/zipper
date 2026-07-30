using System.Diagnostics;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Generates standalone Load Files (DAT or OPT) without creating Native Files or Archives.
/// Used when --loadfile-only flag is specified.
/// </summary>
internal static class LoadFileOnlyGenerator
{
    /// <summary>
    /// Generates a standalone Load File and its companion properties JSON.
    /// </summary>
    /// <param name="request">File generation request with loadfile-only settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing generated file paths and performance metrics.</returns>
    public static async Task<LoadFileOnlyResult> GenerateAsync(FileGenerationRequest request, CancellationToken cancellationToken = default)
    {
        request = request.Clone();

        var stopwatch = Stopwatch.StartNew();

        Directory.CreateDirectory(request.Output.OutputPath);

        var baseFileName = $"loadfile_{DateTime.UtcNow:yyyyMMdd_HHmmss}";

        var formatsToGenerate = (request.LoadFile.Formats is not null && request.LoadFile.Formats.Count > 0)
            ? request.LoadFile.Formats
            : new List<LoadFileFormat> { LoadFileFormat.Dat };

        string primaryLoadFilePath = string.Empty;
        string primaryPropertiesPath = string.Empty;
        long totalRecords = 0;

        var generatedFiles = new List<string>();

        try
        {
            foreach (var format in formatsToGenerate)
            {
                var formatRequest = EnsureStableOptPageCounts(request, format);
                var extension = format == LoadFileFormat.Opt ? ".opt" : ".dat";
                var loadFilePath = Path.Combine(request.Output.OutputPath, $"{baseFileName}{extension}");
                generatedFiles.Add(loadFilePath);

                // Source-Driven Generation: rows become FileData shells (no Native File bytes)
                // and flow through the Standard composers so every Load File Format reflects
                // source paths, File Types, and record identity.
                var sourceDriven = formatRequest.SourceRecords is not null;
                IReadOnlyList<FileData> records = sourceDriven
                    ? BuildSourceShells(formatRequest)
                    : Array.Empty<FileData>();
                var writerMode = sourceDriven ? WriterMode.Standard : WriterMode.LoadfileOnly;

                ChaosEngine? chaosEngine = LoadFileAuditWriter.BuildChaosEngine(formatRequest, records, format);

                ILoadFileWriter writer = LoadFileWriterFactory.CreateWriter(
                    format == LoadFileFormat.Opt ? LoadFileFormat.Opt : LoadFileFormat.Dat,
                    writerMode);

                var fileStream = new FileStream(loadFilePath, FileMode.Create, FileAccess.Write, FileShare.None, PerformanceConstants.DefaultBufferSize, true);
                await using (fileStream.ConfigureAwait(false))
                {
                    await writer.WriteAsync(fileStream, formatRequest, records, chaosEngine, cancellationToken).ConfigureAwait(false);
                }

                string propertiesPath = await LoadFileAuditWriter.WriteAsync(
                    loadFilePath,
                    formatRequest,
                    records,
                    chaosEngine?.Anomalies,
                    format).ConfigureAwait(false);
                generatedFiles.Add(propertiesPath);

                if (format == formatsToGenerate[0] || string.IsNullOrEmpty(primaryLoadFilePath))
                {
                    primaryLoadFilePath = loadFilePath;
                    primaryPropertiesPath = propertiesPath;
                    var (total, _) = LoadFileAuditWriter.ComputeRecordCounts(formatRequest, records, format);
                    totalRecords = total;
                }
            }

            stopwatch.Stop();

            return new LoadFileOnlyResult
            {
                LoadFilePath = primaryLoadFilePath,
                PropertiesFilePath = primaryPropertiesPath,
                TotalRecords = totalRecords,
                GenerationTime = stopwatch.Elapsed,
            };
        }
        catch
        {
            foreach (var file in generatedFiles)
            {
                if (File.Exists(file))
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // Best effort cleanup
                    }
                }
            }

            throw;
        }
    }

    /// <summary>
    /// Builds in-memory <see cref="FileData"/> shells from Source Records for Loadfile-Only
    /// mode: real paths, File Types, and record identity; synthetic sizes, page counts, and
    /// (when enabled) simulated hashes. No Native File bytes are produced.
    /// </summary>
    private static IReadOnlyList<FileData> BuildSourceShells(FileGenerationRequest request)
    {
        var sourceRecords = request.SourceRecords!;
        var random = request.Metadata.Seed.HasValue
#pragma warning disable S2245
            ? new Random(request.Metadata.Seed.Value + 1)
            : new Random();
#pragma warning restore S2245

        var shells = new List<FileData>(sourceRecords.Count);
        long index = 0;
        foreach (var row in sourceRecords)
        {
            index++;
            var workItem = row.ToWorkItem(index);
            var pageCount = request.Tiff.PageRange.HasValue
                ? TiffMultiPageGenerator.GetPageCount(request.Tiff.PageRange, request.Metadata.Seed, index)
                : random.Next(1, 11);

            shells.Add(new FileData
            {
                WorkItem = workItem,
                DataLength = random.Next(1024, 10_485_760),
                PageCount = pageCount,
                Hashes = request.Hash.Mode == Config.HashMode.Simulated ? GenerateSimulatedHashes(request, workItem) : null,
            });
        }

        return shells;
    }

    private static IReadOnlyDictionary<Config.HashAlgorithm, string>? GenerateSimulatedHashes(FileGenerationRequest request, FileWorkItem workItem)
    {
        var hashConfig = request.Hash;
        if (!hashConfig.IsEnabled)
        {
            return null;
        }

        var dict = new Dictionary<Config.HashAlgorithm, string>(hashConfig.Algorithms.Count);
        var rng = Config.HashUtility.CreateSeededRandom(request, workItem.Index);
        foreach (var algo in hashConfig.Algorithms)
        {
            dict[algo] = Config.HashUtility.GenerateSimulatedHash(algo, rng);
        }

        return dict;
    }

    private static FileGenerationRequest EnsureStableOptPageCounts(FileGenerationRequest request, LoadFileFormat format)
    {
        if (format != LoadFileFormat.Opt || request.Tiff.PageRange.HasValue || request.Metadata.Seed.HasValue)
        {
            return request;
        }

        var stableRequest = request.Clone();
        stableRequest.Metadata = request.Metadata with { Seed = Random.Shared.Next() };
        return stableRequest;
    }
}

/// <summary>
/// Result of a loadfile-only generation operation.
/// </summary>
internal class LoadFileOnlyResult
{
    /// <summary>
    /// Gets or sets the path to the generated load file.
    /// </summary>
    public string LoadFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the path to the properties JSON file.
    /// </summary>
    public string PropertiesFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total records written.
    /// </summary>
    public long TotalRecords { get; set; }

    /// <summary>
    /// Gets or sets the generation time.
    /// </summary>
    public TimeSpan GenerationTime { get; set; }
}
