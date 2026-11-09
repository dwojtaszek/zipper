using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Zipper.Validation;

/// <summary>
/// Verifies a Chaos run's produced Load File against its schema-versioned expectation sidecar.
/// The sidecar records, per anomaly, the expected consumer outcome class plus the invariants that
/// must still hold: the produced record count and that every remaining record parses cleanly.
/// This is the local dogfood consumer; downstream pipelines can apply the same oracle.
/// </summary>
public sealed class ChaosExpectationValidator
{
    private const string Category = "ChaosExpectation";
    private const string AuditSuffix = "_properties.json";
    private const string SidecarSuffix = "_chaos_expectations.json";

    /// <summary>
    /// Validates a Load File against its Chaos expectation sidecar.
    /// </summary>
    /// <param name="loadFilePath">The produced Load File.</param>
    /// <param name="sidecarPath">The expectation sidecar next to the Audit File.</param>
    /// <param name="request">The generation request (delimiter and encoding configuration).</param>
    /// <returns>Findings; any error means the sidecar did not validate against the produced output.</returns>
    public ValidationResult Validate(string loadFilePath, string sidecarPath, FileGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(loadFilePath);
        ArgumentNullException.ThrowIfNull(sidecarPath);
        ArgumentNullException.ThrowIfNull(request);

        var result = new ValidationResult();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(sidecarPath));
        }
        catch (JsonException exception)
        {
            result.Add(new ValidationFinding(ValidationSeverity.Error, Category, $"Expectation sidecar is not valid JSON: {exception.Message}", sidecarPath));
            return result;
        }

        using (document)
        {
            ValidateDocument(document.RootElement, loadFilePath, sidecarPath, request, result);
        }

        return result;
    }

    private static void ValidateDocument(
        JsonElement root,
        string loadFilePath,
        string sidecarPath,
        FileGenerationRequest request,
        ValidationResult result)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            AddError(result, sidecarPath, "Expectation sidecar root must be a JSON object.");
            return;
        }

        if (!root.TryGetProperty("schemaVersion", out var schemaElement)
            || !schemaElement.TryGetInt32(out int schemaVersion)
            || schemaVersion != 1)
        {
            AddError(result, sidecarPath, "Expectation sidecar schemaVersion must be 1.");
        }

        string loadFile = root.TryGetProperty("loadFile", out var loadFileElement) ? loadFileElement.GetString() ?? string.Empty : string.Empty;
        if (!string.Equals(loadFile, Path.GetFileName(loadFilePath), StringComparison.Ordinal))
        {
            AddError(result, sidecarPath, $"Expectation sidecar loadFile '{loadFile}' does not match produced Load File '{Path.GetFileName(loadFilePath)}'.");
        }

        if (!TryParseFormat(root, out var format))
        {
            AddError(result, sidecarPath, "Expectation sidecar loadFileFormat is missing or unknown.");
            return;
        }

        if (!root.TryGetProperty("anomalies", out var anomaliesElement) || anomaliesElement.ValueKind != JsonValueKind.Array)
        {
            AddError(result, sidecarPath, "Expectation sidecar anomalies must be an array.");
            return;
        }

        var anomalies = new List<SidecarAnomaly>();
        foreach (var element in anomaliesElement.EnumerateArray())
        {
            var anomaly = ReadAnomaly(element, sidecarPath, result);
            if (anomaly is not null)
            {
                anomalies.Add(anomaly);
            }
        }

        if (!root.TryGetProperty("totalAnomalies", out var totalElement)
            || !totalElement.TryGetInt32(out int totalAnomalies)
            || totalAnomalies != anomaliesElement.GetArrayLength())
        {
            AddError(result, sidecarPath, "Expectation sidecar totalAnomalies must equal the anomalies array length.");
        }

        if (!TryReadInvariants(root, out long expectedDataRecords, out bool remainingRecordsParseCleanly))
        {
            AddError(result, sidecarPath, "Expectation sidecar invariants.expectedDataRecords is missing or invalid.");
            return;
        }

        ValidateAgainstAudit(sidecarPath, anomalies, anomaliesElement.GetArrayLength(), result);

        if (format is LoadFileFormat.Dat or LoadFileFormat.Opt or LoadFileFormat.Csv or LoadFileFormat.Concordance)
        {
            ValidateProducedRows(loadFilePath, format, request, expectedDataRecords, remainingRecordsParseCleanly, anomalies, result);
        }
    }

    private static SidecarAnomaly? ReadAnomaly(JsonElement element, string sidecarPath, ValidationResult result)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            AddError(result, sidecarPath, "Expectation sidecar anomaly entries must be objects.");
            return null;
        }

        string errorType = element.TryGetProperty("errorType", out var errorTypeElement) ? errorTypeElement.GetString() ?? string.Empty : string.Empty;
        string outcomeClass = element.TryGetProperty("outcomeClass", out var outcomeElement) ? outcomeElement.GetString() ?? string.Empty : string.Empty;
        string lineNumber = element.TryGetProperty("lineNumber", out var lineElement) ? lineElement.GetString() ?? string.Empty : string.Empty;
        string recordId = element.TryGetProperty("recordID", out var recordElement) ? recordElement.GetString() ?? string.Empty : string.Empty;

        if (!ChaosOutcomeClasses.All.Contains(outcomeClass, StringComparer.Ordinal))
        {
            AddError(result, sidecarPath, $"Anomaly '{errorType}' records unknown outcome class '{outcomeClass}'.");
        }

        return new SidecarAnomaly(errorType, outcomeClass, lineNumber, recordId);
    }

    private static bool TryParseFormat(JsonElement root, out LoadFileFormat format)
    {
        format = LoadFileFormat.Dat;
        if (!root.TryGetProperty("loadFileFormat", out var formatElement))
        {
            return false;
        }

        switch (formatElement.GetString())
        {
            case "dat":
                format = LoadFileFormat.Dat;
                return true;
            case "opt":
                format = LoadFileFormat.Opt;
                return true;
            case "csv":
                format = LoadFileFormat.Csv;
                return true;
            case "concordance":
                format = LoadFileFormat.Concordance;
                return true;
            case "edrmxml":
                format = LoadFileFormat.EdrmXml;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadInvariants(JsonElement root, out long expectedDataRecords, out bool remainingRecordsParseCleanly)
    {
        expectedDataRecords = 0;
        remainingRecordsParseCleanly = true;
        if (!root.TryGetProperty("invariants", out var invariants)
            || invariants.ValueKind != JsonValueKind.Object
            || !invariants.TryGetProperty("expectedDataRecords", out var expectedElement)
            || !expectedElement.TryGetInt64(out expectedDataRecords)
            || expectedDataRecords < 0)
        {
            return false;
        }

        if (invariants.TryGetProperty("remainingRecordsParseCleanly", out var remainingElement))
        {
            remainingRecordsParseCleanly = remainingElement.ValueKind != JsonValueKind.False;
        }

        return true;
    }

    /// <summary>
    /// Cross-checks the sidecar against the produced Audit File so a sidecar cannot drift from the run that produced it.
    /// </summary>
    private static void ValidateAgainstAudit(string sidecarPath, IReadOnlyList<SidecarAnomaly> anomalies, int sidecarCount, ValidationResult result)
    {
        if (!sidecarPath.EndsWith(SidecarSuffix, StringComparison.Ordinal))
        {
            return;
        }

        string auditPath = string.Concat(sidecarPath.AsSpan(0, sidecarPath.Length - SidecarSuffix.Length), AuditSuffix);
        if (!File.Exists(auditPath))
        {
            return;
        }

        JsonDocument auditDocument;
        try
        {
            auditDocument = JsonDocument.Parse(File.ReadAllText(auditPath));
        }
        catch (JsonException exception)
        {
            AddError(result, auditPath, $"Audit File is not valid JSON: {exception.Message}");
            return;
        }

        using (auditDocument)
        {
            var root = auditDocument.RootElement;
            if (!root.TryGetProperty("chaosMode", out var chaosMode) || chaosMode.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            int auditCount = chaosMode.TryGetProperty("totalAnomalies", out var totalElement) && totalElement.TryGetInt32(out int total)
                ? total
                : 0;
            if (auditCount != sidecarCount)
            {
                AddError(result, auditPath, $"Expectation sidecar lists {sidecarCount} anomalies but the Audit File records {auditCount}.");
            }

            if (!chaosMode.TryGetProperty("injectedAnomalies", out var injected) || injected.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var unmatched = anomalies.ToList();
            foreach (var entry in injected.EnumerateArray())
            {
                string errorType = entry.TryGetProperty("errorType", out var typeElement) ? typeElement.GetString() ?? string.Empty : string.Empty;
                string lineNumber = entry.TryGetProperty("lineNumber", out var lineElement) ? lineElement.GetString() ?? string.Empty : string.Empty;
                string recordId = entry.TryGetProperty("recordID", out var recordElement) ? recordElement.GetString() ?? string.Empty : string.Empty;
                int index = unmatched.FindIndex(anomaly =>
                    string.Equals(anomaly.ErrorType, errorType, StringComparison.Ordinal)
                    && string.Equals(anomaly.LineNumber, lineNumber, StringComparison.Ordinal)
                    && string.Equals(anomaly.RecordId, recordId, StringComparison.Ordinal));
                if (index >= 0)
                {
                    unmatched.RemoveAt(index);
                }
            }

            if (unmatched.Count > 0)
            {
                var missing = unmatched[0];
                AddError(result, auditPath, $"Expectation sidecar anomaly '{missing.ErrorType}' on record '{missing.RecordId}' is not recorded in the produced Audit File.");
            }
        }
    }

    private static void ValidateProducedRows(
        string loadFilePath,
        LoadFileFormat format,
        FileGenerationRequest request,
        long expectedDataRecords,
        bool remainingRecordsParseCleanly,
        IReadOnlyList<SidecarAnomaly> anomalies,
        ValidationResult result)
    {
        if (!File.Exists(loadFilePath))
        {
            AddError(result, loadFilePath, "Produced Load File is missing.");
            return;
        }

        var lines = File.ReadAllLines(loadFilePath, ResolveEncoding(format, request));
        int dataStartIndex = format == LoadFileFormat.Opt ? 0 : 1;
        if (format != LoadFileFormat.Opt && lines.Length == 0)
        {
            AddError(result, loadFilePath, "Produced Load File is empty.");
            return;
        }

        // Record-splitting anomalies insert one physical newline each, so every later anomaly's
        // logical line number shifts by the number of splits recorded before it.
        var splitLines = anomalies
            .Where(anomaly => ChaosExpectationOracle.RecordSplittingTypes.Contains(anomaly.ErrorType)
                && TryParseLineNumber(anomaly.LineNumber, out _))
            .Select(anomaly => long.Parse(anomaly.LineNumber, CultureInfo.InvariantCulture))
            .OrderBy(lineNumber => lineNumber)
            .ToList();
        long PhysicalLine(long logicalLine) => logicalLine + splitLines.Count(splitLine => splitLine < logicalLine);

        var damagedLines = new HashSet<long>();
        foreach (var anomaly in anomalies)
        {
            if (!TryParseLineNumber(anomaly.LineNumber, out long logicalLine))
            {
                continue;
            }

            long physicalLine = PhysicalLine(logicalLine);
            damagedLines.Add(physicalLine);
            if (ChaosExpectationOracle.RecordSplittingTypes.Contains(anomaly.ErrorType))
            {
                damagedLines.Add(physicalLine + 1);
            }
        }

        // A damaged header removes the column-count oracle for the whole file; the Audit File
        // cross-check still validates the sidecar, so skip the row invariants instead of guessing.
        if (format != LoadFileFormat.Opt && damagedLines.Contains(1))
        {
            return;
        }

        int expectedFieldCount = format == LoadFileFormat.Opt ? 7 : ParseFieldCount(lines[0], format, request);
        long expectedPhysicalRows = expectedDataRecords + splitLines.Count;
        long actualPhysicalRows = Math.Max(0, lines.Length - dataStartIndex);
        if (actualPhysicalRows != expectedPhysicalRows)
        {
            AddError(result, loadFilePath, $"Produced record count {actualPhysicalRows} does not match the expectation sidecar's {expectedPhysicalRows} ({expectedDataRecords} expected data records plus {splitLines.Count} record splits).");
        }

        for (int index = dataStartIndex; index < lines.Length; index++)
        {
            long lineNumber = index + 1;
            if (remainingRecordsParseCleanly
                && !damagedLines.Contains(lineNumber)
                && ParseFieldCount(lines[index], format, request) != expectedFieldCount)
            {
                AddError(result, loadFilePath, $"Record on line {lineNumber} does not parse cleanly into {expectedFieldCount} columns.", lineNumber);
            }
        }

        if (!remainingRecordsParseCleanly)
        {
            return;
        }

        foreach (var anomaly in anomalies)
        {
            if (!TryParseLineNumber(anomaly.LineNumber, out long logicalLine)
                || ChaosExpectationOracle.RecordSplittingTypes.Contains(anomaly.ErrorType))
            {
                continue;
            }

            long lineNumber = PhysicalLine(logicalLine);
            if (lineNumber <= dataStartIndex || lineNumber > lines.Length)
            {
                continue;
            }

            int fieldCount = ParseFieldCount(lines[lineNumber - 1], format, request);
            if (ChaosExpectationOracle.StructuralCountTypes.Contains(anomaly.ErrorType)
                && string.Equals(anomaly.OutcomeClass, ChaosOutcomeClasses.ParseFails, StringComparison.Ordinal)
                && fieldCount == expectedFieldCount)
            {
                AddError(result, loadFilePath, $"Anomaly '{anomaly.ErrorType}' declares parse-fails but record on line {lineNumber} parses cleanly into {expectedFieldCount} columns.", lineNumber);
            }
            else if (ChaosExpectationOracle.ValueLevelTypes.Contains(anomaly.ErrorType)
                && string.Equals(anomaly.OutcomeClass, ChaosOutcomeClasses.ParseSucceedsWithLoss, StringComparison.Ordinal)
                && fieldCount != expectedFieldCount)
            {
                AddError(result, loadFilePath, $"Anomaly '{anomaly.ErrorType}' declares parse-succeeds-with-loss but record on line {lineNumber} does not parse cleanly.", lineNumber);
            }
        }
    }

    private static int ParseFieldCount(string line, LoadFileFormat format, FileGenerationRequest request)
    {
        if (format == LoadFileFormat.Opt)
        {
            return line.Count(character => character == ',') + 1;
        }

        if (format == LoadFileFormat.Csv)
        {
            return DatLineParser.Parse(line, ',', '"').Count;
        }

        return DatLineParser.Parse(line, request.Delimiters.GetColumnChar(), request.Delimiters.GetQuoteChar()).Count;
    }

    private static Encoding ResolveEncoding(LoadFileFormat format, FileGenerationRequest request)
    {
        // OPT implicitly emits ANSI when UTF-8 was not requested explicitly; mirrors the Audit File.
        if (format == LoadFileFormat.Opt
            && !request.LoadFile.IsEncodingExplicit
            && string.Equals(request.LoadFile.Encoding, "UTF-8", StringComparison.Ordinal))
        {
            return EncodingHelper.GetEncodingOrDefault("ANSI");
        }

        return EncodingHelper.GetEncodingOrDefault(request.LoadFile.Encoding);
    }

    private static bool TryParseLineNumber(string value, out long lineNumber) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out lineNumber) && lineNumber > 0;

    private static void AddError(ValidationResult result, string path, string message, long? lineNumber = null) =>
        result.Add(new ValidationFinding(ValidationSeverity.Error, Category, message, path, lineNumber));

    private sealed record SidecarAnomaly(string ErrorType, string OutcomeClass, string LineNumber, string RecordId);
}
