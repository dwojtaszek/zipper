using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Zipper.Emails;

/// <summary>
/// Implements RFC 2047 MIME encoded-word encoding and decoding for email headers,
/// with RFC 5322 header folding and opt-in deliberately non-RFC variants.
/// </summary>
internal static class Rfc2047Helper
{
    private static readonly TimeSpan DefaultRegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex EncodedWordRegex = new(
        @"=\?([^?]+)\?([BbQq])\?([^?]*)\?=",
        RegexOptions.Compiled,
        DefaultRegexTimeout);

    private static readonly Regex AdjacentEncodedWordsWhitespaceRegex = new(
        @"(\=\?[^?]+\?[BbQq]\?[^?]*\?\=)\s+(\=\?[^?]+\?[BbQq]\?[^?]*\?\=)",
        RegexOptions.Compiled,
        DefaultRegexTimeout);

    /// <summary>
    /// Checks if a string contains non-ASCII characters.
    /// </summary>
    public static bool ContainsNonAscii(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        foreach (char c in text)
        {
            if (c > 127 || (c < 32 && c != '\t'))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Encodes a header string using RFC 2047 encoded-word format if it contains non-ASCII characters,
    /// or folds it if it exceeds standard length.
    /// </summary>
    public static string EncodeSubject(
        string subject,
        bool fold = true,
        bool nonRfc = false,
        char preferredEncoding = 'B')
    {
        if (string.IsNullOrEmpty(subject))
        {
            return subject;
        }

        if (!ContainsNonAscii(subject))
        {
            return fold && subject.Length > 75
                ? FoldHeaderLine("Subject: " + subject)["Subject: ".Length..]
                : subject;
        }

        char enc = char.ToUpperInvariant(preferredEncoding) == 'Q' ? 'Q' : 'B';

        Span<byte> runeBytes = stackalloc byte[4];
        var words = new List<string>();
        if (enc == 'B')
        {
            // Chunk UTF-8 runes into <= 45 bytes per encoded-word so the result <= 75 chars
            var currentBytes = new List<byte>(45);
            foreach (var rune in subject.EnumerateRunes())
            {
                int runeByteCount = rune.EncodeToUtf8(runeBytes);
                if (currentBytes.Count + runeByteCount > 45 && currentBytes.Count > 0)
                {
                    var b64 = Convert.ToBase64String(currentBytes.ToArray());
                    words.Add($"=?utf-8?B?{b64}?=");
                    currentBytes.Clear();
                }

                for (int i = 0; i < runeByteCount; i++)
                {
                    currentBytes.Add(runeBytes[i]);
                }
            }

            if (currentBytes.Count > 0)
            {
                var b64 = Convert.ToBase64String(currentBytes.ToArray());
                words.Add($"=?utf-8?B?{b64}?=");
            }
        }
        else
        {
            // Quoted-Printable chunking
            var currentQp = new StringBuilder(60);
            foreach (var rune in subject.EnumerateRunes())
            {
                string encodedChar;
                if (rune.Value == 32)
                {
                    encodedChar = "_";
                }
                else if (rune.IsAscii && rune.Value is >= 33 and <= 126 and not 61 and not 63 and not 95)
                {
                    encodedChar = ((char)rune.Value).ToString();
                }
                else
                {
                    int runeByteCount = rune.EncodeToUtf8(runeBytes);
                    var sb = new StringBuilder(runeByteCount * 3);
                    for (int i = 0; i < runeByteCount; i++)
                    {
                        sb.Append('=');
                        sb.Append(runeBytes[i].ToString("X2", CultureInfo.InvariantCulture));
                    }

                    encodedChar = sb.ToString();
                }

                if (currentQp.Length + encodedChar.Length > 50 && currentQp.Length > 0)
                {
                    words.Add($"=?utf-8?Q?{currentQp}?=");
                    currentQp.Clear();
                }

                currentQp.Append(encodedChar);
            }

            if (currentQp.Length > 0)
            {
                words.Add($"=?utf-8?Q?{currentQp}?=");
            }
        }

        if (nonRfc && words.Count > 1)
        {
            // Deliberately non-RFC variant: omitted linear whitespace between adjacent encoded words
            return string.Join(string.Empty, words);
        }

        if (fold && words.Count > 1)
        {
            // Folded across lines with CRLF + space
            return string.Join("\r\n ", words);
        }

        return string.Join(" ", words);
    }

    /// <summary>
    /// Folds a long header line at word boundaries to keep lines <= 76 characters per RFC 5322.
    /// </summary>
    public static string FoldHeaderLine(string headerLine, int maxLineLength = 76)
    {
        if (headerLine.Length <= maxLineLength)
        {
            return headerLine;
        }

        var sb = new StringBuilder(headerLine.Length + 16);
        int lineStart = 0;

        while (lineStart < headerLine.Length)
        {
            int remaining = headerLine.Length - lineStart;
            if (remaining <= maxLineLength)
            {
                sb.Append(headerLine.AsSpan(lineStart));
                break;
            }

            // Find last space before maxLineLength
            int searchEnd = lineStart + maxLineLength;
            int lastSpace = headerLine.LastIndexOf(' ', searchEnd, maxLineLength);

            if (lastSpace <= lineStart)
            {
                // No space in window, find first space after searchEnd
                lastSpace = headerLine.IndexOf(' ', searchEnd);
                if (lastSpace < 0)
                {
                    // No space at all, emit remainder
                    sb.Append(headerLine.AsSpan(lineStart));
                    break;
                }
            }

            sb.Append(headerLine.AsSpan(lineStart, lastSpace - lineStart));
            sb.Append("\r\n ");
            lineStart = lastSpace + 1;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Unfolds a folded header by removing CRLF + WSP sequences.
    /// </summary>
    public static string UnfoldHeader(string header)
    {
        if (string.IsNullOrEmpty(header))
        {
            return header;
        }

        return Regex.Replace(header, @"\r\n[ \t]+", " ", RegexOptions.None, DefaultRegexTimeout);
    }

    /// <summary>
    /// Decodes an RFC 2047 encoded header value back to plain Unicode text.
    /// </summary>
    public static string Decode(string headerValue)
    {
        if (string.IsNullOrEmpty(headerValue))
        {
            return headerValue;
        }

        // Unfold first
        var unfolded = UnfoldHeader(headerValue);

        // Per RFC 2047 Section 6.2, linear whitespace between adjacent encoded-words is deleted
        while (AdjacentEncodedWordsWhitespaceRegex.IsMatch(unfolded))
        {
            unfolded = AdjacentEncodedWordsWhitespaceRegex.Replace(unfolded, "$1$2");
        }

        return EncodedWordRegex.Replace(unfolded, match =>
        {
            try
            {
                string charsetName = match.Groups[1].Value;
                string encodingType = match.Groups[2].Value.ToUpperInvariant();
                string encodedText = match.Groups[3].Value;

                Encoding encoding;
                try
                {
                    encoding = Encoding.GetEncoding(charsetName);
                }
                catch
                {
                    encoding = Encoding.UTF8;
                }

                if (encodingType == "B")
                {
                    byte[] bytes = Convert.FromBase64String(encodedText);
                    return encoding.GetString(bytes);
                }
                else if (encodingType == "Q")
                {
                    return DecodeQuotedPrintableText(encodedText, encoding);
                }

                return match.Value;
            }
            catch
            {
                return match.Value;
            }
        });
    }

    private static string DecodeQuotedPrintableText(string qpText, Encoding encoding)
    {
        var bytes = new List<byte>(qpText.Length);
        int i = 0;
        while (i < qpText.Length)
        {
            char c = qpText[i];
            if (c == '_')
            {
                bytes.Add((byte)' ');
                i++;
            }
            else if (c == '=' && i + 2 < qpText.Length &&
                     Uri.IsHexDigit(qpText[i + 1]) && Uri.IsHexDigit(qpText[i + 2]))
            {
                byte b = byte.Parse(qpText.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                bytes.Add(b);
                i += 3;
            }
            else
            {
                bytes.Add((byte)c);
                i++;
            }
        }

        return encoding.GetString(bytes.ToArray());
    }
}
