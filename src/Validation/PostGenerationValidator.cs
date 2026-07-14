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

        if (context.ArchiveFilePath is not null)
        {
            ValidateArchive(context, result);
        }
        else
        {
            ValidateDiskFiles(context, result);
        }

        return result;
    }

    private static void ValidateDiskFiles(ValidationContext context, ValidationResult result)
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
                bates: context.Request.Bates,
                encoding: EncodingHelper.GetEncodingOrDefault(context.Request.LoadFile.Encoding),
                columnDelimiter: context.Request.Delimiters.GetColumnChar(),
                quoteDelimiter: context.Request.Delimiters.GetQuoteChar());
            result.AddRange(vr.Findings);
        }
    }

    private static void ValidateArchive(ValidationContext context, ValidationResult result)
    {
        using var archive = ZipFile.OpenRead(context.ArchiveFilePath!);
        var entryPaths = context.ArchiveEntryPaths
            ?? archive.Entries.Select(e => e.FullName).ToArray();
        var runner = new ValidatorRunner();

        foreach (var (formatName, _) in context.LoadFiles)
        {
            if (formatName == "edrmxml")
                continue;

            var extension = GetExtensionForFormat(formatName);
            var baseName = Path.GetFileNameWithoutExtension(context.LoadFiles[formatName]);
            var fileName = baseName + extension;
            var entry = archive.GetEntry(fileName);
            if (entry is null)
            {
                result.Add(new ValidationFinding(
                    ValidationSeverity.Error,
                    "MissingLoadFile",
                    $"Generated load file '{fileName}' is missing from the Archive.",
                    context.ArchiveFilePath));
                continue;
            }

            using var reader = new StreamReader(
                entry.Open(),
                EncodingHelper.GetEncodingOrDefault(context.Request.LoadFile.Encoding),
                detectEncodingFromByteOrderMarks: true);
            var vr = runner.ValidateLoadFile(
                reader,
                entry.FullName,
                formatName,
                null,
                entryPaths,
                context.Request.Bates,
                context.Request.Delimiters.GetColumnChar(),
                context.Request.Delimiters.GetQuoteChar());
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

    private static string GetExtensionForFormat(string formatName) => formatName switch
    {
        "opt" => ".opt",
        "csv" => ".csv",
        "concordance" => ".dat",
        _ => ".dat",
    };
}
