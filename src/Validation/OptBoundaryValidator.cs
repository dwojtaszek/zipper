namespace Zipper.Validation;

public sealed class OptBoundaryValidator
{
    private const int ExpectedColumns = 7;

    public void Validate(string content, string filePath, ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(result);
        var reader = new StringReader(content);
        string? line;
        int lineNum = 0;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNum++;
            if (line.Length == 0) continue;
            int count = line.Split(',').Length;
            if (count != ExpectedColumns)
            {
                result.Add(new ValidationFinding(
                    ValidationSeverity.Error,
                    "OptBoundary",
                    $"OPT line {lineNum} has {count} columns, expected {ExpectedColumns}",
                    filePath, lineNum));
            }
        }
    }
}
