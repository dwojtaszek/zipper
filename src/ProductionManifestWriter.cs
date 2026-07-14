using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zipper;

/// <summary>
/// Writes a Production Manifest describing a generated Production Set.
/// </summary>
internal static class ProductionManifestWriter
{
    private static readonly JsonSerializerOptions ManifestSerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes the Production Manifest to the specified directory.
    /// </summary>
    /// <param name="productionPath">Root directory of the Production Set.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="batesStart">First Bates Number in the Production Set.</param>
    /// <param name="batesEnd">Last Bates Number in the Production Set.</param>
    /// <param name="volumeCount">Number of volume subfolders created.</param>
    /// <param name="generationTime">Total time for generation.</param>
    /// <returns>Path to the Production Manifest.</returns>
    public static async Task<string> WriteAsync(
        string productionPath,
        FileGenerationRequest request,
        string batesStart,
        string batesEnd,
        int volumeCount,
        TimeSpan generationTime,
        System.Collections.Generic.IReadOnlyList<FileData>? fileDataList = null,
        System.Collections.Generic.IReadOnlyList<string>? priorManifests = null,
        Validation.SupplementalValidationReport? supplementalValidation = null,
        string? productionId = null,
        int rollingSequenceNumber = 1,
        string? batesRangeMode = null,
        string? batesPrefix = null)
    {
        var manifestPath = Path.Combine(productionPath, "_manifest.json");

        fileDataList ??= Array.Empty<FileData>();
        long parentCount = fileDataList.Count > 0 ? fileDataList.Count : request.Output.FileCount;
        long attachmentCount = 0;
        if (request.Metadata.WithFamilies && request.Output.IsEml)
        {
            attachmentCount = fileDataList.Count(f => f.Attachment.HasValue);
        }
        long totalNativeCount = parentCount + attachmentCount;

        var manifest = new ProductionManifest
        {
            ProductionDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            ProductionId = productionId ?? Path.GetFileName(productionPath),
            RollingSequenceNumber = rollingSequenceNumber,
            BatesNumberStart = batesStart,
            BatesNumberEnd = batesEnd,
            BatesRangeMode = batesRangeMode ?? "continuous",
            BatesRange = new BatesRange
            {
                Start = batesStart,
                End = batesEnd,
                Prefix = batesPrefix ?? request.Bates?.Prefix ?? string.Empty,
                Digits = request.Bates?.Digits ?? 8,
            },
            NativeFileCount = totalNativeCount,
            ParentNativeFileCount = attachmentCount > 0 ? parentCount : null,
            AttachmentNativeFileCount = attachmentCount > 0 ? attachmentCount : null,
            FileType = request.Output.FileType,
            VolumeCount = volumeCount,
            VolumeSize = request.Production.VolumeSize,
            Directories = new ProductionDirectories
            {
                Data = "DATA",
                Natives = "NATIVES",
                Text = "TEXT",
                Images = "IMAGES",
                Redacted = request.Production.RedactedProduction ? "REDACTED" : null,
            },
            LoadFiles = new ProductionLoadFiles
            {
                Dat = "DATA/loadfile.dat",
                Opt = "DATA/loadfile.opt",
            },
            Settings = new ProductionSettings
            {
                Encoding = request.LoadFile.Encoding,
                ColumnDelimiter = FormatDelimiter(request.Delimiters.ColumnDelimiter),
                QuoteDelimiter = FormatDelimiter(request.Delimiters.QuoteDelimiter),
                ColumnProfile = request.Metadata.ColumnProfile?.Name,
                Seed = request.Metadata.Seed,
            },
            GenerationTime = $"{generationTime.TotalSeconds:F1}s",
            ValidationReport = "_validation_report.json",
            PriorManifests = priorManifests is { Count: > 0 } ? priorManifests : null,
            SupplementalValidation = supplementalValidation,
        };

        // Redaction stats
        if (request.Production.RedactedProduction && fileDataList.Count > 0)
        {
            long redactedCount = fileDataList.Count(f => f.RedactedImageRelPath is not null);
            long withheldCount = fileDataList.Count(f => f.NativePathOverride is not null);
            var reasonCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fileDataList)
            {
                if (f.RedactionReason is not null)
                {
                    reasonCounts.TryGetValue(f.RedactionReason, out var count);
                    reasonCounts[f.RedactionReason] = count + 1;
                }
            }

            manifest.RedactedFileCount = redactedCount;
            manifest.WithheldNativeFileCount = withheldCount > 0 ? withheldCount : null;
            manifest.RedactionReasons = reasonCounts.Count > 0 ? reasonCounts : null;
        }

