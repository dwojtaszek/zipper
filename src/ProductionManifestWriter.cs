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
        string? batesPrefix = null,
        IFileMaterializer? materializer = null)
    {
        var manifestPath = Path.Combine(productionPath, "_manifest.json");

        fileDataList ??= Array.Empty<FileData>();
        long parentCount = fileDataList.Count > 0 ? fileDataList.Count : request.Output.FileCount;
        long attachmentCount = 0;
        if (request.Metadata.WithFamilies && request.Output.HasFileType("eml"))
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
            FileType = request.Output.FileTypeRatios is { Count: > 0 }
                ? string.Join(",", request.Output.FileTypeRatios.Select(r => r.Type))
                : request.Output.SourceFileTypes is { Count: > 0 }
                    ? string.Join(",", request.Output.SourceFileTypes)
                    : request.Output.FileType,
            VolumeCount = volumeCount,
            VolumeSize = request.Production.VolumeSize,
            Directories = new ProductionDirectories
            {
                Data = "DATA",
                Natives = "NATIVES",
                Text = "TEXT",
                Images = "IMAGES",
                Redacted = request.Production.RedactedProduction ? "REDACTED" : null,
                Originals = request.Production.SourcePathMode == Config.SourcePathMode.Originals ? "ORIGINALS" : null,
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

        // Azure Blob custom metadata (ticket #881): the derived block an upload
        // client maps onto x-ms-meta-<key>. Keys are fixed Azure-safe literals;
        // values are normalized to the tab/printable-ASCII set (REQ-225).
        manifest.Metadata = new Dictionary<string, string>
        {
            ["production_id"] = NormalizeMetadataValue(manifest.ProductionId),
            ["bates_number_start"] = NormalizeMetadataValue(manifest.BatesNumberStart),
            ["bates_number_end"] = NormalizeMetadataValue(manifest.BatesNumberEnd),
            ["volume_count"] = volumeCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
                    // Each parent with an attachment has one redacted child (in redacted mode)
                    long increment = f.Attachment.HasValue ? 2 : 1;
                    reasonCounts.TryGetValue(f.RedactionReason, out var count);
                    reasonCounts[f.RedactionReason] = count + increment;
                }
            }

            // Children are also redacted: one child per parent with attachment
            int childCount = fileDataList.Count(f => f.Attachment.HasValue);
            redactedCount += childCount;

            manifest.RedactedFileCount = redactedCount;
            manifest.WithheldNativeFileCount = withheldCount > 0 ? withheldCount : null;
            manifest.RedactionReasons = reasonCounts.Count > 0 ? reasonCounts : null;
        }

        // REQ-226: the Production Metadata block is bounded by the Azure Blob custom-metadata
        // budget. ProductionSetPostValidator reports an over-budget block as an artifact
        // finding; this guard is the by-construction counterpart so a direct caller outside
        // that validation path cannot write a manifest no upload client will accept.
        // Measured before serialization so no partial manifest lands on disk.
        var metadataBudgetBytes = MeasureProductionMetadataBudget(manifest.Metadata);
        if (metadataBudgetBytes > ProductionMetadataBudget.MaxBytes)
        {
            throw new InvalidOperationException(
                $"Production Metadata block is {metadataBudgetBytes} bytes across all keys and values; the Azure Blob metadata budget is {ProductionMetadataBudget.MaxBytes:N0} bytes.");
        }

        var json = JsonSerializer.Serialize(manifest, ManifestSerializerOptions);
        if (materializer is not null)
        {
            await materializer.WriteTextAsync(manifestPath, json, new System.Text.UTF8Encoding(false)).ConfigureAwait(false);
        }
        else
        {
            await File.WriteAllTextAsync(manifestPath, json, new System.Text.UTF8Encoding(false)).ConfigureAwait(false);
        }

        return manifestPath;
    }

    /// <summary>Sums the REQ-226 Production Metadata budget across every key and value.</summary>
    private static long MeasureProductionMetadataBudget(IReadOnlyDictionary<string, string> metadata)
    {
        long totalBytes = 0;
        foreach (var pair in metadata)
        {
            totalBytes += ProductionMetadataBudget.PairBytes(pair.Key, pair.Value);
        }

        return totalBytes;
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

    /// <summary>
    /// Normalizes a derived metadata value for Azure Blob custom metadata
    /// (ticket #881): values may contain only tab (U+0009) or ASCII characters
    /// U+0020 through U+007E. Tab is preserved. CR/LF become ' ' because
    /// newlines in metadata values are an HTTP response-splitting hazard;
    /// every other disallowed character becomes '_'.
    /// </summary>
    private static string NormalizeMetadataValue(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '\r' or '\n' => ' ',
                '\t' => c,
                >= ' ' and <= '~' => c,
                _ => '_',
            });
        }

        return builder.ToString();
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

    [JsonPropertyName("metadata")]
    public System.Collections.Generic.IReadOnlyDictionary<string, string>? Metadata { get; set; }
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

    [JsonPropertyName("originals")]
    public string? Originals { get; set; }
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
