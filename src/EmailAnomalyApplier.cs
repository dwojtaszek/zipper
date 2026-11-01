using System.Globalization;
using System.Text;

namespace Zipper;

/// <summary>
/// The mutated EML bytes and the audit entry describing the anomaly applied to them.
/// </summary>
internal readonly record struct EmailAnomalyResult(byte[] Content, ChaosAnomaly Anomaly);

/// <summary>
/// Injects MIME-level anomalies into rendered EML bytes: boundary disagreements, malformed RFC 2047
/// encoded-words, illegal header folding or bare CR/LF, and deep <c>message/rfc822</c> nesting.
/// </summary>
/// <remarks>
/// Mutations work on the raw bytes through ISO-8859-1, which maps every byte to exactly one character,
/// so attachment payloads and non-UTF-8 bytes pass through unchanged. Each call is pure: the output
/// depends only on the input bytes, the anomaly type, the work item index, and the random sequence.
/// </remarks>
internal static class EmailAnomalyApplier
{
    private const string Crlf = "\r\n";
    private const string MissingDelimitersBoundary = "----=_Part_MISSING_DELIMITERS";
    private const int RfC822NestingDepth = 25;

    private static readonly Encoding Lossless = Encoding.Latin1;

    /// <summary>
    /// Applies one email anomaly type to the EML bytes. Every call changes the bytes and returns one audit entry.
    /// </summary>
    /// <param name="emlBytes">The rendered EML bytes.</param>
    /// <param name="fileName">The EML file name recorded in the audit entry.</param>
    /// <param name="recordId">The record identifier recorded in the audit entry.</param>
    /// <param name="chaosType">A type from <see cref="ChaosAnomalyTypes.Email"/>.</param>
    /// <param name="workItemIndex">The 1-based work item index, used to keep nested boundaries unique.</param>
    /// <param name="random">The random sequence for this work item.</param>
    /// <returns>The mutated bytes and their audit entry.</returns>
    public static EmailAnomalyResult Apply(
        byte[] emlBytes,
        string fileName,
        string recordId,
        string chaosType,
        long workItemIndex,
        Random random)
    {
        return chaosType switch
        {
            "mime-boundary" => ApplyMimeBoundary(emlBytes, fileName, recordId, random),
            "encoded-word" => ApplyEncodedWord(emlBytes, fileName, recordId, random),
            "header-fold" => ApplyHeaderFold(emlBytes, fileName, recordId, random),
            "rf822-depth" => ApplyRfc822Depth(emlBytes, fileName, recordId, workItemIndex),
            _ => throw new ArgumentException($"Unknown email anomaly type '{chaosType}'.", nameof(chaosType)),
        };
    }

    /// <summary>
    /// Derives the random sequence for one work item from the run seed and the item index. The sequence
    /// does not depend on which thread generates the item or in what order items are generated.
    /// </summary>
    internal static Random CreateItemRandom(long seed, long workItemIndex)
    {
        ulong mixed = Mix(unchecked((ulong)seed) + (unchecked((ulong)workItemIndex) * 0x9E3779B97F4A7C15UL));

#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        return new Random(unchecked((int)(mixed >> 33)));
#pragma warning restore S2245
    }

