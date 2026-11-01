using System.Text;
using Zipper.LoadFiles;

namespace Zipper;

/// <summary>
/// Chaos Engine applier for Email and MIME-level anomaly types.
/// Injects boundary disagreements, malformed RFC 2047 encoded-word sequences,
/// illegal header folding / bare CR/LF, and excessive RFC 822 nesting depth.
/// </summary>
internal sealed class EmailAnomalyApplier : IAnomalyApplier
{
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly Random random;

    public EmailAnomalyApplier(Random? random = null)
    {
#pragma warning disable S2245 // Pseudo-randomness is safe for mock metadata generation
        this.random = random ?? new Random();
#pragma warning restore S2245
    }

    /// <inheritdoc/>
    public string Apply(
        long lineNumber,
        string line,
        string recordId,
        string chaosType,
        List<ChaosAnomaly> anomalies,
        LoadFileRecord? record = null,
        ILoadFileSerializer? serializer = null)
    {
        string normalizedType = NormalizeType(chaosType);
        switch (normalizedType)
        {
            case "encoded-word":
                if (line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
                {
                    string mutated = "Subject: =?unknown-8859-99?B?dGVzdA==?=";
                    anomalies.Add(new ChaosAnomaly
                    {
                        File = recordId,
                        LineNumber = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        RecordID = recordId,
                        Column = "Subject",
                        ErrorType = "encoded-word",
                        Description = "Injected RFC 2047 encoded-word with unknown charset 'unknown-8859-99'.",
                        Location = $"line {lineNumber}, Subject header",
                    });
                    return mutated;
                }
                return line;

            case "header-fold":
                if (line.Contains(':', StringComparison.Ordinal))
                {
                    int colonIndex = line.IndexOf(':', StringComparison.Ordinal);
                    string headerName = line[..colonIndex].Trim();
                    string mutated = line.Insert(colonIndex + 2, "\r\nIllegalFold ");
                    anomalies.Add(new ChaosAnomaly
                    {
                        File = recordId,
                        LineNumber = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        RecordID = recordId,
                        Column = headerName,
                        ErrorType = "header-fold",
                        Description = "Injected illegal header folding without linear whitespace on continuation line.",
                        Location = $"line {lineNumber}, {headerName} header",
                    });
                    return mutated;
                }
                return line;

            case "mime-boundary":
                if (line.StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
                {
                    string mutated = "Content-Type: multipart/mixed; boundary=\"----=_Part_MISMATCH\"";
                    anomalies.Add(new ChaosAnomaly
                    {
                        File = recordId,
                        LineNumber = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        RecordID = recordId,
                        Column = "Content-Type",
                        ErrorType = "mime-boundary",
                        Description = "Boundary parameter in Content-Type header differs from the actual delimiters in the body.",
                        Location = $"line {lineNumber}, Content-Type header",
                    });
                    return mutated;
                }
                return line;

            case "rf822-depth":
                anomalies.Add(new ChaosAnomaly
                {
                    File = recordId,
                    LineNumber = lineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    RecordID = recordId,
                    Column = "Content-Type",
                    ErrorType = "rf822-depth",
                    Description = "Generated nested message/rfc822 parts to a depth of 25, exceeding common parser recursion limits.",
                    Location = $"line {lineNumber}, Content-Type header",
                });
                return line;

            default:
                return line;
        }
    }

    /// <inheritdoc/>
    public byte[]? GetEncodingAnomaly(long lineNumber, long nextLineNumber, Encoding encoding, List<ChaosAnomaly> anomalies)
        => null;

    /// <summary>
    /// Applies an email/MIME chaos anomaly to rendered EML bytes.
    /// </summary>
    public byte[] ApplyToEmail(
        byte[] emlBytes,
        string fileName,
        string recordId,
        string chaosType,
        List<ChaosAnomaly> anomalies,
        long workItemIndex = 1)
    {
        string normalizedType = NormalizeType(chaosType);
        switch (normalizedType)
        {
            case "mime-boundary":
                return ApplyMimeBoundary(emlBytes, fileName, recordId, anomalies);

            case "encoded-word":
                return ApplyEncodedWord(emlBytes, fileName, recordId, anomalies);

            case "header-fold":
                return ApplyHeaderFold(emlBytes, fileName, recordId, anomalies);

            case "rf822-depth":
                return ApplyRfc822Depth(emlBytes, fileName, recordId, anomalies, workItemIndex);

            default:
                return emlBytes;
        }
    }

    private byte[] ApplyMimeBoundary(
        byte[] emlBytes,
        string fileName,
        string recordId,
        List<ChaosAnomaly> anomalies)
    {
        string text = Utf8.GetString(emlBytes);
        string[] lines = text.Split("\r\n");

        int contentTypeLineIndex = -1;
        string? boundary = null;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0)
            {
                // Reached end of top-level message headers
                break;
            }

            if (lines[i].StartsWith("Content-Type:", StringComparison.OrdinalIgnoreCase))
            {
                contentTypeLineIndex = i;
                int bIndex = lines[i].IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
                if (bIndex >= 0)
                {
                    string rawVal = lines[i][(bIndex + 9)..].Trim();
                    if (rawVal.StartsWith('"'))
                    {
                        int quoteEnd = rawVal.IndexOf('"', 1);
                        if (quoteEnd > 1)
                        {
                            boundary = rawVal[1..quoteEnd];
                        }
                    }
                    else
                    {
                        int semi = rawVal.IndexOfAny([';', ' ', '\r', '\n']);
                        boundary = semi >= 0 ? rawVal[..semi] : rawVal;
                    }
                }
                break;
            }
        }

        if (contentTypeLineIndex >= 0 && !string.IsNullOrEmpty(boundary))
        {
            // Choose between mismatching Content-Type boundary vs removing closing delimiter
            int variant = this.random.Next(2);
            if (variant == 0)
            {
                // Variant A: Corrupt the boundary parameter in Content-Type
                string oldLine = lines[contentTypeLineIndex];
                string corruptedLine = oldLine.Replace(boundary, boundary + "_MISMATCH", StringComparison.Ordinal);
                lines[contentTypeLineIndex] = corruptedLine;

                anomalies.Add(new ChaosAnomaly
                {
                    File = fileName,
                    LineNumber = (contentTypeLineIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    RecordID = recordId,
                    Column = "Content-Type",
                    ErrorType = "mime-boundary",
                    Description = "Boundary parameter in Content-Type header differs from the actual delimiters in the body (cpython#135854).",
                    Location = $"line {contentTypeLineIndex + 1}, Content-Type header",
                });

                return Utf8.GetBytes(string.Join("\r\n", lines));
            }
            else
            {
                // Variant B: Alter or remove the final closing delimiter --{boundary}--
                string closingDelimiter = $"--{boundary}--";
                int closingIndex = -1;
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    if (lines[i].Trim() == closingDelimiter)
                    {
                        closingIndex = i;
                        break;
                    }
                }

                if (closingIndex >= 0)
                {
                    // Remove the trailing -- so it looks like an unclosed part delimiter
                    lines[closingIndex] = $"--{boundary}";

                    anomalies.Add(new ChaosAnomaly
                    {
                        File = fileName,
                        LineNumber = (closingIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                        RecordID = recordId,
                        Column = "MIME-Boundary",
                        ErrorType = "mime-boundary",
                        Description = "Final MIME multipart closing delimiter is missing trailing hyphens, leaving container unclosed.",
                        Location = $"line {closingIndex + 1}, MIME closing delimiter",
                    });

                    return Utf8.GetBytes(string.Join("\r\n", lines));
                }
            }
        }

        // Fallback for single-part emails: change Content-Type to multipart with missing delimiters
        if (contentTypeLineIndex >= 0)
        {
            lines[contentTypeLineIndex] = "Content-Type: multipart/mixed; boundary=\"----=_Part_MISSING_DELIMITERS\"";
            anomalies.Add(new ChaosAnomaly
            {
                File = fileName,
                LineNumber = (contentTypeLineIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                RecordID = recordId,
                Column = "Content-Type",
                ErrorType = "mime-boundary",
                Description = "Content-Type header declares multipart boundary '----=_Part_MISSING_DELIMITERS' that does not exist in the body.",
                Location = $"line {contentTypeLineIndex + 1}, Content-Type header",
            });
            return Utf8.GetBytes(string.Join("\r\n", lines));
        }

        return emlBytes;
    }

    private byte[] ApplyEncodedWord(
        byte[] emlBytes,
        string fileName,
        string recordId,
        List<ChaosAnomaly> anomalies)
    {
        string text = Utf8.GetString(emlBytes);
        string[] lines = text.Split("\r\n");

        int targetLineIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
            {
                targetLineIndex = i;
                break;
            }
        }

        if (targetLineIndex < 0)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("From:", StringComparison.OrdinalIgnoreCase))
                {
                    targetLineIndex = i;
                    break;
                }
            }
        }

        if (targetLineIndex < 0)
        {
            return emlBytes;
        }

        int variant = this.random.Next(3);
        string malformedEncodedWord = variant switch
        {
            0 => "=?unknown-8859-99?B?dGVzdA==?=",
            1 => "=?utf-8?B?abc===?=",
            _ => "=?utf-8?B?dGV zdA==?=",
        };

        string description = variant switch
        {
            0 => "Injected RFC 2047 encoded-word with unknown charset 'unknown-8859-99'.",
            1 => "Injected RFC 2047 encoded-word with invalid Base64 padding 'abc==='.",
            _ => "Injected RFC 2047 encoded-word containing illegal whitespace.",
        };

        lines[targetLineIndex] = $"Subject: {malformedEncodedWord}";

        anomalies.Add(new ChaosAnomaly
        {
            File = fileName,
            LineNumber = (targetLineIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            RecordID = recordId,
            Column = "Subject",
            ErrorType = "encoded-word",
            Description = description,
            Location = $"line {targetLineIndex + 1}, Subject header",
        });

        return Utf8.GetBytes(string.Join("\r\n", lines));
    }

    private byte[] ApplyHeaderFold(
        byte[] emlBytes,
        string fileName,
        string recordId,
        List<ChaosAnomaly> anomalies)
    {
        string text = Utf8.GetString(emlBytes);
        string[] lines = text.Split("\r\n");

        int targetLineIndex = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))
            {
                targetLineIndex = i;
                break;
            }
        }

        if (targetLineIndex < 0)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith("From:", StringComparison.OrdinalIgnoreCase))
                {
                    targetLineIndex = i;
                    break;
                }
            }
        }

        if (targetLineIndex < 0)
        {
            return emlBytes;
        }

        string originalLine = lines[targetLineIndex];
        int colon = originalLine.IndexOf(':', StringComparison.Ordinal);
        string headerVal = colon >= 0 && colon + 1 < originalLine.Length
            ? originalLine[(colon + 1)..].TrimStart()
            : "Test Message";

        int variant = this.random.Next(3);
        string mutatedLine;
        string description;

        if (variant == 0)
        {
            // Illegal folding without leading whitespace
            mutatedLine = $"Subject: {headerVal}\r\nIllegalFoldedContinuation";
            description = "Injected illegal header folding without linear whitespace on continuation line.";
        }
        else if (variant == 1)
        {
            // Bare LF inside header
            mutatedLine = $"Subject: {headerVal}\nBareLFInHeader";
            description = "Injected bare LF character inside header value.";
        }
        else
        {
            // Bare CR inside header
            mutatedLine = $"Subject: {headerVal}\rBareCRInHeader";
            description = "Injected bare CR character inside header value.";
        }

        lines[targetLineIndex] = mutatedLine;

        anomalies.Add(new ChaosAnomaly
        {
            File = fileName,
            LineNumber = (targetLineIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            RecordID = recordId,
            Column = "Subject",
            ErrorType = "header-fold",
            Description = description,
            Location = $"line {targetLineIndex + 1}, Subject header",
        });

        return Utf8.GetBytes(string.Join("\r\n", lines));
    }

    private static byte[] ApplyRfc822Depth(
        byte[] emlBytes,
        string fileName,
        string recordId,
        List<ChaosAnomaly> anomalies,
        long workItemIndex)
    {
        const int targetDepth = 25;
        byte[] currentBytes = emlBytes;

        for (int depth = targetDepth; depth >= 1; depth--)
        {
            string boundary = string.Format(System.Globalization.CultureInfo.InvariantCulture, "----=_Part_Depth_{0}_{1}", workItemIndex, depth);
            var sb = new StringBuilder();
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"From: user_{depth}@example.com\r\n");
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"To: recipient_{depth}@example.com\r\n");
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"Subject: Forwarded Level {depth}\r\n");
            sb.Append("Date: Mon, 01 Jan 2024 10:00:00 +0000\r\n");
            sb.Append("MIME-Version: 1.0\r\n");
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"Content-Type: multipart/mixed; boundary=\"{boundary}\"\r\n");
            sb.Append("\r\n");
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"--{boundary}\r\n");
            sb.Append("Content-Type: text/plain; charset=utf-8\r\n");
            sb.Append("\r\n");
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"Enclosing RFC 822 message level {depth}.\r\n");
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"--{boundary}\r\n");
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"Content-Type: message/rfc822; name=\"forwarded_{depth}.eml\"\r\n");
            sb.Append(System.Globalization.CultureInfo.InvariantCulture, $"Content-Disposition: attachment; filename=\"forwarded_{depth}.eml\"\r\n");
            sb.Append("\r\n");

            byte[] headerPrefixBytes = Utf8.GetBytes(sb.ToString());
            byte[] closingBytes = Utf8.GetBytes($"\r\n--{boundary}--\r\n");

            var wrapped = new byte[headerPrefixBytes.Length + currentBytes.Length + closingBytes.Length];
            Buffer.BlockCopy(headerPrefixBytes, 0, wrapped, 0, headerPrefixBytes.Length);
            Buffer.BlockCopy(currentBytes, 0, wrapped, headerPrefixBytes.Length, currentBytes.Length);
            Buffer.BlockCopy(closingBytes, 0, wrapped, headerPrefixBytes.Length + currentBytes.Length, closingBytes.Length);

            currentBytes = wrapped;
        }

        anomalies.Add(new ChaosAnomaly
        {
            File = fileName,
            LineNumber = "1",
            RecordID = recordId,
            Column = "Content-Type",
            ErrorType = "rf822-depth",
            Description = $"Generated nested message/rfc822 parts to a depth of {targetDepth}, exceeding common parser recursion limits.",
            Location = $"Content-Type: multipart/mixed (nested message/rfc822 depth {targetDepth})",
        });

        return currentBytes;
    }

    private static string NormalizeType(string type)
    {
        string lower = type.Trim().ToLowerInvariant();
        return lower switch
        {
            "rfc822-depth" => "rf822-depth",
            _ => lower,
        };
    }
}
