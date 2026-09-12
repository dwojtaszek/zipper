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
        var report = new ProductionSetValidationReport();
        var findings = report.Findings;

        var datRelPath = "DATA/loadfile.dat";
        var optRelPath = "DATA/loadfile.opt";
        var datPath = Path.Combine(productionPath, "DATA", "loadfile.dat");
        var optPath = Path.Combine(productionPath, "DATA", "loadfile.opt");

        int datRowsChecked = 0;
        int optRowsChecked = 0;

        int checkedNativesCount = 0;
        int checkedTextsCount = 0;
        int checkedImagesCount = 0;

        var seenDocIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenBatesNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenOptBates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var parentBatesList = new List<string>();

        // 1. Verify DAT load file existence and contents
        if (!File.Exists(datPath))
        {
            findings.Add(new ValidationReportFinding
            {
                Code = "PathExistence",
                Severity = "error",
                Path = datRelPath,
                Message = "DAT load file does not exist."
            });
        }
        else
        {
            var encoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile?.Encoding);
            var datLines = File.ReadLines(datPath, encoding);
            var skipNativePathValidation = string.Equals(request.Production.WithheldNativePolicy, "replace-with-placeholder", StringComparison.OrdinalIgnoreCase);

            using (var enumerator = datLines.GetEnumerator())
            {
                if (enumerator.MoveNext())
                {
                    datRowsChecked++;
                    var headerLine = enumerator.Current;
                    var colDelimChar = request.Delimiters?.GetColumnChar() ?? '\x14';
                    var quoteDelimChar = request.Delimiters?.GetQuoteChar() ?? '\xfe';

                    var headers = ParseDatLine(headerLine, colDelimChar, quoteDelimChar);
                    int docIdIdx = headers.FindIndex(h => string.Equals(h, "DOCID", StringComparison.OrdinalIgnoreCase));
                    int batesIdx = headers.FindIndex(h => string.Equals(h, "BATES_NUMBER", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "BATES", StringComparison.OrdinalIgnoreCase));
                    int nativeIdx = headers.FindIndex(h => string.Equals(h, "NATIVE_PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "PATH", StringComparison.OrdinalIgnoreCase));
                    int textIdx = headers.FindIndex(h => string.Equals(h, "TEXT_PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "TEXT", StringComparison.OrdinalIgnoreCase));
                    int imageIdx = headers.FindIndex(h => string.Equals(h, "IMAGE_PATH", StringComparison.OrdinalIgnoreCase) || string.Equals(h, "IMAGE", StringComparison.OrdinalIgnoreCase));
                    int parentIdIdx = headers.FindIndex(h => string.Equals(h, "PARENTDOCID", StringComparison.OrdinalIgnoreCase));
                    int redactedImageIdx = headers.FindIndex(h => string.Equals(h, "REDACTED_IMAGE_PATH", StringComparison.OrdinalIgnoreCase));
                    int redactedTextIdx = headers.FindIndex(h => string.Equals(h, "REDACTED_TEXT_PATH", StringComparison.OrdinalIgnoreCase));
                    int nativeWithheldIdx = headers.FindIndex(h => string.Equals(h, "NATIVE_WITHHELD", StringComparison.OrdinalIgnoreCase));

                    int i = 0;
                    while (enumerator.MoveNext())
                    {
                        i++;
                        datRowsChecked++;
                        var line = enumerator.Current;
                        if (string.IsNullOrEmpty(line))
                            continue;

                        var fields = ParseDatLine(line, colDelimChar, quoteDelimChar);
                        if (fields.Count != headers.Count)
                        {
                            findings.Add(new ValidationReportFinding
                            {
                                Code = "ColumnCount",
                                Severity = "error",
                                Path = datRelPath,
                                Line = i + 1,
                                Message = $"Expected {headers.Count} columns, got {fields.Count} on line {i + 1}"
                            });
                        }

                        // DOCID uniqueness
                        if (docIdIdx >= 0 && docIdIdx < fields.Count)
                        {
                            var docId = fields[docIdIdx];
                            if (!string.IsNullOrEmpty(docId))
                            {
                                if (!seenDocIds.Add(docId))
                                {
                                    findings.Add(new ValidationReportFinding
                                    {
                                        Code = "UniqueId",
                                        Severity = "error",
                                        Path = datRelPath,
                                        Line = i + 1,
                                        Message = $"Duplicate DOCID: '{docId}'"
                                    });
                                }
                            }
                        }

                        // BATES_NUMBER uniqueness and consistency
                        string batesVal = string.Empty;
                        if (batesIdx >= 0 && batesIdx < fields.Count)
                        {
                            batesVal = fields[batesIdx];
                            if (!string.IsNullOrEmpty(batesVal))
                            {
                                if (!seenBatesNumbers.Add(batesVal))
                                {
                                    findings.Add(new ValidationReportFinding
                                    {
                                        Code = "UniqueId",
                                        Severity = "error",
                                        Path = datRelPath,
                                        Line = i + 1,
                                        Message = $"Duplicate Bates Number: '{batesVal}'"
                                    });
                                }
                            }
                        }

                        // Parent/Child Bates range consistency check
                        if (!string.IsNullOrEmpty(batesVal))
                        {
                            bool isParent = parentIdIdx == -1 || parentIdIdx >= fields.Count || string.IsNullOrEmpty(fields[parentIdIdx]);
                            if (isParent)
                            {
                                parentBatesList.Add(batesVal);
                            }
                            else
                            {
                                var parentDocId = fields[parentIdIdx];
                                if (!batesVal.StartsWith(parentDocId, StringComparison.Ordinal))
                                {
                                    findings.Add(new ValidationReportFinding
                                    {
                                        Code = "BatesConsistency",
                                        Severity = "error",
                                        Path = datRelPath,
                                        Line = i + 1,
                                        Message = $"Child Bates number '{batesVal}' does not start with parent Doc ID '{parentDocId}'"
                                    });
                                }
                            }
                        }

                        // Native path existence
                        if (nativeIdx >= 0 && nativeIdx < fields.Count && !skipNativePathValidation)
                        {
                            var nativePath = fields[nativeIdx];
                            if (!string.IsNullOrEmpty(nativePath))
                            {
                                checkedNativesCount++;
                                var fullNativePath = Path.Combine(productionPath, nativePath.Replace('\\', '/'));
                                if (!File.Exists(fullNativePath))
                                {
                                    findings.Add(new ValidationReportFinding
                                    {
                                        Code = "PathExistence",
                                        Severity = "error",
                                        Path = datRelPath,
                                        Line = i + 1,
                                        Message = $"Referenced native file '{nativePath}' does not exist."
                                    });
                                }
                            }
                        }

                        // Text path existence
                        if (textIdx >= 0 && textIdx < fields.Count)
                        {
                            var textPath = fields[textIdx];
                            if (!string.IsNullOrEmpty(textPath))
                            {
                                checkedTextsCount++;
                                var fullTextPath = Path.Combine(productionPath, textPath.Replace('\\', '/'));
                                if (!File.Exists(fullTextPath))
                                {
                                    findings.Add(new ValidationReportFinding
                                    {
                                        Code = "PathExistence",
                                        Severity = "error",
                                        Path = datRelPath,
                                        Line = i + 1,
                                        Message = $"Referenced text file '{textPath}' does not exist."
                                    });
                                }
                            }
                        }

                        // Image path existence
                        if (imageIdx >= 0 && imageIdx < fields.Count)
                        {
                            var imagePath = fields[imageIdx];
                            if (!string.IsNullOrEmpty(imagePath))
                            {
                                checkedImagesCount++;
                                var fullImagePath = Path.Combine(productionPath, imagePath.Replace('\\', '/'));
                                if (!File.Exists(fullImagePath))
                                {
                                    var dir = Path.GetDirectoryName(fullImagePath) ?? string.Empty;
                                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(fullImagePath);
                                    var ext = Path.GetExtension(fullImagePath);
                                    var pageOnePath = Path.Combine(dir, $"{fileNameWithoutExt}_001{ext}");

                                    if (!File.Exists(pageOnePath))
                                    {
                                        findings.Add(new ValidationReportFinding
                                        {
                                            Code = "PathExistence",
                                            Severity = "error",
                                            Path = datRelPath,
                                            Line = i + 1,
                                            Message = $"Referenced image file '{imagePath}' does not exist."
                                        });
                                    }
                                }
                            }
                        }

                        // Redacted image path existence
                        if (redactedImageIdx >= 0 && redactedImageIdx < fields.Count)
                        {
                            var redactedImagePath = fields[redactedImageIdx];
                            if (!string.IsNullOrEmpty(redactedImagePath))
                            {
                                var fullRedactedImagePath = Path.Combine(productionPath, redactedImagePath.Replace('\\', '/'));
                                if (!File.Exists(fullRedactedImagePath))
                                {
                                    findings.Add(new ValidationReportFinding
                                    {
                                        Code = "PathExistence",
                                        Severity = "error",
                                        Path = datRelPath,
                                        Line = i + 1,
                                        Message = $"Referenced redacted image file '{redactedImagePath}' does not exist."
                                    });
                                }
                            }
                        }

                        // Redacted text path existence
                        if (redactedTextIdx >= 0 && redactedTextIdx < fields.Count)
                        {
                            var redactedTextPath = fields[redactedTextIdx];
                            if (!string.IsNullOrEmpty(redactedTextPath))
                            {
                                var fullRedactedTextPath = Path.Combine(productionPath, redactedTextPath.Replace('\\', '/'));
                                if (!File.Exists(fullRedactedTextPath))
                                {
                                    findings.Add(new ValidationReportFinding
                                    {
                                        Code = "PathExistence",
                                        Severity = "error",
                                        Path = datRelPath,
                                        Line = i + 1,
                                        Message = $"Referenced redacted text file '{redactedTextPath}' does not exist."
                                    });
                                }
                            }
                        }

                        // NATIVE_WITHHELD value validation
                        if (nativeWithheldIdx >= 0 && nativeWithheldIdx < fields.Count)
                        {
                            var withheldVal = fields[nativeWithheldIdx];
                            if (string.IsNullOrEmpty(withheldVal) ||
                                (!string.Equals(withheldVal, "YES", StringComparison.OrdinalIgnoreCase) &&
                                 !string.Equals(withheldVal, "NO", StringComparison.OrdinalIgnoreCase)))
                            {
                                findings.Add(new ValidationReportFinding
                                {
                                    Code = "InvalidValue",
                                    Severity = "error",
                                    Path = datRelPath,
                                    Line = i + 1,
                                    Message = $"NATIVE_WITHHELD must be 'YES' or 'NO', got '{withheldVal}'"
                                });
                            }
                        }
                    }
                }
            }
        }

        // Manifest Bates range consistency
        var manifestPath = Path.Combine(productionPath, "_manifest.json");
        string? mStart = null;
        string? mEnd = null;
        int? rollingSeqNum = null;
        if (File.Exists(manifestPath))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (doc.RootElement.TryGetProperty("batesNumberStart", out var startElem))
                {
                    mStart = startElem.GetString();
                }
                if (doc.RootElement.TryGetProperty("batesNumberEnd", out var endElem))
                {
                    mEnd = endElem.GetString();
                }
                if (doc.RootElement.TryGetProperty("rollingSequenceNumber", out var seqElem) &&
                    seqElem.TryGetInt32(out var sNum))
                {
                    rollingSeqNum = sNum;
                }

                if (parentBatesList.Count > 0)
                {
                    if (!string.IsNullOrEmpty(mStart) && !string.Equals(parentBatesList[0], mStart, StringComparison.Ordinal))
                    {
                        findings.Add(new ValidationReportFinding
                        {
                            Code = "BatesConsistency",
                            Severity = "error",
                            Path = datRelPath,
                            Message = $"DAT first Bates number '{parentBatesList[0]}' does not match manifest start '{mStart}'"
                        });
                    }
                    if (!string.IsNullOrEmpty(mEnd) && !string.Equals(parentBatesList[^1], mEnd, StringComparison.Ordinal))
                    {
                        findings.Add(new ValidationReportFinding
                        {
                            Code = "BatesConsistency",
                            Severity = "error",
                            Path = datRelPath,
                            Message = $"DAT last Bates number '{parentBatesList[^1]}' does not match manifest end '{mEnd}'"
                        });
                    }
                }
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or System.IO.IOException)
            {
                findings.Add(new ValidationReportFinding
                {
                    Code = "ManifestSyntax",
                    Severity = "error",
                    Path = "_manifest.json",
                    Message = $"Manifest file '_manifest.json' could not be read or parsed: {ex.Message}"
                });
            }
        }

        // Bates range continuity/sequence check
        if (request.Bates != null && parentBatesList.Count > 0)
        {
            var effectiveBates = ResolveEffectiveBates(request, rollingSeqNum, mStart);
            var batesSequence = BatesSequence.FromConfig(effectiveBates);
            for (int i = 0; i < parentBatesList.Count; i++)
            {
                var expectedBates = batesSequence.Format(i).ToString();
                var actualBates = parentBatesList[i];
                if (!string.Equals(actualBates, expectedBates, StringComparison.Ordinal))
                {
                    findings.Add(new ValidationReportFinding
                    {
                        Code = "BatesConsistency",
                        Severity = "error",
                        Path = datRelPath,
                        Message = $"Bates range inconsistency: expected '{expectedBates}' at parent index {i}, but got '{actualBates}'"
                    });
                }
            }
        }

        // 2. Verify OPT load file existence and contents
        if (!File.Exists(optPath))
        {
            findings.Add(new ValidationReportFinding
            {
                Code = "PathExistence",
                Severity = "error",
                Path = optRelPath,
                Message = "OPT load file does not exist."
            });
        }
        else
        {
            var optEncoding = EncodingHelper.GetEncodingOrDefault(request.LoadFile?.Encoding);
            int i = -1;
            string? firstOptBates = null;
            string? lastOptBates = null;
            foreach (var line in File.ReadLines(optPath, optEncoding))
            {
                i++;
                optRowsChecked++;
                if (string.IsNullOrEmpty(line))
                    continue;

                var columns = line.Split(',');
                if (columns.Length != 7)
                {
                    findings.Add(new ValidationReportFinding
                    {
                        Code = "OptBoundary",
                        Severity = "error",
                        Path = optRelPath,
                        Line = i + 1,
                        Message = $"OPT line {i + 1} has {columns.Length} columns, expected 7"
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
                        if (!seenOptBates.Add(optBates))
                        {
                            findings.Add(new ValidationReportFinding
                            {
                                Code = "UniqueId",
                                Severity = "error",
                                Path = optRelPath,
                                Line = i + 1,
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
                        var fullImagePath = Path.Combine(productionPath, imagePath.Replace('\\', '/'));
                        if (!File.Exists(fullImagePath))
                        {
                            findings.Add(new ValidationReportFinding
                            {
                                Code = "PathExistence",
                                Severity = "error",
                                Path = optRelPath,
                                Line = i + 1,
                                Message = $"Referenced image file '{imagePath}' does not exist."
                            });
                        }
                    }
                }
            }

            // OPT Bates boundaries consistency with manifest
            if (!string.IsNullOrEmpty(firstOptBates) && !string.IsNullOrEmpty(mStart) && !firstOptBates.StartsWith(mStart, StringComparison.Ordinal))
            {
                findings.Add(new ValidationReportFinding
                {
                    Code = "BatesConsistency",
                    Severity = "error",
                    Path = optRelPath,
                    Message = $"OPT first Bates number '{firstOptBates}' does not match manifest start '{mStart}'"
                });
            }
            if (!string.IsNullOrEmpty(lastOptBates) && !string.IsNullOrEmpty(mEnd) && !lastOptBates.StartsWith(mEnd, StringComparison.Ordinal))
            {
                findings.Add(new ValidationReportFinding
                {
                    Code = "BatesConsistency",
                    Severity = "error",
                    Path = optRelPath,
                    Message = $"OPT last Bates number '{lastOptBates}' does not match manifest end '{mEnd}'"
                });
            }
        }

        // Set counts and status
        report.ErrorCount = findings.Count(f => string.Equals(f.Severity, "error", StringComparison.OrdinalIgnoreCase));
        report.WarningCount = findings.Count(f => string.Equals(f.Severity, "warning", StringComparison.OrdinalIgnoreCase));
        report.Status = report.ErrorCount > 0 ? "failed" : "passed";

        report.CheckedFileCounts = new Dictionary<string, int>
        {
            ["dat"] = File.Exists(datPath) ? 1 : 0,
            ["opt"] = File.Exists(optPath) ? 1 : 0,
            ["native"] = checkedNativesCount,
            ["text"] = checkedTextsCount,
            ["image"] = checkedImagesCount
        };

        report.CheckedLoadFileRowCounts = new Dictionary<string, int>
        {
            ["dat"] = datRowsChecked,
            ["opt"] = optRowsChecked
        };

        return report;
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