        var json = JsonSerializer.Serialize(manifest, ManifestSerializerOptions);
        await File.WriteAllTextAsync(manifestPath, json).ConfigureAwait(false);

        return manifestPath;
    }

    private static string FormatDelimiter(string delimiter)
    {
        if (string.IsNullOrEmpty(delimiter))
        {
            return string.Empty;
        }

        if (delimiter.Length == 1 && delimiter[0] < 32)
        {
            return $"ascii:{(int)delimiter[0]}";
        }

        return $"char:{delimiter}";
    }
}

internal class ProductionManifest
{
    [JsonPropertyName("productionDate")]
    public string ProductionDate { get; set; } = string.Empty;

    [JsonPropertyName("productionId")]
    public string ProductionId { get; set; } = string.Empty;

    [JsonPropertyName("rollingSequenceNumber")]
    public int RollingSequenceNumber { get; set; }

    [JsonPropertyName("batesNumberStart")]
    public string BatesNumberStart { get; set; } = string.Empty;

    [JsonPropertyName("batesNumberEnd")]
    public string BatesNumberEnd { get; set; } = string.Empty;

    [JsonPropertyName("batesRangeMode")]
    public string BatesRangeMode { get; set; } = string.Empty;

    [JsonPropertyName("batesRange")]
    public BatesRange BatesRange { get; set; } = new();

    [JsonPropertyName("nativeFileCount")]
    public long NativeFileCount { get; set; }

    [JsonPropertyName("parentNativeFileCount")]
    public long? ParentNativeFileCount { get; set; }

    [JsonPropertyName("attachmentNativeFileCount")]
    public long? AttachmentNativeFileCount { get; set; }

    [JsonPropertyName("fileType")]
    public string FileType { get; set; } = string.Empty;

    [JsonPropertyName("volumeCount")]
    public int VolumeCount { get; set; }

    [JsonPropertyName("volumeSize")]
    public int VolumeSize { get; set; }

    [JsonPropertyName("directories")]
    public ProductionDirectories Directories { get; set; } = new();

    [JsonPropertyName("loadFiles")]
    public ProductionLoadFiles LoadFiles { get; set; } = new();

    [JsonPropertyName("settings")]
    public ProductionSettings Settings { get; set; } = new();

    [JsonPropertyName("validationReport")]
    public string? ValidationReport { get; set; }

    [JsonPropertyName("generationTime")]
    public string GenerationTime { get; set; } = string.Empty;

    [JsonPropertyName("priorManifests")]
    public System.Collections.Generic.IReadOnlyList<string>? PriorManifests { get; set; }

    [JsonPropertyName("supplementalValidation")]
    public Validation.SupplementalValidationReport? SupplementalValidation { get; set; }

    [JsonPropertyName("redactedFileCount")]
    public long? RedactedFileCount { get; set; }

    [JsonPropertyName("withheldNativeFileCount")]
    public long? WithheldNativeFileCount { get; set; }

    [JsonPropertyName("redactionReasons")]
    public System.Collections.Generic.IReadOnlyDictionary<string, long>? RedactionReasons { get; set; }
}

internal class BatesRange
{
    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;

    [JsonPropertyName("prefix")]
    public string Prefix { get; set; } = string.Empty;

    [JsonPropertyName("digits")]
    public int Digits { get; set; }
}

internal class ProductionDirectories
{
    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;

    [JsonPropertyName("natives")]
    public string Natives { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("images")]
    public string Images { get; set; } = string.Empty;

    [JsonPropertyName("redacted")]
    public string? Redacted { get; set; }
}

internal class ProductionLoadFiles
{
    [JsonPropertyName("dat")]
    public string Dat { get; set; } = string.Empty;

    [JsonPropertyName("opt")]
    public string Opt { get; set; } = string.Empty;
}

internal class ProductionSettings
{
    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = string.Empty;

    [JsonPropertyName("columnDelimiter")]
    public string ColumnDelimiter { get; set; } = string.Empty;

    [JsonPropertyName("quoteDelimiter")]
    public string QuoteDelimiter { get; set; } = string.Empty;

    [JsonPropertyName("columnProfile")]
    public string? ColumnProfile { get; set; }

    [JsonPropertyName("seed")]
    public int? Seed { get; set; }
}
