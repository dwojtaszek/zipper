namespace Zipper.Validation;

public sealed class ValidatorRunner
{
    public ValidationResult ValidateLoadFile(string loadFilePath, string format, IReadOnlyList<string>? headerColumns, string? expectedEol)
    {
        ArgumentNullException.ThrowIfNull(loadFilePath);
        ArgumentNullException.ThrowIfNull(format);
        var result = new ValidationResult();
        var content = File.ReadAllText(loadFilePath);

        if (format.Equals("opt", StringComparison.OrdinalIgnoreCase))
        {
            new OptBoundaryValidator().Validate(content, loadFilePath, result);
        }

        var colValidator = new ColumnCountValidator();
        if (format.Equals("concordance", StringComparison.OrdinalIgnoreCase))
        {
            colValidator.ValidateConcordance(content, '\x14', loadFilePath, result);
        }
        else if (headerColumns is { Count: > 0 })
        {
            if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                colValidator.ValidateCsv(content, headerColumns.Count, loadFilePath, result);
            }
            else if (format.Equals("dat", StringComparison.OrdinalIgnoreCase))
            {
                colValidator.ValidateDat(content, headerColumns.Count, '\x14', loadFilePath, result);
            }
        }

        if (!string.IsNullOrEmpty(expectedEol))
        {
            new LineEndingValidator().Validate(content, expectedEol, loadFilePath, result);
        }

        return result;
    }
}
