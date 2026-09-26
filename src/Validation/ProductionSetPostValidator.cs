using System.Text.Json.Serialization;

namespace Zipper.Validation;

public class ProductionSetValidationReport
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "passed";

    [JsonPropertyName("errorCount")]
    public int ErrorCount { get; set; }

    [JsonPropertyName("warningCount")]
    public int WarningCount { get; set; }

    [JsonPropertyName("checkedFileCounts")]
    public Dictionary<string, int> CheckedFileCounts { get; set; } = new();

    [JsonPropertyName("checkedLoadFileRowCounts")]
    public Dictionary<string, int> CheckedLoadFileRowCounts { get; set; } = new();

    [JsonPropertyName("findings")]
    public List<ValidationReportFinding> Findings { get; set; } = new();
}

public class ValidationReportFinding
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("line")]
    public long? Line { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;
}

internal sealed class ProductionSetPostValidator
{
    public static ProductionSetValidationReport Validate(string productionPath, FileGenerationRequest request)
    {
        var state = new ValidationState
        {
            Request = request,
            ProductionPath = productionPath,
            DatPath = Path.Combine(productionPath, "DATA", "loadfile.dat"),
            OptPath = Path.Combine(productionPath, "DATA", "loadfile.opt"),
            SkipNativePathValidation = string.Equals(request.Production.WithheldNativePolicy, "replace-with-placeholder", StringComparison.OrdinalIgnoreCase),
        };

        ValidateDatLoadFile(state);
        ValidateManifest(state);
        ValidateBatesContinuity(state);
        ValidateOptLoadFile(state);
        FinalizeReport(state);

        return state.Report;
    }

    /// <summary>
    /// The mutable per-run state shared by the per-concern validation steps.
    ///
    /// Kept as one flat bag on purpose (#1017): the fields are grouped by concern
    /// below rather than nested into per-concern records, because no group has a
    /// single owner. The prior-Manifest fields in particular are written by one step
    /// and read by two others (<see cref="ParentBatesList"/> is filled while reading
    /// the DAT and then consumed by both Manifest and Bates-continuity validation), so
    /// splitting the bag by concern would hide that coupling rather than remove it.
    ///
    /// The real constraint this bag imposes is that <see cref="Validate"/>'s step
    /// order is load-bearing: each step consumes state a later-listed step produced.
    /// Reordering those five calls compiles, passes review, and silently changes which
    /// findings are reported. Keep the order, and keep it in this comment.
    /// </summary>
    private sealed class ValidationState
    {
        // --- Inputs: the request and the resolved paths on disk ---

        public required FileGenerationRequest Request { get; init; }
        public required string ProductionPath { get; init; }
        public required string DatPath { get; init; }
        public required string OptPath { get; init; }
        public required bool SkipNativePathValidation { get; init; }

        // --- Output: the report being accumulated as the steps run ---
        public ProductionSetValidationReport Report { get; } = new();
        public List<ValidationReportFinding> Findings => Report.Findings;

        // --- Which Load File a finding is attributed to ---
        public string DatRelPath { get; } = "DATA/loadfile.dat";
        public string OptRelPath { get; } = "DATA/loadfile.opt";

        // --- Row counts, reported as checkedLoadFileRowCounts ---
        public int DatRowsChecked { get; set; }
        public int OptRowsChecked { get; set; }

        // --- Referenced-file counts, reported as checkedFileCounts ---
        public int CheckedNativesCount { get; set; }
        public int CheckedTextsCount { get; set; }
        public int CheckedImagesCount { get; set; }

        // --- Uniqueness accumulators ---
        public HashSet<string> SeenDocIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SeenBatesNumbers { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SeenOptBates { get; } = new(StringComparer.OrdinalIgnoreCase);

        // --- Bates continuity between this set's Load File and its Production Manifest ---
        public List<string> ParentBatesList { get; } = [];
        public string? ManifestStart { get; set; }
        public string? ManifestEnd { get; set; }
        public int? RollingSeqNum { get; set; }
    }

