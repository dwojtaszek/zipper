namespace Zipper.SourceInput;

/// <summary>
/// Validates and normalizes source-supplied relative paths so generated output entries can
/// never escape the output root. Pure segment-level checks (no filesystem I/O): rejects
/// rooted/UNC/drive-letter paths, parent traversal, dot segments, and characters that are
/// invalid in ZIP entry names on any supported platform.
/// </summary>
internal static class SourcePathSanitizer
{
    private static readonly char[] InvalidChars = { '<', '>', '|', '"', '?', '*', ':' };

    internal static bool TryNormalize(string? rawPath, out string normalized, out string? error)
    {
        normalized = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(rawPath))
        {
            error = "Path is empty.";
            return false;
        }

        var trimmed = rawPath.Trim();

        var invalidIndex = trimmed.IndexOfAny(InvalidChars);
        if (invalidIndex >= 0)
        {
            error = $"Path contains invalid character '{trimmed[invalidIndex]}'.";
            return false;
        }

        foreach (var c in trimmed)
        {
            if (c < 32)
            {
                error = "Path contains a control character.";
                return false;
            }
        }

        var unified = trimmed.Replace('\\', '/');
        if (unified.StartsWith("/", StringComparison.Ordinal))
        {
            error = "Path must be relative (rooted and UNC paths are not allowed).";
            return false;
        }

        var segments = unified.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            if (segment == "..")
            {
                error = "Path contains a parent-directory segment ('..').";
                return false;
            }

            if (segment == ".")
            {
                error = "Path contains a current-directory segment ('.').";
                return false;
            }
        }

        if (segments.Length == 0)
        {
            error = "Path is empty.";
            return false;
        }

        normalized = string.Join('/', segments);
        return true;
    }
}
