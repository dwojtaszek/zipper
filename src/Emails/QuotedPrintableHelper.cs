using System.Text;

namespace Zipper.Emails;

/// <summary>
/// Encodes body text into MIME Quoted-Printable transfer encoding per RFC 2045.
/// </summary>
internal static class QuotedPrintableHelper
{
    private const int MaxLineLength = 76;
    private const string HexDigits = "0123456789ABCDEF";

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

                char tokenCharacter = default;
                bool encodeByte;
                // Safe ASCII: printable characters except '='
                if (b is >= 33 and <= 126 and not 61)
                {
                    tokenCharacter = (char)b;
                    encodeByte = false;
                }
                else if (b is 32 or 9) // Space or Tab
                {
                    if (isLastByte || currentLineLength + 1 >= MaxLineLength - 1)
                    {
                        // Trailing whitespace at end of line or right before a soft break must be encoded per RFC 2045
                        encodeByte = true;
                    }
                    else
                    {
                        tokenCharacter = (char)b;
                        encodeByte = false;
                    }
                }
                else
                {
                    encodeByte = true;
                }

                int tokenLength = encodeByte ? 3 : 1;
                // If adding token would exceed MaxLineLength (allowing 1 char for soft break '='), insert soft break
                if (currentLineLength + tokenLength > MaxLineLength - 1)
                {
                    sb.Append("=\r\n");
                    currentLineLength = 0;
                }

                if (encodeByte)
                {
                    sb.Append('=');
                    sb.Append(HexDigits[b >> 4]);
                    sb.Append(HexDigits[b & 0x0F]);
                }
                else
                {
                    sb.Append(tokenCharacter);
                }

                currentLineLength += tokenLength;
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
