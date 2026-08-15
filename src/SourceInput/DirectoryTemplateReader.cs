namespace Zipper.SourceInput;

/// <summary>
/// Thin I/O shell for Directory Template intake: walks a user-provided directory template
/// (mirroring relative paths, File Types inferred from extensions) and feeds each entry into
/// the shared <see cref="SourceRecordIntake"/> pipeline, which owns path sanitization,
/// File Type validation, and count rules. Source file bytes are never read or copied.
/// </summary>
internal static class DirectoryTemplateReader
{
    internal static bool TryRead(string directoryPath, out IReadOnlyList<SourceRecord> records, out string? error, int maxRecords = SourceRecordIntake.MaxSourceRecords)
    {
        records = Array.Empty<SourceRecord>();
        error = null;

        if (!Directory.Exists(directoryPath))
        {
            error = $"Directory template '{directoryPath}' does not exist.";
            return false;
        }

        // Include hidden/system entries: silently skipping files would produce fewer Source
        // Records than the template contains. Reparse points (symbolic links) are skipped to
        // prevent infinite recursion through link cycles. Each entry is validated and
        // converted during the lazy enumeration (inside the try, so I/O errors stay captured)
        // and the cap is enforced by the intake as entries arrive, so memory stays bounded by
        // the Source Record list.
        var intake = new SourceRecordIntake($"Directory template '{directoryPath}'", maxRecords);
        try
        {
            foreach (var file in Directory.EnumerateFiles(directoryPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                IgnoreInaccessible = false,
            }))
            {
                var rawRelative = Path.GetRelativePath(directoryPath, file);
                if (!intake.TryAdd(rawRelative, fileTypeText: null, controlNumber: null, batesNumber: null,
                    metadata: null, rowContext: $"Directory template entry '{rawRelative}'", out var rowError))
                {
                    error = rowError;
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Cannot read directory template '{directoryPath}': {ex.Message}";
            return false;
        }

        if (!intake.TryBuild(out var built, out error))
        {
            return false;
        }

        // Directory Template ordering is the walker's policy: Source CSV preserves source
        // order, Directory Template sorts by relative path.
        var sorted = built.ToList();
        sorted.Sort(static (a, b) => string.CompareOrdinal(a.RelativePath, b.RelativePath));
        records = sorted;
        return true;
    }
}
