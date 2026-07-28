namespace Zipper.Config;

/// <summary>
/// Parses the <c>--types</c> argument value (<c>type:weight</c> pairs, comma-separated)
/// into validated <see cref="FileTypeRatio"/> entries.
/// </summary>
public static class FileTypeRatioParser
{
    // Bounds keep the allocation math (fileCount * weight) safely inside Int64 range.
    internal const long MaxWeight = 1_000_000;

    public static bool TryParse(string? input, out IReadOnlyList<FileTypeRatio> ratios, out string? error)
    {
        ratios = Array.Empty<FileTypeRatio>();
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "--types requires a value (e.g., \"pdf:50,eml:30,tiff:20\").";
            return false;
        }

        var parsed = new List<FileTypeRatio>();
        foreach (var rawEntry in input.Split(','))
        {
            if (!TryParseEntry(rawEntry, parsed, out error))
            {
                return false;
            }
        }

        ratios = parsed;
        return true;
    }

    private static bool TryParseEntry(string rawEntry, List<FileTypeRatio> parsed, out string? error)
    {
        error = null;
        var entry = rawEntry.Trim();
        if (entry.Length == 0)
        {
            error = "Invalid --types value: empty entry.";
            return false;
        }

        var colonIndex = entry.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex <= 0 || colonIndex == entry.Length - 1 || entry.IndexOf(':', colonIndex + 1) >= 0)
        {
            error = $"Invalid --types entry '{entry}'. Expected format: <type>:<weight> (e.g., pdf:50).";
            return false;
        }

        var type = entry[..colonIndex].Trim().ToLowerInvariant();
        var weightStr = entry[(colonIndex + 1)..].Trim();

        if (type.Length == 0 || weightStr.Length == 0)
        {
            error = $"Invalid --types entry '{entry}'. Expected format: <type>:<weight> (e.g., pdf:50).";
            return false;
        }

        if (!FileGeneratorFactory.IsKnownType(type))
        {
            error = $"Unsupported file type '{type}' in --types. Supported types: pdf, jpg, tiff, eml, docx, xlsx.";
            return false;
        }

        // Digit-only check rejects signs ('+5', '-3') that long.TryParse would otherwise accept.
        if (weightStr.Any(c => !char.IsDigit(c)) ||
            !long.TryParse(weightStr, System.Globalization.CultureInfo.InvariantCulture, out var weight) || weight <= 0)
        {
            error = $"Invalid --types weight '{weightStr}' for type '{type}'. Weight must be a positive integer.";
            return false;
        }

        if (weight > MaxWeight)
        {
            error = $"Invalid --types weight '{weightStr}' for type '{type}'. Weight must not exceed {MaxWeight}.";
            return false;
        }

        if (parsed.Any(r => string.Equals(r.Type, type, StringComparison.Ordinal)))
        {
            error = $"Duplicate file type '{type}' in --types.";
            return false;
        }

        parsed.Add(new FileTypeRatio { Type = type, Weight = weight });
        return true;
    }
}
