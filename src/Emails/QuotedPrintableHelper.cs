using System.Text;

namespace Zipper.Emails;

/// <summary>
/// Encodes body text into MIME Quoted-Printable transfer encoding per RFC 2045.
/// </summary>
internal static class QuotedPrintableHelper
{
    private const int MaxLineLength = 76;

    /// <summary>
    /// Encodes a UTF-8 text into Quoted-Printable representation with soft line breaks.
    /// </summary>
    public static string Encode(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length * 2);
        int currentLineLength = 0;

        // Process line by line to preserve hard CRLF / LF line breaks
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];
            var lineBytes = Encoding.UTF8.GetBytes(line);

            for (int i = 0; i < lineBytes.Length; i++)
            {
                byte b = lineBytes[i];
                bool isLastByte = (i == lineBytes.Length - 1);

                string token;
                // Safe ASCII: printable characters except '='
                if (b is >= 33 and <= 126 and not 61)
                {
                    token = ((char)b).ToString();
                }
                else if (b is 32 or 9) // Space or Tab
                {
                    if (isLastByte)
                    {
                        // Trailing whitespace at end of line must be encoded
                        token = b == 32 ? "=20" : "=09";
                    }
                    else
                    {
                        token = ((char)b).ToString();
                    }
                }
                else
                {
                    token = $"={b:X2}";
                }

                // If adding token would exceed MaxLineLength (allowing 1 char for soft break '='), insert soft break
                if (currentLineLength + token.Length > MaxLineLength - 1)
                {
                    sb.Append("=\r\n");
                    currentLineLength = 0;
                }

                sb.Append(token);
                currentLineLength += token.Length;
            }

            if (lineIndex < lines.Length - 1)
            {
                sb.Append("\r\n");
                currentLineLength = 0;
            }
        }

        return sb.ToString();
    }
}
