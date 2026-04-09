using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zipper;

/// <summary>
/// Writes a _manifest.json file describing a generated production set.
/// </summary>
internal static class ProductionManifestWriter
{
    /// <summary>
    /// Writes the production manifest to the specified directory.
    /// </summary>
    /// <param name="productionPath">Root directory of the production set.</param>
    /// <param name="request">The file generation request.</param>
    /// <param name="batesStart">First Bates number in the production.</param>
    /// <param name="batesEnd">Last Bates number in the production.</param>
    /// <param name="volumeCount">Number of volume subfolders created.</param>
    /// <param name="generationTime">Total time for generation.</param>
    /// <returns>Path to the manifest file.</returns>
    public static async Task<string> WriteAsync(
        string productionPath,
        FileGenerationRequest request,
        string batesStart,
        string batesEnd,
        int volumeCount,
        TimeSpan generationTime)
    {
        var manifestPath = Path.Combine(productionPath, "_manifest.json");

        var manifest = new ProductionManifest
        {
            ProductionDate = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            BatesRange = new BatesRange
            {
                Start = batesStart,
                End = batesEnd,
                Prefix = request.BatesConfig?.Prefix ?? string.Empty,
                Digits = request.BatesConfig?.Digits ?? 8,
            },
            DocumentCount = request.FileCount,
            FileType = request.FileType,
            VolumeCount = volumeCount,
            VolumeSize = request.VolumeSize,
            Directories = new ProductionDirectories
            {
                Data = "DATA",
                Natives = "NATIVES",
                Text = "TEXT",
                Images = "IMAGES",
            },
            LoadFiles = new ProductionLoadFiles
            {
                Dat = "DATA/loadfile.dat",
                Opt = "DATA/loadfile.opt",
            },
            Settings = new ProductionSettings
            {
                Encoding = request.Encoding,
                ColumnDelimiter = FormatDelimiter(request.ColumnDelimiter),
                QuoteDelimiter = FormatDelimiter(request.QuoteDelimiter),
                ColumnProfile = request.ColumnProfile?.Name,
                Seed = request.Seed,
            },
            GenerationTime = $"{generationTime.TotalSeconds:F1}s",
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        var json = JsonSerializer.Serialize(manifest, options);
        await File.WriteAllTextAsync(manifestPath, json);

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

    [JsonPropertyName("batesRange")]
    public BatesRange BatesRange { get; set; } = new();

    [JsonPropertyName("documentCount")]
    public long DocumentCount { get; set; }

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

    [JsonPropertyName("generationTime")]
    public string GenerationTime { get; set; } = string.Empty;
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