    /// <summary>Resolved DAT header column positions (-1 when the column is absent).</summary>
    private sealed record DatColumnIndexes(
        int DocId,
        int Bates,
        int Native,
        int Text,
        int Image,
        int ParentId,
        int RedactedImage,
        int RedactedText,
        int NativeWithheld);

    private static DatColumnIndexes ResolveDatColumnIndexes(List<string> headers)
    {
        return new DatColumnIndexes(
            headers.FindIndex(h => string.Equals(h, "DOCID", StringComparison.OrdinalIgnoreCase)),
            headers.FindIndex(h => string.Equals(h, "BATES_NUMBER", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "BATES", StringComparison.OrdinalIgnoreCase)),
            headers.FindIndex(h => string.Equals(h, "NATIVE_PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "PATH", StringComparison.OrdinalIgnoreCase)),
            headers.FindIndex(h => string.Equals(h, "TEXT_PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "TEXT", StringComparison.OrdinalIgnoreCase)),
            headers.FindIndex(h => string.Equals(h, "IMAGE_PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "IMAGE", StringComparison.OrdinalIgnoreCase)),
            headers.FindIndex(h => string.Equals(h, "PARENTDOCID", StringComparison.OrdinalIgnoreCase)),
            headers.FindIndex(h => string.Equals(h, "REDACTED_IMAGE_PATH", StringComparison.OrdinalIgnoreCase)),
            headers.FindIndex(h => string.Equals(h, "REDACTED_TEXT_PATH", StringComparison.OrdinalIgnoreCase)),
            headers.FindIndex(h => string.Equals(h, "NATIVE_WITHHELD", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// A reference from one Load File record to a file on disk: which Load File and line
    /// the reference appears on, what kind of file it names, and the path as the Load File
    /// spells it. Bundled so <see cref="AddReferencedFileFinding"/> takes one argument
    /// instead of a positional clump.
    ///
    /// Deliberately not called a Native File: the referenced file is a Native File only
    /// for the native call site. The others reference an Extracted Text companion, a
    /// redaction derivative, or an OPT image record.
    ///
    /// The resolved on-disk path is deliberately absent: it is always
    /// <c>Path.Combine(ProductionPath, ReferencedPath)</c>, so accepting it as a field
    /// would let a call site pass a path that disagrees with the one the Load File names
    /// and have the finding blame the wrong file.
    /// </summary>
    private readonly record struct ReferencedFile(
        string RelPath,
        long Line,
        string KindLabel,
        string ReferencedPath);

    private static void AddReferencedFileFinding(ValidationState state, ReferencedFile reference)
    {
        var fullPath = Path.Combine(state.ProductionPath, reference.ReferencedPath.Replace('\\', '/'));
        if (!File.Exists(fullPath))
        {
            state.Findings.Add(new ValidationReportFinding
            {
                Code = "PathExistence",
                Severity = "error",
                Path = reference.RelPath,
                Line = reference.Line,
                Message = $"Referenced {reference.KindLabel} file '{reference.ReferencedPath}' does not exist."
            });
        }
    }

    private static void ValidateDatLoadFile(ValidationState state)
    {
        // 1. Verify DAT load file existence and contents
        if (!File.Exists(state.DatPath))
        {
            state.Findings.Add(new ValidationReportFinding
            {
                Code = "PathExistence",
                Severity = "error",
                Path = state.DatRelPath,
                Message = "DAT load file does not exist."
            });
        }
        else
        {
            var encoding = EncodingHelper.GetEncodingOrDefault(state.Request.LoadFile?.Encoding);
            var datLines = File.ReadLines(state.DatPath, encoding);

            using (var enumerator = datLines.GetEnumerator())
            {
                if (enumerator.MoveNext())
                {
                    state.DatRowsChecked++;
                    var headerLine = enumerator.Current;
                    var colDelimChar = state.Request.Delimiters?.GetColumnChar() ?? '\x14';
                    var quoteDelimChar = state.Request.Delimiters?.GetQuoteChar() ?? '\xfe';

                    var headers = ParseDatLine(headerLine, colDelimChar, quoteDelimChar);
                    var columns = ResolveDatColumnIndexes(headers);

                    int i = 0;
                    while (enumerator.MoveNext())
                    {
                        i++;
                        state.DatRowsChecked++;
                        var line = enumerator.Current;
                        if (string.IsNullOrEmpty(line))
                            continue;

                        var fields = ParseDatLine(line, colDelimChar, quoteDelimChar);
                        ValidateDatRow(state, columns, headers.Count, fields, i + 1);
                    }
                }
            }
        }
    }

    private static void ValidateDatRow(ValidationState state, DatColumnIndexes columns, int headerCount, List<string> fields, long lineNumber)
    {
        if (fields.Count != headerCount)
        {
            state.Findings.Add(new ValidationReportFinding
            {
                Code = "ColumnCount",
                Severity = "error",
                Path = state.DatRelPath,
                Line = lineNumber,
                Message = $"Expected {headerCount} columns, got {fields.Count} on line {lineNumber}"
            });
        }

        ValidateDatDocId(state, columns.DocId, fields, lineNumber);
        ValidateDatBates(state, columns, fields, lineNumber);
        ValidateDatNativePath(state, columns.Native, fields, lineNumber);
        ValidateDatTextPath(state, columns.Text, fields, lineNumber);
        ValidateDatImagePath(state, columns.Image, fields, lineNumber);
        ValidateDatRedactedPath(state, columns.RedactedImage, fields, lineNumber, "redacted image");
        ValidateDatRedactedPath(state, columns.RedactedText, fields, lineNumber, "redacted text");
        ValidateDatNativeWithheld(state, columns.NativeWithheld, fields, lineNumber);
    }

    private static void ValidateDatDocId(ValidationState state, int docIdIdx, List<string> fields, long lineNumber)
    {
        // DOCID uniqueness
        if (docIdIdx >= 0 && docIdIdx < fields.Count)
        {
            var docId = fields[docIdIdx];
            if (!string.IsNullOrEmpty(docId))
            {
                if (!state.SeenDocIds.Add(docId))
                {
                    state.Findings.Add(new ValidationReportFinding
                    {
                        Code = "UniqueId",
                        Severity = "error",
                        Path = state.DatRelPath,
                        Line = lineNumber,
                        Message = $"Duplicate DOCID: '{docId}'"
                    });
                }
            }
        }
    }

    private static void ValidateDatBates(ValidationState state, DatColumnIndexes columns, List<string> fields, long lineNumber)
    {
        // BATES_NUMBER uniqueness and consistency
        string batesVal = string.Empty;
        if (columns.Bates >= 0 && columns.Bates < fields.Count)
        {
            batesVal = fields[columns.Bates];
            if (!string.IsNullOrEmpty(batesVal))
            {
                if (!state.SeenBatesNumbers.Add(batesVal))
                {
                    state.Findings.Add(new ValidationReportFinding
                    {
                        Code = "UniqueId",
                        Severity = "error",
                        Path = state.DatRelPath,
                        Line = lineNumber,
                        Message = $"Duplicate Bates Number: '{batesVal}'"
                    });
                }
            }
        }

        // Parent/Child Bates range consistency check
        if (!string.IsNullOrEmpty(batesVal))
        {
            bool isParent = columns.ParentId == -1 || columns.ParentId >= fields.Count || string.IsNullOrEmpty(fields[columns.ParentId]);
            if (isParent)
            {
                state.ParentBatesList.Add(batesVal);
            }
            else
            {
                var parentDocId = fields[columns.ParentId];
                if (!batesVal.StartsWith(parentDocId, StringComparison.Ordinal))
                {
                    state.Findings.Add(new ValidationReportFinding
                    {
                        Code = "BatesConsistency",
                        Severity = "error",
                        Path = state.DatRelPath,
                        Line = lineNumber,
                        Message = $"Child Bates number '{batesVal}' does not start with parent Doc ID '{parentDocId}'"
                    });
                }
            }
        }
    }

    private static void ValidateDatNativePath(ValidationState state, int nativeIdx, List<string> fields, long lineNumber)
    {
        // Native path existence
        if (nativeIdx >= 0 && nativeIdx < fields.Count && !state.SkipNativePathValidation)
        {
            var nativePath = fields[nativeIdx];
            if (!string.IsNullOrEmpty(nativePath))
            {
                state.CheckedNativesCount++;
                AddReferencedFileFinding(state, new ReferencedFile(state.DatRelPath, lineNumber, "native", nativePath));
            }
        }
    }

    private static void ValidateDatTextPath(ValidationState state, int textIdx, List<string> fields, long lineNumber)
    {
        // Text path existence
        if (textIdx >= 0 && textIdx < fields.Count)
        {
            var textPath = fields[textIdx];
            if (!string.IsNullOrEmpty(textPath))
            {
                state.CheckedTextsCount++;
                AddReferencedFileFinding(state, new ReferencedFile(state.DatRelPath, lineNumber, "text", textPath));
            }
        }
    }

    private static void ValidateDatImagePath(ValidationState state, int imageIdx, List<string> fields, long lineNumber)
    {
        // Image path existence
        if (imageIdx >= 0 && imageIdx < fields.Count)
        {
            var imagePath = fields[imageIdx];
            if (!string.IsNullOrEmpty(imagePath))
            {
                state.CheckedImagesCount++;
                var fullImagePath = Path.Combine(state.ProductionPath, imagePath.Replace('\\', '/'));
                if (!File.Exists(fullImagePath))
                {
                    var dir = Path.GetDirectoryName(fullImagePath) ?? string.Empty;
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fullImagePath);
                    var ext = Path.GetExtension(fullImagePath);
                    var pageOnePath = Path.Combine(dir, $"{fileNameWithoutExt}_001{ext}");

                    if (!File.Exists(pageOnePath))
                    {
                        state.Findings.Add(new ValidationReportFinding
                        {
                            Code = "PathExistence",
                            Severity = "error",
                            Path = state.DatRelPath,
                            Line = lineNumber,
                            Message = $"Referenced image file '{imagePath}' does not exist."
                        });
                    }
                }
            }
        }
    }

    private static void ValidateDatRedactedPath(ValidationState state, int redactedIdx, List<string> fields, long lineNumber, string kindLabel)
    {
        // Redacted image/text path existence
        if (redactedIdx >= 0 && redactedIdx < fields.Count)
        {
            var redactedPath = fields[redactedIdx];
            if (!string.IsNullOrEmpty(redactedPath))
            {
                AddReferencedFileFinding(state, new ReferencedFile(state.DatRelPath, lineNumber, kindLabel, redactedPath));
            }
        }
    }

    private static void ValidateDatNativeWithheld(ValidationState state, int nativeWithheldIdx, List<string> fields, long lineNumber)
    {
        // NATIVE_WITHHELD value validation
        if (nativeWithheldIdx >= 0 && nativeWithheldIdx < fields.Count)
        {
            var withheldVal = fields[nativeWithheldIdx];
            if (string.IsNullOrEmpty(withheldVal) ||
                (!string.Equals(withheldVal, "YES", StringComparison.OrdinalIgnoreCase) &&
                 !string.Equals(withheldVal, "NO", StringComparison.OrdinalIgnoreCase)))
            {
                state.Findings.Add(new ValidationReportFinding
                {
                    Code = "InvalidValue",
                    Severity = "error",
                    Path = state.DatRelPath,
                    Line = lineNumber,
                    Message = $"NATIVE_WITHHELD must be 'YES' or 'NO', got '{withheldVal}'"
                });
            }
        }
    }

    private static void ValidateManifest(ValidationState state)
    {
        // Manifest Bates range consistency
        var manifestPath = Path.Combine(state.ProductionPath, "_manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (doc.RootElement.TryGetProperty("batesNumberStart", out var startElem))
                {
                    state.ManifestStart = startElem.GetString();
                }
                if (doc.RootElement.TryGetProperty("batesNumberEnd", out var endElem))
                {
                    state.ManifestEnd = endElem.GetString();
                }
                if (doc.RootElement.TryGetProperty("rollingSequenceNumber", out var seqElem) &&
                    seqElem.TryGetInt32(out var sNum))
                {
                    state.RollingSeqNum = sNum;
                }

                // Azure Blob custom metadata constraints (ticket #881, REQ-225/REQ-226)
                if (doc.RootElement.TryGetProperty("metadata", out var metadataElem))
                {
                    if (metadataElem.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        ValidateProductionMetadata(state, metadataElem);
                    }
                    else
                    {
                        // Present but not an object (e.g. hand-edited): report rather than silently skipping.
                        state.Findings.Add(new ValidationReportFinding
                        {
                            Code = "MetadataAzureConstraint",
                            Severity = "error",
                            Path = "_manifest.json",
                            Message = $"Manifest 'metadata' must be an object of string key/value pairs, but it is a {metadataElem.ValueKind}."
                        });
                    }
                }

                if (state.ParentBatesList.Count > 0)
                {
                    if (!string.IsNullOrEmpty(state.ManifestStart) && !string.Equals(state.ParentBatesList[0], state.ManifestStart, StringComparison.Ordinal))
                    {
                        state.Findings.Add(new ValidationReportFinding
                        {
                            Code = "BatesConsistency",
                            Severity = "error",
                            Path = state.DatRelPath,
                            Message = $"DAT first Bates number '{state.ParentBatesList[0]}' does not match manifest start '{state.ManifestStart}'"
                        });
                    }
                    if (!string.IsNullOrEmpty(state.ManifestEnd) && !string.Equals(state.ParentBatesList[^1], state.ManifestEnd, StringComparison.Ordinal))
                    {
                        state.Findings.Add(new ValidationReportFinding
                        {
                            Code = "BatesConsistency",
                            Severity = "error",
                            Path = state.DatRelPath,
                            Message = $"DAT last Bates number '{state.ParentBatesList[^1]}' does not match manifest end '{state.ManifestEnd}'"
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or System.IO.IOException)
            {
                state.Findings.Add(new ValidationReportFinding
                {
                    Code = "ManifestSyntax",
                    Severity = "error",
                    Path = "_manifest.json",
                    Message = $"Manifest file '_manifest.json' could not be read or parsed: {ex.Message}"
                });
            }
        }
    }

    /// <summary>Azure Blob metadata keys follow the C# identifier rule: start with a letter or underscore, then ASCII letters, digits, or underscores (ticket #881).</summary>
    private static bool IsAzureSafeKey(string key)
    {
        if (key.Length == 0 || !(char.IsAsciiLetter(key[0]) || key[0] == '_'))
        {
            return false;
        }

        for (var i = 1; i < key.Length; i++)
        {
            if (!(char.IsAsciiLetterOrDigit(key[i]) || key[i] == '_'))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Enforces the Azure Blob custom-metadata constraints on the manifest's
    /// <c>metadata</c> block (ticket #881, REQ-226): keys must match the Azure
    /// C#-identifier rule (start with a letter or underscore, then ASCII
    /// letters, digits, or underscores), values may contain only tab (U+0009)
    /// or ASCII characters U+0020 through U+007E (CR/LF are rejected as a
    /// subset: newlines in metadata values are an HTTP response-splitting
    /// hazard), and the combined size of all keys and values must stay within
    /// the 8,192-byte Azure metadata budget.
    /// The generator emits normalized values (REQ-225); this guards the
    /// artifact contract for pipeline-edited manifests and future metadata
    /// sources.
    /// </summary>
    private static void ValidateProductionMetadata(ValidationState state, System.Text.Json.JsonElement metadata)
    {
        long totalBytes = 0;
        foreach (var pair in metadata.EnumerateObject())
        {
            if (!IsAzureSafeKey(pair.Name))
            {
                state.Findings.Add(new ValidationReportFinding
                {
                    Code = "MetadataAzureConstraint",
                    Severity = "error",
                    Path = "_manifest.json",
                    Message = $"Metadata key '{pair.Name}' violates the Azure Blob naming rule: keys must start with a letter or underscore and contain only ASCII letters, digits, or underscores."
                });
            }

            if (pair.Value.ValueKind != System.Text.Json.JsonValueKind.String)
            {
                state.Findings.Add(new ValidationReportFinding
                {
                    Code = "MetadataAzureConstraint",
                    Severity = "error",
                    Path = "_manifest.json",
                    Message = $"Metadata value for '{pair.Name}' must be a string."
                });

                // A non-string value is reported above, but its key still counts toward the
                // budget — the value simply measures as zero bytes.
                totalBytes += ProductionMetadataBudget.PairBytes(pair.Name, string.Empty);
                continue;
            }

            var value = pair.Value.GetString() ?? string.Empty;
            if (value.Any(c => c != '\t' && (c < '\u0020' || c > '\u007e')))
            {
                state.Findings.Add(new ValidationReportFinding
                {
                    Code = "MetadataAzureConstraint",
                    Severity = "error",
                    Path = "_manifest.json",
                    Message = $"Metadata value for '{pair.Name}' contains a character outside the allowed Azure Blob metadata set: tab (U+0009) or U+0020 through U+007E."
                });
            }

            totalBytes += ProductionMetadataBudget.PairBytes(pair.Name, value);
        }

        if (totalBytes > ProductionMetadataBudget.MaxBytes)
        {
            state.Findings.Add(new ValidationReportFinding
            {
                Code = "MetadataAzureConstraint",
                Severity = "error",
                Path = "_manifest.json",
                Message = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Metadata block is {0} bytes across all keys and values; the Azure Blob metadata budget is {1:N0} bytes.",
                    totalBytes,
                    ProductionMetadataBudget.MaxBytes)
            });
        }
    }

    private static void ValidateBatesContinuity(ValidationState state)
    {
        // Bates range continuity/sequence check
        if (state.Request.Bates != null && state.ParentBatesList.Count > 0)
        {
            var effectiveBates = ResolveEffectiveBates(state.Request, state.RollingSeqNum, state.ManifestStart);
            var batesSequence = BatesSequence.FromConfig(effectiveBates);
            for (int i = 0; i < state.ParentBatesList.Count; i++)
            {
                var expectedBates = batesSequence.Format(i).ToString();
                var actualBates = state.ParentBatesList[i];
                if (!string.Equals(actualBates, expectedBates, StringComparison.Ordinal))
                {
                    state.Findings.Add(new ValidationReportFinding
                    {
                        Code = "BatesConsistency",
                        Severity = "error",
                        Path = state.DatRelPath,
                        Message = $"Bates range inconsistency: expected '{expectedBates}' at parent index {i}, but got '{actualBates}'"
                    });
                }
            }
        }
    }

    private static void ValidateOptLoadFile(ValidationState state)
    {
        // 2. Verify OPT load file existence and contents
        if (!File.Exists(state.OptPath))
        {
            state.Findings.Add(new ValidationReportFinding
            {
                Code = "PathExistence",
                Severity = "error",
                Path = state.OptRelPath,
                Message = "OPT load file does not exist."
            });
        }
        else
        {
            var optEncoding = EncodingHelper.GetEncodingOrDefault(state.Request.LoadFile?.Encoding);
            int i = -1;
            string? firstOptBates = null;
            string? lastOptBates = null;
            foreach (var line in File.ReadLines(state.OptPath, optEncoding))
            {
                i++;
                state.OptRowsChecked++;
                if (string.IsNullOrEmpty(line))
                    continue;

                var columns = line.Split(',');
                ValidateOptRow(state, columns, i + 1, ref firstOptBates, ref lastOptBates);
            }

            // OPT Bates boundaries consistency with manifest
            if (!string.IsNullOrEmpty(firstOptBates) && !string.IsNullOrEmpty(state.ManifestStart) && !firstOptBates.StartsWith(state.ManifestStart, StringComparison.Ordinal))
            {
                state.Findings.Add(new ValidationReportFinding
                {
                    Code = "BatesConsistency",
                    Severity = "error",
                    Path = state.OptRelPath,
                    Message = $"OPT first Bates number '{firstOptBates}' does not match manifest start '{state.ManifestStart}'"
                });
            }
            if (!string.IsNullOrEmpty(lastOptBates) && !string.IsNullOrEmpty(state.ManifestEnd) && !lastOptBates.StartsWith(state.ManifestEnd, StringComparison.Ordinal))
            {
                state.Findings.Add(new ValidationReportFinding
                {
                    Code = "BatesConsistency",
                    Severity = "error",
                    Path = state.OptRelPath,
                    Message = $"OPT last Bates number '{lastOptBates}' does not match manifest end '{state.ManifestEnd}'"
                });
            }
        }
    }

    private static void ValidateOptRow(ValidationState state, string[] columns, long lineNumber, ref string? firstOptBates, ref string? lastOptBates)
    {
        if (columns.Length != 7)
        {
            state.Findings.Add(new ValidationReportFinding
            {
                Code = "OptBoundary",
                Severity = "error",
                Path = state.OptRelPath,
                Line = lineNumber,
                Message = $"OPT line {lineNumber} has {columns.Length} columns, expected 7"
            });
        }

        // OPT Bates uniqueness
        if (columns.Length > 0)
        {
            var optBates = columns[0];
            if (!string.IsNullOrEmpty(optBates))
            {
                firstOptBates ??= optBates;
                lastOptBates = optBates;
                if (!state.SeenOptBates.Add(optBates))
                {
                    state.Findings.Add(new ValidationReportFinding
                    {
                        Code = "UniqueId",
                        Severity = "error",
                        Path = state.OptRelPath,
                        Line = lineNumber,
                        Message = $"Duplicate Bates Number in OPT: '{optBates}'"
                    });
                }
            }
        }

        // OPT Image path existence
        if (columns.Length > 2)
        {
            var imagePath = columns[2];
            if (!string.IsNullOrEmpty(imagePath))
            {
                AddReferencedFileFinding(state, new ReferencedFile(state.OptRelPath, lineNumber, "image", imagePath));
            }
        }
    }

    private static void FinalizeReport(ValidationState state)
    {
        var report = state.Report;
        var findings = state.Findings;

        // Set counts and status
        report.ErrorCount = findings.Count(f => string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase));
        report.WarningCount = findings.Count(f => string.Equals(f.Severity, "warning", StringComparison.OrdinalIgnoreCase));
        report.Status = report.ErrorCount > 0 ? "failed" : "passed";

        report.CheckedFileCounts = new Dictionary<string, int>
        {
            ["dat"] = File.Exists(state.DatPath) ? 1 : 0,
            ["opt"] = File.Exists(state.OptPath) ? 1 : 0,
            ["native"] = state.CheckedNativesCount,
            ["text"] = state.CheckedTextsCount,
            ["image"] = state.CheckedImagesCount
        };

        report.CheckedLoadFileRowCounts = new Dictionary<string, int>
        {
            ["dat"] = state.DatRowsChecked,
            ["opt"] = state.OptRowsChecked
        };
    }

    private static Config.BatesNumberConfig ResolveEffectiveBates(FileGenerationRequest request, int? rollingSeqNum, string? manifestStart)
    {
        var bates = request.Bates!;
        if (rollingSeqNum.HasValue && rollingSeqNum.Value >= 1 && !string.IsNullOrEmpty(manifestStart))
        {
            // If request.Bates already formats index 0 to manifestStart, it is already effective
            if (string.Equals(BatesSequence.FromConfig(bates).Format(0).ToString(), manifestStart, StringComparison.Ordinal))
            {
                return bates;
            }

            int rollingIndex = rollingSeqNum.Value - 1;
            long start;
            if (request.Production.RollingBatesMode == Config.RollingBatesMode.Restart)
            {
                start = bates.Starts is not null && bates.Starts.Count > rollingIndex
                    ? bates.Starts[rollingIndex]
                    : bates.Start;
            }
            else
            {
                start = bates.Starts is not null && bates.Starts.Count > rollingIndex
                    ? bates.Starts[rollingIndex]
                    : bates.Start + (rollingIndex * request.Output.FileCount * bates.Increment);
            }

            string prefix = bates.Prefixes is not null && bates.Prefixes.Count > rollingIndex
                ? bates.Prefixes[rollingIndex]
                : bates.Prefix;

            return bates with
            {
                Prefix = prefix,
                Start = start,
                Prefixes = null,
                Starts = null,
            };
        }

        return bates;
    }

    public static List<string> ParseDatLine(string line, char colDelim, char quoteDelim)
        => DatLineParser.Parse(line, colDelim, quoteDelim);
}
