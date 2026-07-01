using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zipper;

/// <summary>
/// Generates the companion _properties.json audit file for loadfile-only mode.
/// </summary>
internal static class LoadFileAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Writes the properties JSON file.
    /// </summary>
    /// <param name="outputPath">Path to the generated load file.</param>
    /// <param name="request">File generation request.</param>
    /// <param name="composedRecords">The generated records.</param>
    /// <param name="anomalies">List of chaos anomalies (empty if chaos disabled).</param>
    /// <param name="format">The explicit load file format.</param>
    /// <returns>Path to the generated properties file.</returns>
    public static async Task<string> WriteAsync(
        string outputPath,
        FileGenerationRequest request,
        IReadOnlyCollection<FileData> composedRecords,
        IReadOnlyList<ChaosAnomaly>? anomalies = null,
        LoadFileFormat? format = null)
    {
        var propertiesPath = Path.ChangeExtension(outputPath, null) + "_properties.json";
        var json = GenerateAuditJson(outputPath, request, composedRecords, anomalies, format);
        await File.WriteAllTextAsync(propertiesPath, json).ConfigureAwait(false);
        return propertiesPath;
    }

    /// <summary>
    /// Computes total lines and builds the ChaosEngine for the specified format.
    /// </summary>
    public static ChaosEngine? BuildChaosEngine(
        FileGenerationRequest request,
        IReadOnlyCollection<FileData> composedRecords,
        LoadFileFormat format)
    {
        var (_, totalLines) = ComputeRecordCounts(request, composedRecords, format);
        return ChaosEngineBuilder.Build(request, totalLines, format);
    }

    private static (long TotalRecords, long TotalLines) ComputeRecordCounts(
        FileGenerationRequest request,
        IReadOnlyCollection<FileData> composedRecords,
        LoadFileFormat format)
    {
        long totalRecords;
        if (composedRecords.Count == 0 && request.Output.FileCount > 0)
        {
            totalRecords = request.Output.FileCount;
        }
        else
        {
            totalRecords = format switch
            {
                LoadFileFormat.Opt => ComputeOptRecordCount(request, composedRecords),
                _ => ComputeDatRecordCount(request, composedRecords)
            };
        }

        long totalLines = format == LoadFileFormat.Opt ? totalRecords : totalRecords + 1;
        return (totalRecords, totalLines);
    }

    private static long ComputeOptRecordCount(FileGenerationRequest request, IReadOnlyCollection<FileData> composedRecords)
    {
        long total = 0;
        bool includePageCount = request.Tiff.ShouldIncludePageCount(request.Output);
        bool withFamilies = request.Metadata.WithFamilies && request.Output.IsEml;

        foreach (var f in composedRecords)
        {
            total += (includePageCount ? Math.Max(1, f.PageCount) : 1) +
                     (withFamilies && f.Attachment.HasValue ? 1 : 0);
        }
        return total;
    }

    private static long ComputeDatRecordCount(FileGenerationRequest request, IReadOnlyCollection<FileData> composedRecords)
    {
        long total = composedRecords.Count;
        if (request.Metadata.WithFamilies && request.Output.IsEml)
        {
            foreach (var f in composedRecords)
            {
                if (f.Attachment.HasValue)
                {
                    total++;
                }
            }
        }
        return total;
    }

    /// <summary>
    /// Generates the properties JSON string without writing to disk.
    /// </summary>
    /// <param name="outputPath">Path to the generated load file.</param>
    /// <param name="request">File generation request.</param>
    /// <param name="composedRecords">The generated records.</param>
    /// <param name="anomalies">List of chaos anomalies.</param>
    /// <param name="format">The explicit load file format.</param>
    /// <returns>The properties JSON string.</returns>
    public static string GenerateAuditJson(
        string outputPath,
        FileGenerationRequest request,
        IReadOnlyCollection<FileData> composedRecords,
        IReadOnlyList<ChaosAnomaly>? anomalies = null,
        LoadFileFormat? format = null)
    {
        var activeFormat = format ?? (request.LoadFile.Formats.Count > 0 ? request.LoadFile.Formats[0] : LoadFileFormat.Dat);
        var (totalRecords, _) = ComputeRecordCounts(request, composedRecords, activeFormat);
        var formatName = activeFormat == LoadFileFormat.Opt
            ? "OPT (Image)"
            : "DAT (Metadata)";

        AuditDelimiters delimiters;
        if (activeFormat == LoadFileFormat.Opt)
        {
            delimiters = new AuditDelimiters
            {
                Column = "char:,",
                Quote = "none",
                Newline = "none",
                MultiValue = "none",
                NestedValue = "none",
            };
        }
        else
        {
            delimiters = new AuditDelimiters
            {
                Column = FormatDelimiter(request.Delimiters.ColumnDelimiter),
                Quote = string.IsNullOrEmpty(request.Delimiters.QuoteDelimiter) ? "none" : FormatDelimiter(request.Delimiters.QuoteDelimiter),
                Newline = FormatDelimiter(request.Delimiters.NewlineDelimiter),
                MultiValue = FormatDelimiter(request.Delimiters.MultiValueDelimiter),
                NestedValue = FormatDelimiter(request.Delimiters.NestedValueDelimiter),
            };
        }

        var encodingName = request.LoadFile.Encoding;
        if (activeFormat == LoadFileFormat.Opt)
        {
            if (!request.LoadFile.IsEncodingExplicit && string.Equals(request.LoadFile.Encoding, "UTF-8", StringComparison.Ordinal))
            {
                encodingName = "ANSI";
            }
        }

        var audit = new AuditDocument
        {
            FileName = Path.GetFileName(outputPath),
            Format = formatName,
            TotalRecords = totalRecords,
            Properties = new AuditProperties
            {
                Encoding = encodingName,
                LineEnding = request.Delimiters.EndOfLine,
                Delimiters = delimiters,
            },
            ChaosMode = new AuditChaosMode
            {
                Enabled = request.Chaos.ChaosMode && anomalies is not null,
                TargetAmount = request.Chaos.ChaosMode && anomalies is not null ? request.Chaos.ChaosAmount : null,
                TotalAnomalies = anomalies?.Count ?? 0,
                InjectedAnomalies = anomalies is not null && anomalies.Count > 0
                    ? anomalies.Select(a => new AuditAnomalyEntry
                    {
                        LineNumber = a.LineNumber,
                        RecordID = a.RecordID,
                        Column = a.Column,
                        ErrorType = a.ErrorType,
                        Description = a.Description,
                    }).ToList()
                    : null,
            },
        };

        return JsonSerializer.Serialize(audit, JsonOptions);
    }

    private static string FormatDelimiter(string delimiter)
    {
        if (string.IsNullOrEmpty(delimiter))
        {
            return "none";
        }

        int code = delimiter[0];
        if (code < 32 || code > 126)
        {
            return $"ascii:{code}";
        }

        return $"char:{delimiter[0]}";
    }

    private record AuditDocument
    {
        public string FileName { get; init; } = string.Empty;

        public string Format { get; init; } = string.Empty;

        public long TotalRecords { get; init; }

        public AuditProperties Properties { get; init; } = new();

        public AuditChaosMode ChaosMode { get; init; } = new();
    }

    private record AuditProperties
    {
        public string Encoding { get; init; } = string.Empty;

        public string LineEnding { get; init; } = string.Empty;

        public AuditDelimiters Delimiters { get; init; } = new();
    }

    private record AuditDelimiters
    {
        public string Column { get; init; } = string.Empty;

        public string Quote { get; init; } = string.Empty;

        public string Newline { get; init; } = string.Empty;

        public string MultiValue { get; init; } = string.Empty;

        public string NestedValue { get; init; } = string.Empty;
    }

    private record AuditChaosMode
    {
        public bool Enabled { get; init; }

        public string? TargetAmount { get; init; }

        public int TotalAnomalies { get; init; }

        public List<AuditAnomalyEntry>? InjectedAnomalies { get; init; }
    }

    private record AuditAnomalyEntry
    {
        public string LineNumber { get; init; } = string.Empty;

        public string RecordID { get; init; } = string.Empty;

        public string Column { get; init; } = string.Empty;

        public string ErrorType { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;
    }
}
