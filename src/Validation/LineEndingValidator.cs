namespace Zipper.Validation;

public sealed class LineEndingValidator
{
    public void Validate(string content, string expectedEol, string filePath, ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrEmpty(content))
            return;

        var span = content.AsSpan();
        bool consistent = true;
        int lineNum = 1;
        int i = 0;

        while (i < span.Length)
        {
            if (span[i] == '\r')
            {
                if (expectedEol == "\n")
                {
                    consistent = false;
                    break;
                }

                if (i + 1 < span.Length && span[i + 1] == '\n')
                {
                    i += 2;
                }
                else
                {
                    consistent = false;
                    break;
                }
                lineNum++;
            }
            else if (span[i] == '\n')
            {
                if (expectedEol == "\r\n")
                {
                    consistent = false;
                    break;
                }

                i++;
                lineNum++;
            }
            else
            {
                i++;
            }
        }

        if (!consistent)
        {
            result.Add(new ValidationFinding(
                ValidationSeverity.Error,
                "LineEnding",
                $"Inconsistent line ending on or before line {lineNum}. Expected '{EolDisplay(expectedEol)}'.",
                filePath));
        }
    }

    private static string EolDisplay(string eol) => eol switch
    {
        "\n" => "LF",
        "\r\n" => "CRLF",
        "\r" => "CR",
        _ => $"0x{string.Join("", eol.Select(c => ((int)c).ToString("X2")))}",
    };
}
