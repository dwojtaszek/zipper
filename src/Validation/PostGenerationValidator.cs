using System.IO.Compression;

namespace Zipper.Validation;

public sealed class PostGenerationValidator
{
    public ValidationResult Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = new ValidationResult();

        if (context.IsChaosMode && context.ProductionSetPath is null)
            return result;

        if (context.ProductionSetPath is not null)
        {
            ValidateProductionSet(context, result);
            return result;
        }

        // Always extract entry paths from archive for path reconciliation,
        // even when load files are on disk (IncludeLoadFile=false).
        IReadOnlyList<string>? entryPaths = context.ArchiveEntryPaths;
        if (entryPaths is null && context.ArchiveFilePath is not null && File.Exists(context.ArchiveFilePath))
        {
            using var archive = ZipFile.OpenRead(context.ArchiveFilePath);
            entryPaths = archive.Entries.Select(e => e.FullName).ToArray();
        }

        ValidateDiskFiles(context, result, entryPaths);

        return result;
    }

    private static void ValidateDiskFiles(ValidationContext context, ValidationResult result, IReadOnlyList<string>? entryPaths = null)
    {
        var runner = new ValidatorRunner();
        foreach (var (formatName, filePath) in context.LoadFiles)
        {
            if (formatName == "edrmxml" || !File.Exists(filePath))
                continue;

            var eol = context.SkipEolValidation ? null : GetExpectedEol(context.Request);
            var vr = runner.ValidateLoadFile(
                filePath,
                formatName,
                null,
                eol,
                entryPaths,
                bates: context.Request.Bates,
                encoding: EncodingHelper.GetEncodingOrDefault(context.Request.LoadFile.Encoding),
                columnDelimiter: context.Request.Delimiters.GetColumnChar(),
                quoteDelimiter: context.Request.Delimiters.GetQuoteChar());
            result.AddRange(vr.Findings);
        }
    }

    private static void ValidateProductionSet(ValidationContext context, ValidationResult result)
    {
        var report = ProductionSetPostValidator.Validate(context.ProductionSetPath!, context.Request);
        context.ProductionSetReport = report;
        var injectedImageLines = context.IsChaosMode ? ReadInjectedImageLines(context.ProductionSetPath!) : [];
        foreach (var finding in report.Findings)
        {
            // Only audited OPT path divergence or synthesized continuation images may be absent.
            bool injectedImagePath = finding.Code == "PathExistence"
                && finding.Path == "DATA/loadfile.opt"
                && finding.Line is { } line && injectedImageLines.Contains(line)
                && finding.Message.StartsWith("Referenced image file", StringComparison.Ordinal);
            if (!context.IsChaosMode || (!injectedImagePath && finding.Code is
                "PathExistence" or "MetadataAzureConstraint" or "ManifestSyntax" or "InvalidValue"))
            {
                result.Add(new ValidationFinding(
                    string.Equals(finding.Severity, "error", StringComparison.OrdinalIgnoreCase)
                        ? ValidationSeverity.Error
                        : ValidationSeverity.Warning,
                    finding.Code,
                    finding.Message,
                    finding.Path,
                    finding.Line));
            }
        }

        // EOL validation for production set load files (ProductionSetPostValidator doesn't check this)
        var eol = context.SkipEolValidation ? null : GetExpectedEol(context.Request);
        if (eol is not null)
        {
            var runner = new ValidatorRunner();
            var encoding = EncodingHelper.GetEncodingOrDefault(context.Request.LoadFile.Encoding);
            foreach (var (_, filePath) in context.LoadFiles)
            {
                if (!File.Exists(filePath))
                    continue;
                var vr = runner.ValidateLineEndings(filePath, eol, encoding);
                result.AddRange(vr.Findings);
            }
        }
    }

    private static HashSet<long> ReadInjectedImageLines(string productionPath)
    {
        var path = Path.Combine(productionPath, "DATA", "loadfile.opt_properties.json");
        var lines = new HashSet<long>();
        if (!File.Exists(path))
            return lines;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("chaosMode", out var chaos)
                && chaos.TryGetProperty("injectedAnomalies", out var anomalies))
            {
                foreach (var anomaly in anomalies.EnumerateArray())
                {
                    if (anomaly.GetProperty("file").GetString() != "loadfile.opt"
                        || !long.TryParse(anomaly.GetProperty("lineNumber").GetString(),
                            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long line))
                        continue;
                    var type = anomaly.GetProperty("errorType").GetString();
                    if (type == "desync-path")
                        lines.Add(line);
                    else if (type == "desync-pagecount"
                        && anomaly.GetProperty("description").GetString()?.StartsWith("Duplicated OPT page row", StringComparison.Ordinal) == true)
                        lines.Add(line + 1);
                }
            }
        }
        // Malformed audit data must never exempt missing artifacts.
        catch (System.Text.Json.JsonException) { lines.Clear(); }
        catch (InvalidOperationException) { lines.Clear(); }
        catch (KeyNotFoundException) { lines.Clear(); }
        return lines;
    }

    private static string? GetExpectedEol(FileGenerationRequest request)
    {
        return request.Delimiters.EndOfLine?.ToUpperInvariant() switch
        {
            "CRLF" => "\r\n",
            "LF" => "\n",
            "CR" => "\r",
            _ => null
        };
    }

}
