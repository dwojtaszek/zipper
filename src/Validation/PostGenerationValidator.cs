using System.IO.Compression;

namespace Zipper.Validation;

public sealed class PostGenerationValidator
{
    public ValidationResult Validate(ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var result = new ValidationResult();

        if (context.IsChaosMode)
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

        if (entryPaths is not null)
        {
            ValidateDiskFiles(context, result, entryPaths);
        }

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
        foreach (var finding in report.Findings)
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

        // EOL validation for production set load files (ProductionSetPostValidator doesn't check this)
        var eol = GetExpectedEol(context.Request);
        if (eol is not null)
        {
            var runner = new ValidatorRunner();
            foreach (var (formatName, filePath) in context.LoadFiles)
            {
                if (!File.Exists(filePath))
                    continue;
                var vr = runner.ValidateLoadFile(
                    filePath,
                    formatName,
                    null,
                    eol,
                    encoding: EncodingHelper.GetEncodingOrDefault(context.Request.LoadFile.Encoding),
                    columnDelimiter: context.Request.Delimiters.GetColumnChar(),
                    quoteDelimiter: context.Request.Delimiters.GetQuoteChar());
                result.AddRange(vr.Findings);
            }
        }
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
