namespace Zipper.Validation;

public sealed class PathReconciliationValidator
{
    internal static ValidationFinding? FindSinglePathMismatch(string loadFilePath, string expectedArchiveEntry)
    {
        var normalizedPath = loadFilePath.Replace('\\', '/');
        var normalizedEntry = expectedArchiveEntry.Replace('\\', '/');
        if (string.Equals(normalizedPath, normalizedEntry, StringComparison.OrdinalIgnoreCase)
            || string.Equals(GetSidecarKey(normalizedPath), GetSidecarKey(normalizedEntry), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new ValidationFinding(
            ValidationSeverity.Error,
            "PathReconciliation",
            $"Load file path '{loadFilePath}' does not resolve to any archive entry or sidecar file.");
    }

    public void Validate(IEnumerable<string> loadFilePaths, IEnumerable<string> archiveEntries, string filePath, ValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(loadFilePaths);
        ArgumentNullException.ThrowIfNull(archiveEntries);
        ArgumentNullException.ThrowIfNull(result);
        var entrySet = new HashSet<string>(archiveEntries, StringComparer.OrdinalIgnoreCase);
        var sidecarSet = new HashSet<string>(entrySet.Select(GetSidecarKey), StringComparer.OrdinalIgnoreCase);

        foreach (var loadFilePath in loadFilePaths)
        {
            var normalized = loadFilePath.Replace('\\', '/');
            if (entrySet.Contains(normalized))
                continue;

            var dir = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? "";
            var nameWithoutExt = Path.GetFileNameWithoutExtension(normalized);
            var key = $"{dir}|{nameWithoutExt}";

            if (!sidecarSet.Contains(key))
            {
                result.Add(new ValidationFinding(
                    ValidationSeverity.Error,
                    "PathReconciliation",
                    $"Load file path '{loadFilePath}' does not resolve to any archive entry or sidecar file.",
                    filePath));
            }
        }
    }

    private static string GetSidecarKey(string path)
        => $"{Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty}|{Path.GetFileNameWithoutExtension(path)}";
}
