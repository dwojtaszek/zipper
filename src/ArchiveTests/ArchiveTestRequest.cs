namespace Zipper.ArchiveTests;

/// <summary>
/// A validated Archive Test run request (REQ-208): the selected Case Keys, the Seed
/// (default 42), and the output directory. Output paths are resolved under the working
/// directory via <see cref="PathValidator.ResolveSecurePath"/> exactly like the
/// Standard-mode output flag; an existing destination is a validation error, never a merge.
/// </summary>
internal sealed record ArchiveTestRequest
{
    internal const int DefaultSeed = 42;

    /// <summary>Total budget for all published fixture bytes in one suite (REQ-213).</summary>
    internal const long MaxSuiteBytes = 256L * 1024 * 1024;

    /// <summary>Per-sidecar budget for one Expectation File (REQ-213).</summary>
    internal const long MaxSidecarBytes = ArchiveTestCaseSemantics.MaxJsonBytes;

    internal required IReadOnlyList<string> CaseKeys { get; init; }

    internal required int Seed { get; init; }

    internal required DirectoryInfo OutputDirectory { get; init; }

    internal static ArchiveTestRequest Create(IReadOnlyList<string> caseKeys, int seed, string outputPath)
    {
        if (caseKeys.Count == 0)
        {
            throw new ArgumentException("At least one Archive Test Case Key must be selected.");
        }

        var duplicates = caseKeys
            .GroupBy(key => key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();
        if (duplicates.Count > 0)
        {
            throw new ArgumentException($"Duplicate Archive Test Case Keys: {string.Join(", ", duplicates.Order())}.");
        }

        foreach (var key in caseKeys)
        {
            // Resolves the case definition and throws KeyNotFoundException naming the key for unknown keys.
            _ = ArchiveTestCatalog.GetCase(key);
        }

        var outputDirectory = PathValidator.ResolveSecurePath(outputPath, Directory.GetCurrentDirectory())
            ?? throw new ArgumentException($"Output path '{outputPath}' is invalid or outside the working directory.");
        if (outputDirectory.Exists || File.Exists(outputDirectory.FullName))
        {
            throw new InvalidOperationException(
                $"Output target '{outputDirectory.FullName}' already exists. Archive Test publication never merges into or overwrites an existing directory, file, or link.");
        }

        return new ArchiveTestRequest { CaseKeys = caseKeys, Seed = seed, OutputDirectory = outputDirectory };
    }
}
