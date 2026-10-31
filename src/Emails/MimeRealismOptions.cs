namespace Zipper.Emails;

/// <summary>
/// Configuration options for MIME realism emission modes in email generation.
/// </summary>
public record MimeRealismOptions
{
    public static readonly MimeRealismOptions Disabled = new();

    public static readonly MimeRealismOptions Standard = new()
    {
        IsEnabled = true,
        EncodedWords = true,
        HeaderFolding = true,
        QuotedPrintableBody = true,
        MultipartAlternative = true,
        MultiAttachment = true,
    };

    public static readonly MimeRealismOptions Full = new()
    {
        IsEnabled = true,
        EncodedWords = true,
        HeaderFolding = true,
        QuotedPrintableBody = true,
        Base64Body = true,
        MultipartAlternative = true,
        MultiAttachment = true,
        NestedRfc822 = true,
        DeliberatelyNonRfc = true,
    };

    public bool IsEnabled { get; init; }

    public bool EncodedWords { get; init; }

    public bool HeaderFolding { get; init; }

    public bool QuotedPrintableBody { get; init; }

    public bool Base64Body { get; init; }

    public bool MultipartAlternative { get; init; }

    public bool MultiAttachment { get; init; }

    public bool NestedRfc822 { get; init; }

    public bool DeliberatelyNonRfc { get; init; }

    public int MaxNestingDepth { get; init; } = 2;

    public static MimeRealismOptions Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Disabled;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        if (trimmed is "none" or "off" or "false")
        {
            return Disabled;
        }

        if (trimmed is "standard" or "basic")
        {
            return Standard;
        }

        if (trimmed is "full" or "all")
        {
            return Full;
        }

        var tokens = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return Disabled;
        }

        bool encodedWords = false;
        bool headerFolding = false;
        bool qpBody = false;
        bool base64Body = false;
        bool multipartAlt = false;
        bool multiAttachment = false;
        bool nestedRfc822 = false;
        bool nonRfc = false;

        foreach (var token in tokens)
        {
            switch (token)
            {
                case "encoded-words":
                case "encodedword":
                case "rfc2047":
                    encodedWords = true;
                    break;
                case "header-folding":
                case "folding":
                    headerFolding = true;
                    break;
                case "quoted-printable":
                case "qp":
                    qpBody = true;
                    break;
                case "base64-body":
                case "base64":
                    base64Body = true;
                    break;
                case "multipart-alternative":
                case "multipart":
                case "alternative":
                    multipartAlt = true;
                    break;
                case "multi-attachment":
                case "multi-attachments":
                    multiAttachment = true;
                    break;
                case "nested-rfc822":
                case "nested":
                case "rfc822":
                    nestedRfc822 = true;
                    break;
                case "non-rfc":
                case "deliberately-non-rfc":
                    nonRfc = true;
                    encodedWords = true;
                    break;
            }
        }

        return new MimeRealismOptions
        {
            IsEnabled = true,
            EncodedWords = encodedWords || nonRfc,
            HeaderFolding = headerFolding,
            QuotedPrintableBody = qpBody,
            Base64Body = base64Body,
            MultipartAlternative = multipartAlt,
            MultiAttachment = multiAttachment,
            NestedRfc822 = nestedRfc822,
            DeliberatelyNonRfc = nonRfc,
        };
    }

    public static bool TryValidate(string? value, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = null;
            return true;
        }

        var trimmed = value.Trim().ToLowerInvariant();
        if (trimmed is "none" or "off" or "false" or "standard" or "basic" or "full" or "all")
        {
            errorMessage = null;
            return true;
        }

        var validTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "encoded-words", "encodedword", "rfc2047",
            "header-folding", "folding",
            "quoted-printable", "qp",
            "base64-body", "base64",
            "multipart-alternative", "multipart", "alternative",
            "multi-attachment", "multi-attachments",
            "nested-rfc822", "nested", "rfc822",
            "non-rfc", "deliberately-non-rfc",
        };

        var rawTokens = trimmed.Split(',');
        if (rawTokens.Length == 0 || rawTokens.All(string.IsNullOrWhiteSpace))
        {
            errorMessage = "Realism mode token list cannot be empty.";
            return false;
        }

        foreach (var raw in rawTokens)
        {
            var token = raw.Trim();
            if (string.IsNullOrEmpty(token))
            {
                errorMessage = "Realism mode token list cannot contain empty tokens.";
                return false;
            }

            if (!validTokens.Contains(token))
            {
                errorMessage = $"Invalid realism mode token: '{token}'. Valid values: none, standard, full, or comma-separated tokens: {string.Join(", ", validTokens)}";
                return false;
            }
        }

        errorMessage = null;
        return true;
    }
}
