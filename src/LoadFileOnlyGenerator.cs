using System.Diagnostics;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Generates standalone load files (DAT or OPT) without creating native files or ZIP archives.
/// Used when --loadfile-only flag is specified.
/// </summary>
internal static class LoadFileOnlyGenerator
{
    /// <summary>
    /// Generates a standalone load file and its companion properties JSON.
    /// </summary>
    /// <param name="request">File generation request with loadfile-only settings.</param>
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

        var generatedFiles = new List<string>();

        try
        {
            foreach (var format in formatsToGenerate)
            {
                var extension = format == LoadFileFormat.Opt ? ".opt" : ".dat";
                var loadFilePath = Path.Combine(request.Output.OutputPath, $"{baseFileName}{extension}");
                generatedFiles.Add(loadFilePath);

                var emptyRecords = Array.Empty<FileData>();



                ChaosEngine? chaosEngine = LoadFileAuditWriter.BuildChaosEngine(request, emptyRecords, format);

                ILoadFileWriter writer = LoadFileWriterFactory.CreateWriter(
                    format == LoadFileFormat.Opt ? LoadFileFormat.Opt : LoadFileFormat.Dat,
                    WriterMode.LoadfileOnly);

                var fileStream = new FileStream(loadFilePath, FileMode.Create, FileAccess.Write, FileShare.None, PerformanceConstants.DefaultBufferSize, true);
                await using (fileStream.ConfigureAwait(false))
                {
                    await writer.WriteAsync(fileStream, request, emptyRecords, chaosEngine, cancellationToken).ConfigureAwait(false);
                }

                string propertiesPath = await LoadFileAuditWriter.WriteAsync(
                    loadFilePath,
                    request,
                    emptyRecords,
                    chaosEngine?.Anomalies,
                    format).ConfigureAwait(false);
                generatedFiles.Add(propertiesPath);

                if (format == formatsToGenerate[0] || string.IsNullOrEmpty(primaryLoadFilePath))
                {
                    primaryLoadFilePath = loadFilePath;
                    primaryPropertiesPath = propertiesPath;
                }
            }

            stopwatch.Stop();

            return new LoadFileOnlyResult
            {
                LoadFilePath = primaryLoadFilePath,
                PropertiesFilePath = primaryPropertiesPath,
                TotalRecords = request.Output.FileCount,
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
