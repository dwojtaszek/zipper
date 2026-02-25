using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zipper;

/// <summary>
/// Generates the companion _properties.json audit file for loadfile-only mode.
/// </summary>
internal static class LoadfileAuditWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Writes the properties JSON file.
    /// </summary>
    /// <param name="outputPath">Path to the generated load file.</param>
    /// <param name="request">File generation request.</param>
    /// <param name="totalRecords">Total records written.</param>
    /// <param name="anomalies">List of chaos anomalies (empty if chaos disabled).</param>
    /// <returns>Path to the generated properties file.</returns>
    public static async Task<string> WriteAsync(
        string outputPath,
        FileGenerationRequest request,
        long totalRecords,
        IReadOnlyList<ChaosAnomaly>? anomalies = null)
    {
        var propertiesPath = Path.ChangeExtension(outputPath, null) + "_properties.json";

        var formatName = request.LoadFileFormat == LoadFileFormat.Opt
            ? "OPT (Image)"
            : "DAT (Metadata)";

        var audit = new AuditDocument
        {
            FileName = Path.GetFileName(outputPath),
            Format = formatName,
            TotalRecords = totalRecords,
            Properties = new AuditProperties
            {
                Encoding = request.Encoding,
                LineEnding = request.EndOfLine,
                Delimiters = new AuditDelimiters
                {
                    Column = FormatDelimiter(request.ColumnDelimiter),
                    Quote = string.IsNullOrEmpty(request.QuoteDelimiter) ? "none" : FormatDelimiter(request.QuoteDelimiter),
                    Newline = FormatDelimiter(request.NewlineDelimiter),
                    MultiValue = FormatDelimiter(request.MultiValueDelimiter),
                    NestedValue = FormatDelimiter(request.NestedValueDelimiter),
                },
            },
            ChaosMode = new AuditChaosMode
            {
                Enabled = request.ChaosMode,
                TargetAmount = request.ChaosAmount,
                TotalAnomalies = anomalies?.Count ?? 0,
                InjectedAnomalies = anomalies != null && anomalies.Count > 0
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

        var json = JsonSerializer.Serialize(audit, JsonOptions);
        await File.WriteAllTextAsync(propertiesPath, json);
        return propertiesPath;
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