    // SplitMix64 finalizer: a fixed bit mixer. Unlike HashCode, its output is stable across processes.
    private static ulong Mix(ulong value)
    {
        ulong z = value + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    private static EmailAnomalyResult ApplyMimeBoundary(byte[] emlBytes, string fileName, string recordId, Random random)
    {
        var message = EmlLines.Parse(emlBytes);
        int contentType = message.FindHeader("Content-Type");

        if (contentType >= 0 && TryFindBoundary(message, contentType, out int boundaryLine, out string boundary))
        {
            int closingLine = message.FindClosingDelimiter(boundary);
            if (closingLine >= 0 && random.Next(2) == 0)
            {
                // Drop the closing hyphens so the multipart container never closes.
                message.Lines[closingLine] = $"--{boundary}";
                return new EmailAnomalyResult(
                    message.ToBytes(),
                    Audit(
                        fileName,
                        recordId,
                        closingLine + 1,
                        "MIME-Boundary",
                        "mime-boundary",
                        "Final MIME multipart closing delimiter is missing its trailing hyphens, leaving the container unclosed.",
                        $"line {closingLine + 1}, MIME closing delimiter"));
            }

            // The Content-Type boundary no longer matches the delimiters that the body still uses.
            message.Lines[boundaryLine] = message.Lines[boundaryLine].Replace(boundary, boundary + "_MISMATCH", StringComparison.Ordinal);
            return new EmailAnomalyResult(
                message.ToBytes(),
                Audit(
                    fileName,
                    recordId,
                    boundaryLine + 1,
                    "Content-Type",
                    "mime-boundary",
                    "Content-Type boundary parameter differs from the actual delimiters in the body.",
                    $"line {boundaryLine + 1}, Content-Type header"));
        }

        // Single-part message: declare a multipart container whose delimiters the body does not contain.
        int declared = message.ReplaceHeader("Content-Type", $"Content-Type: multipart/mixed; boundary=\"{MissingDelimitersBoundary}\"");
        return new EmailAnomalyResult(
            message.ToBytes(),
            Audit(
                fileName,
                recordId,
                declared + 1,
                "Content-Type",
                "mime-boundary",
                $"Content-Type declares multipart boundary '{MissingDelimitersBoundary}' with no matching delimiters in the body.",
                $"line {declared + 1}, Content-Type header"));
    }

    private static EmailAnomalyResult ApplyEncodedWord(byte[] emlBytes, string fileName, string recordId, Random random)
    {
        var message = EmlLines.Parse(emlBytes);
        var (malformed, description) = random.Next(3) switch
        {
            0 => ("=?x-unknown-8859-99?B?dGVzdA==?=", "Subject uses an RFC 2047 encoded-word with an unknown charset."),
            1 => ("=?utf-8?B?abc===?=", "Subject uses an RFC 2047 encoded-word with invalid Base64 padding."),
            _ => ("=?utf-8?B?dGV zdA==?=", "Subject uses an RFC 2047 encoded-word with whitespace inside the encoded text."),
        };

        int line = message.ReplaceHeader("Subject", $"Subject: {malformed}");
        return new EmailAnomalyResult(
            message.ToBytes(),
            Audit(fileName, recordId, line + 1, "Subject", "encoded-word", description, $"line {line + 1}, Subject header"));
    }

    private static EmailAnomalyResult ApplyHeaderFold(byte[] emlBytes, string fileName, string recordId, Random random)
    {
        var message = EmlLines.Parse(emlBytes);
        string value = message.HeaderValue("Subject") ?? "Test Message";

        int variant = random.Next(3);
        int foldAt = FindNonWhitespaceBoundary(value);
        if (variant == 0 && foldAt < 0)
        {
            variant = 1;
        }

        string[] replacement;
        string description;
        switch (variant)
        {
            case 0:
                // Continuation line without leading whitespace, split between two non-whitespace characters.
                replacement = new[] { $"Subject: {value[..foldAt]}", value[foldAt..] };
                description = "Subject header is folded at a non-whitespace position with no linear whitespace on the continuation line.";
                break;
            case 1:
                replacement = new[] { $"Subject: {value}\nBareLFInHeader" };
                description = "Subject header contains a bare LF character.";
                break;
            default:
                replacement = new[] { $"Subject: {value}\rBareCRInHeader" };
                description = "Subject header contains a bare CR character.";
                break;
        }

        int line = message.ReplaceHeader("Subject", replacement);
        return new EmailAnomalyResult(
            message.ToBytes(),
            Audit(fileName, recordId, line + 1, "Subject", "header-fold", description, $"line {line + 1}, Subject header"));
    }

    private static EmailAnomalyResult ApplyRfc822Depth(byte[] emlBytes, string fileName, string recordId, long workItemIndex)
    {
        byte[] content = emlBytes;
        for (int depth = RfC822NestingDepth; depth >= 1; depth--)
        {
            content = WrapInForwardedPart(content, depth, workItemIndex);
        }

        return new EmailAnomalyResult(
            content,
            Audit(
                fileName,
                recordId,
                1,
                "Content-Type",
                "rf822-depth",
                $"Wrapped the message in {RfC822NestingDepth} nested message/rfc822 parts to exercise parser recursion limits.",
                $"Content-Type: multipart/mixed (nested message/rfc822 depth {RfC822NestingDepth})"));
    }

    private static byte[] WrapInForwardedPart(byte[] inner, int depth, long workItemIndex)
    {
        string boundary = $"----=_Chaos_Depth_{workItemIndex}_{depth}";
        string head =
            $"From: user{depth}@example.com{Crlf}" +
            $"To: recipient{depth}@example.com{Crlf}" +
            $"Subject: Forwarded Level {depth}{Crlf}" +
            $"Date: Mon, 01 Jan 2024 10:00:00 +0000{Crlf}" +
            $"MIME-Version: 1.0{Crlf}" +
            $"Content-Type: multipart/mixed; boundary=\"{boundary}\"{Crlf}" +
            Crlf +
            $"--{boundary}{Crlf}" +
            $"Content-Type: text/plain; charset=utf-8{Crlf}" +
            Crlf +
            $"Enclosing RFC 822 message level {depth}.{Crlf}" +
            $"--{boundary}{Crlf}" +
            $"Content-Type: message/rfc822{Crlf}" +
            $"Content-Disposition: attachment; filename=\"forwarded_{depth}.eml\"{Crlf}" +
            Crlf;
        byte[] headBytes = Lossless.GetBytes(head);
        byte[] tailBytes = Lossless.GetBytes($"{Crlf}--{boundary}--{Crlf}");

        var wrapped = new byte[headBytes.Length + inner.Length + tailBytes.Length];
        headBytes.CopyTo(wrapped, 0);
        inner.CopyTo(wrapped, headBytes.Length);
        tailBytes.CopyTo(wrapped, headBytes.Length + inner.Length);
        return wrapped;
    }

    private static bool TryFindBoundary(EmlLines message, int contentType, out int lineIndex, out string boundary)
    {
        const string parameter = "boundary=";
        int end = message.HeaderEndOf(contentType);
        for (int i = contentType; i < end; i++)
        {
            string line = message.Lines[i];
            int at = line.IndexOf(parameter, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                continue;
            }

            string rest = line[(at + parameter.Length)..];
            string value;
            if (rest.StartsWith('"'))
            {
                int closeQuote = rest.IndexOf('"', 1);
                value = closeQuote > 1 ? rest[1..closeQuote] : string.Empty;
            }
            else
            {
                int stop = rest.IndexOfAny([';', ' ', '\t']);
                value = stop >= 0 ? rest[..stop] : rest;
            }

            if (value.Length > 0)
            {
                lineIndex = i;
                boundary = value;
                return true;
            }
        }

        lineIndex = -1;
        boundary = string.Empty;
        return false;
    }

    // Returns a split point with non-whitespace characters on both sides, searching from the middle outward.
    private static int FindNonWhitespaceBoundary(string value)
    {
        int middle = value.Length / 2;
        for (int i = middle; i < value.Length; i++)
        {
            if (IsSplittable(value, i))
            {
                return i;
            }
        }

        for (int i = middle - 1; i >= 1; i--)
        {
            if (IsSplittable(value, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsSplittable(string value, int index) =>
        index > 0 && index < value.Length && !char.IsWhiteSpace(value[index - 1]) && !char.IsWhiteSpace(value[index]);

    private static ChaosAnomaly Audit(
        string fileName,
        string recordId,
        int lineNumber,
        string column,
        string errorType,
        string description,
        string location) =>
        new()
        {
            File = fileName,
            LineNumber = lineNumber.ToString(CultureInfo.InvariantCulture),
            RecordID = recordId,
            Column = column,
            ErrorType = errorType,
            Description = description,
            Location = location,
        };

    /// <summary>
    /// The EML message as physical lines. Splitting and joining on CRLF is lossless, so untouched lines
    /// keep their exact bytes.
    /// </summary>
    private sealed class EmlLines
    {
        private EmlLines(List<string> lines)
        {
            this.Lines = lines;
        }

        public List<string> Lines { get; }

        public static EmlLines Parse(byte[] bytes) => new(Lossless.GetString(bytes).Split(Crlf).ToList());

        public byte[] ToBytes() => Lossless.GetBytes(string.Join(Crlf, this.Lines));

        // Index of the blank line that ends the top-level header block, or the line count when there is none.
        public int HeaderBlockEnd()
        {
            int blank = this.Lines.IndexOf(string.Empty);
            return blank < 0 ? this.Lines.Count : blank;
        }

        public int FindHeader(string name)
        {
            string prefix = name + ":";
            int end = this.HeaderBlockEnd();
            for (int i = 0; i < end; i++)
            {
                if (this.Lines[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        // Index just after the last continuation line (a line starting with whitespace) of the header at start.
        public int HeaderEndOf(int start)
        {
            int i = start + 1;
            while (i < this.Lines.Count && this.Lines[i].Length > 0 && (this.Lines[i][0] == ' ' || this.Lines[i][0] == '\t'))
            {
                i++;
            }

            return i;
        }

        public string? HeaderValue(string name)
        {
            int start = this.FindHeader(name);
            if (start < 0)
            {
                return null;
            }

            int end = this.HeaderEndOf(start);
            var parts = new List<string>(end - start);
            for (int i = start; i < end; i++)
            {
                int valueStart = i == start ? this.Lines[i].IndexOf(':', StringComparison.Ordinal) + 1 : 0;
                parts.Add(this.Lines[i][valueStart..].Trim());
            }

            return string.Join(' ', parts).Trim();
        }

        /// <summary>
        /// Replaces the top-level header named <paramref name="name"/>, including its continuation lines,
        /// with <paramref name="newLines"/>. Inserts them before the header block end when the header is absent.
        /// </summary>
        /// <returns>The 0-based index of the first replacement line.</returns>
        public int ReplaceHeader(string name, params string[] newLines)
        {
            int start = this.FindHeader(name);
            if (start < 0)
            {
                start = this.HeaderBlockEnd();
            }
            else
            {
                this.Lines.RemoveRange(start, this.HeaderEndOf(start) - start);
            }

            this.Lines.InsertRange(start, newLines);
            return start;
        }

        public int FindClosingDelimiter(string boundary)
        {
            string closing = $"--{boundary}--";
            for (int i = this.Lines.Count - 1; i >= 0; i--)
            {
                if (this.Lines[i].TrimEnd() == closing)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
