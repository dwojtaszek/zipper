using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zipper;

/// <summary>
/// Generates the companion _properties.json audit file emitted with every generated Load File in every generation mode.
/// The chaos anomaly manifest is populated whenever the run injects Anomalies (including Production Set and Standard Mode native-file chaos).
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
        await EmitAsync(outputPath, propertiesPath, request, composedRecords,
            path => new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, PerformanceConstants.DefaultBufferSize, true),
            anomalies, format).ConfigureAwait(false);

        return propertiesPath;
    }

    /// <summary>
    /// Emits an Audit File and its eligible Chaos Expectation Sidecar to caller-owned targets.
    /// </summary>
    internal static async Task EmitAsync(
        string outputPath,
        string auditPath,
        FileGenerationRequest request,
        IReadOnlyCollection<FileData> records,
        Func<string, Stream> openTarget,
        IReadOnlyList<ChaosAnomaly>? anomalies = null,
        LoadFileFormat? format = null,
        long? totalRecordsOverride = null,
        CancellationToken cancellationToken = default)
    {
        await WriteJsonAsync(auditPath,
            GenerateAuditJson(outputPath, request, records, anomalies, format, totalRecordsOverride),
            openTarget, cancellationToken).ConfigureAwait(false);

        if (request.Chaos.ChaosMode && anomalies is not null
            && !ChaosScenarios.GeneratesAdversarialValues(request.Chaos.ChaosScenario))
        {
            await WriteJsonAsync(ExpectationSidecarPath(auditPath),
                GenerateExpectationSidecarJson(outputPath, request, records, anomalies, format, totalRecordsOverride),
                openTarget, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteJsonAsync(
        string path, string json, Func<string, Stream> openTarget, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = openTarget(path);
        await using (stream.ConfigureAwait(false))
        {
            await using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
            await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns the Chaos expectation sidecar path for an Audit File path.
    /// Sidecars keep the Audit File's base name so DAT and OPT sidecars cannot collide.
    /// </summary>
    public static string ExpectationSidecarPath(string auditPath)
    {
        ArgumentNullException.ThrowIfNull(auditPath);
        const string suffix = "_properties.json";
        return auditPath.EndsWith(suffix, StringComparison.Ordinal)
            ? string.Concat(auditPath.AsSpan(0, auditPath.Length - suffix.Length), "_chaos_expectations.json")
            : auditPath + "_chaos_expectations.json";
    }

    /// <summary>
    /// Generates the schema-versioned Chaos expectation sidecar: per anomaly, the expected
    /// consumer outcome class, affected record and columns, and the invariants that must hold.
    /// </summary>
    /// <param name="outputPath">Path to the generated load file.</param>
    /// <param name="request">File generation request.</param>
    /// <param name="composedRecords">The generated records.</param>
    /// <param name="anomalies">List of chaos anomalies.</param>
    /// <param name="format">The explicit load file format.</param>
    /// <param name="expectedDataRecords">The produced data-record count when it differs from the clean count.</param>
    /// <returns>The expectation sidecar JSON string.</returns>
    public static string GenerateExpectationSidecarJson(
        string outputPath,
        FileGenerationRequest request,
        IReadOnlyCollection<FileData> composedRecords,
        IReadOnlyList<ChaosAnomaly>? anomalies,
        LoadFileFormat? format = null,
        long? expectedDataRecords = null)
    {
        var activeFormat = format ?? (request.LoadFile.Formats.Count > 0 ? request.LoadFile.Formats[0] : LoadFileFormat.Dat);
        long expectedRecords = expectedDataRecords ?? ComputeRecordCounts(request, composedRecords, activeFormat).TotalRecords;

        var document = new ChaosExpectationDocument
        {
            SchemaVersion = 1,
            LoadFile = Path.GetFileName(outputPath),
            LoadFileFormat = FormatCode(activeFormat),
            TotalAnomalies = anomalies?.Count ?? 0,
            Invariants = new ChaosExpectationInvariants { ExpectedDataRecords = expectedRecords },
            Anomalies = anomalies is null
                ? new List<ChaosExpectationAnomaly>()
                : anomalies.Select(a => new ChaosExpectationAnomaly
                {
                    File = a.File,
                    Location = a.Location,
                    ErrorType = a.ErrorType,
                    OutcomeClass = ChaosExpectationOracle.ExpectedOutcomeClass(a.ErrorType),
                    LineNumber = a.LineNumber,
                    RecordID = a.RecordID,
                    Columns = string.IsNullOrEmpty(a.Column) || string.Equals(a.Column, "N/A", StringComparison.Ordinal)
                        ? new List<string>()
                        : new List<string> { a.Column },
                }).ToList(),
        };

        return JsonSerializer.Serialize(document, JsonOptions);
    }

    /// <summary>
    /// Computes total lines and builds the ChaosEngine for the specified format.
    /// </summary>
    public static ChaosEngine? BuildChaosEngine(
        FileGenerationRequest request,
        IReadOnlyCollection<FileData> composedRecords,
        LoadFileFormat format)
    {
        if (ChaosScenarios.GeneratesAdversarialValues(request.Chaos.ChaosScenario))
        {
            return null;
        }

        var (_, totalLines) = ComputeRecordCounts(request, composedRecords, format);
        return ChaosEngineBuilder.Build(request, totalLines, format);
    }

    internal static (long TotalRecords, long TotalLines) ComputeRecordCounts(
        FileGenerationRequest request,
        IReadOnlyCollection<FileData> composedRecords,
        LoadFileFormat format)
    {
        long totalRecords;
        if (composedRecords.Count == 0 && request.Output.FileCount > 0)
        {
            totalRecords = format == LoadFileFormat.Opt
                ? SimulateOptRecordCount(request)
                : SimulateNonOptRecordCount(request);
        }
        else
        {
            totalRecords = format switch
            {
                LoadFileFormat.Opt => ComputeOptRecordCount(request, composedRecords),
                _ => ComputeDatRecordCount(request, composedRecords)
            };
        }

        long totalLines = format switch
        {
            LoadFileFormat.Opt => totalRecords,
            LoadFileFormat.EdrmXml => Math.Max(1, totalRecords),
            _ => totalRecords + 1,
        };
        return (totalRecords, totalLines);
    }

    // Simulate* runs only for Loadfile-Only (no composed records); --types is rejected there,
    // so the request-level IsEml gate below is correct for the single-type runs that reach it.
    private static long SimulateOptRecordCount(FileGenerationRequest request)
    {
        long total = 0;
#pragma warning disable S2245
        var random = request.Metadata.Seed.HasValue ? new Random(request.Metadata.Seed.Value + 1) : new Random();
#pragma warning restore S2245
        bool withFamilies = request.Metadata.WithFamilies && request.Output.IsEml && request.LoadFile.AttachmentRate > 0;
        for (long i = 1; i <= request.Output.FileCount; i++)
        {
            int pageCount = request.Tiff.PageRange.HasValue
                ? TiffMultiPageGenerator.GetPageCount(request.Tiff.PageRange, request.Metadata.Seed, i)
                : random.Next(1, 11);
            total += pageCount;
            if (withFamilies && LoadFiles.FamilyPlan.HasAttachment(request, i))
            {
                total++;
            }
        }
        return total;
    }

    private static long SimulateNonOptRecordCount(FileGenerationRequest request)
    {
        if (request.Metadata.WithFamilies && request.Output.IsEml && request.LoadFile.AttachmentRate > 0)
        {
            long childCount = request.SimulatedFamilyChildCount
                ?? LoadFiles.FamilyPlan.GetSimulatedChildCount(request);
            return request.Output.FileCount + childCount;
        }
        return request.Output.FileCount;
    }

    private static long ComputeOptRecordCount(FileGenerationRequest request, IReadOnlyCollection<FileData> composedRecords)
    {
        long total = 0;
        bool pageCountEnabled = request.Tiff.ShouldIncludePageCount(request.Output)
            || (request.LoadfileOnly && request.SourceRecords is not null);
        bool withFamilies = request.Metadata.WithFamilies && request.Output.HasFileType("eml");

        foreach (var f in composedRecords)
        {
            // Page expansion applies only to TIFF records in a File Type mix.
            var recordIsTiff = string.Equals(f.WorkItem.EffectiveFileType(request), "tiff", StringComparison.Ordinal);
            total += (pageCountEnabled && recordIsTiff ? Math.Max(1, f.PageCount) : 1) +
                     (withFamilies ? f.GetAttachmentMetadata().Count : 0);
        }
        return total;
    }

    private static long ComputeDatRecordCount(FileGenerationRequest request, IReadOnlyCollection<FileData> composedRecords)
    {
        long total = composedRecords.Count;
        if (request.Metadata.WithFamilies && request.Output.HasFileType("eml"))
        {
            foreach (var f in composedRecords)
            {
                total += f.GetAttachmentMetadata().Count;
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
    /// <param name="totalRecordsOverride">The emitted data-record count when output rows were mutated.</param>
    /// <returns>The properties JSON string.</returns>
    public static string GenerateAuditJson(
        string outputPath,
        FileGenerationRequest request,
        IReadOnlyCollection<FileData> composedRecords,
        IReadOnlyList<ChaosAnomaly>? anomalies = null,
        LoadFileFormat? format = null,
        long? totalRecordsOverride = null)
    {
        var activeFormat = format ?? (request.LoadFile.Formats.Count > 0 ? request.LoadFile.Formats[0] : LoadFileFormat.Dat);
        var (computedTotalRecords, _) = ComputeRecordCounts(request, composedRecords, activeFormat);
        long totalRecords = totalRecordsOverride ?? computedTotalRecords;
        var formatName = FormatDisplayName(activeFormat);

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
            ChaosMode = CreateAuditChaosMode(request, anomalies),
        };

        return JsonSerializer.Serialize(audit, JsonOptions);
    }

    private static AuditChaosMode CreateAuditChaosMode(
        FileGenerationRequest request,
        IReadOnlyList<ChaosAnomaly>? anomalies)
    {
        bool adversarialValues = ChaosScenarios.GeneratesAdversarialValues(request.Chaos.ChaosScenario);
        bool isEnabled = request.Chaos.ChaosMode && (anomalies is not null || adversarialValues);
        string? targetAmount = request.Chaos.ChaosAmount;
        if (string.IsNullOrEmpty(targetAmount) && request.Chaos.ChaosScenario is { } scenarioName)
        {
            targetAmount = ChaosScenarios.GetByName(scenarioName)?.DefaultAmount;
        }

        return new AuditChaosMode
        {
            Enabled = isEnabled,
            TargetAmount = isEnabled ? targetAmount : null,
            TotalAnomalies = anomalies?.Count ?? 0,
            InjectedAnomalies = anomalies is not null && anomalies.Count > 0
                ? anomalies.Select(a => new AuditAnomalyEntry
                {
                    File = a.File,
                    LineNumber = a.LineNumber,
                    RecordID = a.RecordID,
                    Column = a.Column,
                    ErrorType = a.ErrorType,
                    Description = a.Description,
                    Location = a.Location,
                    Peer = a.Peer is not null
                        ? new AuditPeerEntry
                        {
                            File = a.Peer.File,
                            LineNumber = a.Peer.LineNumber,
                            RecordID = a.Peer.RecordID,
                            Column = a.Peer.Column,
                        }
                        : null,
                }).ToList()
                : null,
        };
    }

    private static string FormatDisplayName(LoadFileFormat format) => format switch
    {
        LoadFileFormat.Opt => "OPT (Image)",
        LoadFileFormat.Csv => "CSV (Metadata)",
        LoadFileFormat.EdrmXml => "EDRM-XML (Metadata)",
        LoadFileFormat.Concordance => "Concordance (Metadata)",
        _ => "DAT (Metadata)",
    };

    private static string FormatCode(LoadFileFormat format) => format switch
    {
        LoadFileFormat.Opt => "opt",
        LoadFileFormat.Csv => "csv",
        LoadFileFormat.EdrmXml => "edrmxml",
        LoadFileFormat.Concordance => "concordance",
        _ => "dat",
    };

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
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? File { get; init; }

        public string LineNumber { get; init; } = string.Empty;

        public string RecordID { get; init; } = string.Empty;

        public string Column { get; init; } = string.Empty;

        public string ErrorType { get; init; } = string.Empty;

        public string Description { get; init; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Location { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AuditPeerEntry? Peer { get; init; }
    }

    private record AuditPeerEntry
    {
        public string File { get; init; } = string.Empty;

        public string LineNumber { get; init; } = string.Empty;

        public string RecordID { get; init; } = string.Empty;

        public string Column { get; init; } = string.Empty;
    }

    private record ChaosExpectationDocument
    {
        public int SchemaVersion { get; init; }

        public string LoadFile { get; init; } = string.Empty;

        public string LoadFileFormat { get; init; } = string.Empty;

        public int TotalAnomalies { get; init; }

        public ChaosExpectationInvariants Invariants { get; init; } = new();

        public List<ChaosExpectationAnomaly> Anomalies { get; init; } = new();
    }

    private record ChaosExpectationInvariants
    {
        public long ExpectedDataRecords { get; init; }

        public bool RemainingRecordsParseCleanly { get; init; } = true;
    }

    private record ChaosExpectationAnomaly
    {
        public string? File { get; init; }

        public string? Location { get; init; }

        public string ErrorType { get; init; } = string.Empty;

        public string OutcomeClass { get; init; } = string.Empty;

        public string LineNumber { get; init; } = string.Empty;

        public string RecordID { get; init; } = string.Empty;

        public List<string> Columns { get; init; } = new();
    }
}
