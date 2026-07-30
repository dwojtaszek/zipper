namespace Zipper.SourceInput;

/// <summary>
/// Builds <see cref="SourceRecord"/> rows by walking a user-provided directory template.
/// The directory structure is mirrored (relative paths and File Types inferred from
/// extensions); source file bytes are never read or copied.
/// </summary>
internal static class DirectoryTemplateReader
{
    internal static bool TryRead(string directoryPath, out IReadOnlyList<SourceRecord> records, out string? error)
    {
        records = Array.Empty<SourceRecord>();
        error = null;

        if (!Directory.Exists(directoryPath))
        {
            error = $"Directory template '{directoryPath}' does not exist.";
            return false;
        }

        var result = new List<SourceRecord>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Materialize eagerly (EnumerateFiles is lazy, so enumeration-time I/O errors would
        // otherwise escape the catch) and include hidden/system entries: silently skipping
        // files would produce fewer Source Records than the template contains. Reparse points
        // (symbolic links) are skipped to prevent infinite recursion through link cycles.
        List<string> files;
        try
        {
            files = Directory.EnumerateFiles(directoryPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
            }).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Cannot read directory template '{directoryPath}': {ex.Message}";
            return false;
        }

        foreach (var file in files)
        {
            var rawRelative = Path.GetRelativePath(directoryPath, file);
            if (!SourcePathSanitizer.TryNormalize(rawRelative, out var relativePath, out var pathError))
            {
                error = $"Directory template entry '{rawRelative}' is not a safe relative path: {pathError}";
                return false;
            }

            var extension = Path.GetExtension(relativePath).ToLowerInvariant();
            if (!SourceFileTypeMap.TryFromExtension(extension, out var fileType))
            {
                error = extension.Length == 0
                    ? $"Directory template file '{relativePath}' has no extension; the File Type cannot be inferred."
                    : $"Directory template file '{relativePath}' has unsupported extension '{extension}'. Supported: {SourceFileTypeMap.SupportedExtensionsDisplay}.";
                return false;
            }

            if (!seenPaths.Add(relativePath))
            {
                error = $"Directory template contains duplicate relative path '{relativePath}'.";
                return false;
            }

            result.Add(new SourceRecord
            {
                RelativePath = relativePath,
                FileType = fileType,
            });
        }

        if (result.Count == 0)
        {
            error = $"Directory template '{directoryPath}' contains no files.";
            return false;
        }

        result.Sort(static (a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        records = result;
        return true;
    }
}
