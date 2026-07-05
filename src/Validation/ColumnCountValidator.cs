namespace Zipper.Validation;

public sealed class ColumnCountValidator
{
    public void ValidateCsv(string content, int expectedColumns, string filePath, ValidationResult result)
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
            int count = CountCsvColumns(line.AsSpan());
            if (count != expectedColumns)
            {
                result.Add(new ValidationFinding(
                    ValidationSeverity.Error,
                    "ColumnCount",
                    $"Expected {expectedColumns} columns, got {count} on line {lineNum}",
                    filePath, lineNum));
            }
        }
    }

    public void ValidateDat(string content, int expectedColumns, char columnDelimiter, string filePath, ValidationResult result)
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
            int count = CountDatColumns(line.AsSpan(), columnDelimiter);
            if (count != expectedColumns)
            {
                result.Add(new ValidationFinding(
                    ValidationSeverity.Error,
                    "ColumnCount",
                    $"Expected {expectedColumns} columns, got {count} on line {lineNum}",
                    filePath, lineNum));
            }
        }
    }

    public void ValidateConcordance(string content, char columnDelimiter, string filePath, ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(result);
        var reader = new StringReader(content);
        var firstLine = reader.ReadLine();
        if (firstLine is null) return;
        int firstLineCount = CountConcordanceColumns(firstLine.AsSpan(), columnDelimiter);

        string? line;
        int lineNum = 1;
        while ((line = reader.ReadLine()) is not null)
        {
            lineNum++;
            if (line.Length == 0) continue;
            int count = CountConcordanceColumns(line.AsSpan(), columnDelimiter);
            if (count != firstLineCount)
            {
                result.Add(new ValidationFinding(
                    ValidationSeverity.Error,
                    "ColumnCount",
                    $"Expected {firstLineCount} columns, got {count} on line {lineNum}",
                    filePath, lineNum));
            }
        }
    }

    internal static int CountCsvColumns(ReadOnlySpan<char> line)
    {
        int count = 1;
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                count++;
            }
        }
        return count;
    }

    internal static int CountDatColumns(ReadOnlySpan<char> line, char columnDelimiter)
    {
        int count = 1;
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\xfe' || c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == columnDelimiter && !inQuotes)
            {
                count++;
            }
        }
        return count;
    }

    internal static int CountConcordanceColumns(ReadOnlySpan<char> line, char columnDelimiter)
    {
        int count = 1;
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '\xfe')
            {
                inQuotes = !inQuotes;
            }
            else if (c == columnDelimiter && !inQuotes)
            {
                count++;
            }
        }
        return count;
    }
}
