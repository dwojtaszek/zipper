using System.Diagnostics;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Generates standalone load files (DAT or OPT) without creating native files or ZIP archives.
/// Used when --loadfile-only flag is specified.
/// </summary>
internal static class LoadfileOnlyGenerator
{
    /// <summary>
    /// Generates a standalone load file and its companion properties JSON.
    /// </summary>
    /// <param name="request">File generation request with loadfile-only settings.</param>
    /// <returns>Result containing generated file paths and performance metrics.</returns>
    public static async Task<LoadfileOnlyResult> GenerateAsync(FileGenerationRequest request)
    {
        request = request.Clone();

        var stopwatch = Stopwatch.StartNew();

        Directory.CreateDirectory(request.Output.OutputPath);

        var baseFileName = $"loadfile_{DateTime.Now:yyyyMMdd_HHmmss}";
        var extension = request.LoadFile.LoadFileFormat == LoadFileFormat.Opt ? ".opt" : ".dat";
        var loadFilePath = Path.Combine(request.Output.OutputPath, $"{baseFileName}{extension}");

        var eolString = LoadFiles.LoadFileWriterBase.GetEolString(request.Delimiters.EndOfLine);
        long totalLines = request.LoadFile.LoadFileFormat == LoadFileFormat.Opt
            ? request.Output.FileCount
            : request.Output.FileCount + 1;

        ChaosEngine? chaosEngine = null;
        if (request.Chaos.ChaosMode)
        {
            string? resolvedTypes = request.Chaos.ChaosTypes;
            string? resolvedAmount = request.Chaos.ChaosAmount;

            if (!string.IsNullOrEmpty(request.Chaos.ChaosScenario))
            {
                var scenario = ChaosScenarios.GetByName(request.Chaos.ChaosScenario);
                if (scenario != null)
                {
                    resolvedTypes = string.IsNullOrEmpty(scenario.ChaosTypes) ? null : scenario.ChaosTypes;
                    if (string.IsNullOrEmpty(resolvedAmount))
                    {
                        resolvedAmount = scenario.DefaultAmount;
                    }

                    request.Chaos = request.Chaos with { ChaosAmount = resolvedAmount };
                    request.Chaos = request.Chaos with { ChaosTypes = resolvedTypes };

                    Console.WriteLine(string.Format("  Chaos Scenario: {0} ({1})", scenario.Name, scenario.Description));
                }
            }

            string chaosColDelim = request.LoadFile.LoadFileFormat == LoadFileFormat.Opt ? "," : request.Delimiters.ColumnDelimiter;
            string chaosQuoteDelim = request.LoadFile.LoadFileFormat == LoadFileFormat.Opt ? string.Empty : request.Delimiters.QuoteDelimiter;

            chaosEngine = new ChaosEngine(
                totalLines,
                resolvedAmount,
                resolvedTypes,
                request.LoadFile.LoadFileFormat,
                chaosColDelim,
                chaosQuoteDelim,
                eolString,
                request.Metadata.Seed);
        }

        ILoadFileWriter writer = request.LoadFile.LoadFileFormat == LoadFileFormat.Opt
            ? new LoadfileOnlyOptWriter()
            : new LoadfileOnlyDatWriter();

        await using (var fileStream = new FileStream(loadFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
        {
            await writer.WriteAsync(fileStream, request, new List<FileData>(), chaosEngine);
        }

        string propertiesPath;
        try
        {
            propertiesPath = await LoadfileAuditWriter.WriteAsync(
                loadFilePath,
                request,
                request.Output.FileCount,
                chaosEngine?.Anomalies);
        }
        catch
        {
            if (File.Exists(loadFilePath))
            {
                File.Delete(loadFilePath);
            }

            throw;
        }

        stopwatch.Stop();

        return new LoadfileOnlyResult
        {
            LoadFilePath = loadFilePath,
            PropertiesFilePath = propertiesPath,
            TotalRecords = request.Output.FileCount,
            GenerationTime = stopwatch.Elapsed,
        };
    }
}

/// <summary>
/// Result of a loadfile-only generation operation.
/// </summary>
internal class LoadfileOnlyResult
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
