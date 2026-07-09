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

                var emptyRecords = Array.Empty<FileData>();



                ChaosEngine? chaosEngine = LoadFileAuditWriter.BuildChaosEngine(formatRequest, emptyRecords, format);

                ILoadFileWriter writer = LoadFileWriterFactory.CreateWriter(
                    format == LoadFileFormat.Opt ? LoadFileFormat.Opt : LoadFileFormat.Dat,
                    WriterMode.LoadfileOnly);

                var fileStream = new FileStream(loadFilePath, FileMode.Create, FileAccess.Write, FileShare.None, PerformanceConstants.DefaultBufferSize, true);
                await using (fileStream.ConfigureAwait(false))
                {
                    await writer.WriteAsync(fileStream, formatRequest, emptyRecords, chaosEngine, cancellationToken).ConfigureAwait(false);
                }

                string propertiesPath = await LoadFileAuditWriter.WriteAsync(
                    loadFilePath,
                    formatRequest,
                    emptyRecords,
                    chaosEngine?.Anomalies,
                    format).ConfigureAwait(false);
                generatedFiles.Add(propertiesPath);

                if (format == formatsToGenerate[0] || string.IsNullOrEmpty(primaryLoadFilePath))
                {
                    primaryLoadFilePath = loadFilePath;
                    primaryPropertiesPath = propertiesPath;
                    var (total, _) = LoadFileAuditWriter.ComputeRecordCounts(formatRequest, emptyRecords, format);
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
