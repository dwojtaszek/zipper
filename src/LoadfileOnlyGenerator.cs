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

        var formatsToGenerate = request.LoadFile.LoadFileFormats?.Any() == true
            ? request.LoadFile.LoadFileFormats
            : new List<LoadFileFormat> { request.LoadFile.LoadFileFormat };

        string primaryLoadFilePath = string.Empty;
        string primaryPropertiesPath = string.Empty;

        foreach (var format in formatsToGenerate)
        {
            var extension = format == LoadFileFormat.Opt ? ".opt" : ".dat";
            var loadFilePath = Path.Combine(request.Output.OutputPath, $"{baseFileName}{extension}");

            long totalLines = format == LoadFileFormat.Opt
                ? request.Output.FileCount
                : request.Output.FileCount + 1;

            // Resolve a named chaos scenario for display and audit, then let ChaosEngineBuilder
            // own engine construction (including the format-keyed delimiter defaults). This
            // removes the previous per-caller delimiter leak.
            if (request.Chaos.ChaosMode && !string.IsNullOrEmpty(request.Chaos.ChaosScenario))
            {
                var scenario = ChaosScenarios.GetByName(request.Chaos.ChaosScenario);
                if (scenario != null)
                {
                    if (string.IsNullOrEmpty(request.Chaos.ChaosAmount))
                    {
                        request.Chaos = request.Chaos with { ChaosAmount = scenario.DefaultAmount };
                    }

                    request.Chaos = request.Chaos with { ChaosTypes = string.IsNullOrEmpty(scenario.ChaosTypes) ? null : scenario.ChaosTypes };

                    Console.WriteLine(string.Format("  Chaos Scenario: {0} ({1})", scenario.Name, scenario.Description));
                }
                else
                {
                    Console.Error.WriteLine($"  Warning: Chaos scenario '{request.Chaos.ChaosScenario}' not found; falling back to the supplied --chaos-types/--chaos-amount (if any).");
                }
            }

            ChaosEngine? chaosEngine = ChaosEngineBuilder.Build(request, totalLines, format);

            // DatComposingWriter handles the column-profile path internally, so loadfile-only
            // DAT generation no longer needs a separate profile writer branch here.
            ILoadFileWriter writer = LoadFileWriterFactory.CreateWriter(
                format == LoadFileFormat.Opt ? LoadFileFormat.Opt : LoadFileFormat.Dat,
                WriterMode.LoadfileOnly);

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
                    chaosEngine?.Anomalies,
                    format);
            }
            catch
            {
                if (File.Exists(loadFilePath))
                {
                    File.Delete(loadFilePath);
                }

                throw;
            }

            if (format == request.LoadFile.LoadFileFormat || string.IsNullOrEmpty(primaryLoadFilePath))
            {
                primaryLoadFilePath = loadFilePath;
                primaryPropertiesPath = propertiesPath;
            }
        }

        stopwatch.Stop();

        return new LoadfileOnlyResult
        {
            LoadFilePath = primaryLoadFilePath,
            PropertiesFilePath = primaryPropertiesPath,
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
