using Zipper.Profiles;
using Zipper.Utils;

namespace Zipper.LoadFiles;

/// <summary>
/// Static helpers shared across DAT mode composers (Standard, Loadfile-Only, Production).
/// </summary>
internal static class DatComposerShared
{
    /// <summary>
    /// Applies the naming convention (if any) to a column name.
    /// </summary>
    internal static string ApplyConvention(string name, string? namingConvention)
        => NamingConventionHelper.ApplyConvention(name, namingConvention);

    /// <summary>
    /// Returns the effective timestamp: fixed epoch when a seed is set (for reproducibility),
    /// otherwise the current UTC time.
    /// </summary>
    internal static DateTime EffectiveNow(FileGenerationRequest request)
        => request.Metadata.Seed.HasValue
            ? new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            : DateTime.UtcNow;

    /// <summary>
    /// Builds a <see cref="LoadFileRecord"/> from the header columns and ordered values.
    /// </summary>
    internal static LoadFileRecord MakeRecord(
        IReadOnlyList<string> headerColumns, string recordId, List<string> orderedValues)
        => LoadFileRecordBuilder.Build(headerColumns, orderedValues, recordId);

    /// <summary>
    /// Resolves parent/child identifiers and attachment status for a file, using the Bates
    /// sequence when configured or a DOC-index fallback otherwise.
    /// </summary>
    internal static (string ParentId, string ChildId, bool HasAttachment) GetFamilyIdentifiers(
        FileData fileData, FileGenerationRequest request, BatesSequence? batesSequence)
    {
        bool hasAttachment = request.Metadata.WithFamilies && request.Output.IsEml && fileData.Attachment.HasValue;
        string parentId = batesSequence is not null
            ? batesSequence.Format(fileData.WorkItem.Index - 1).ToString()
            : $"DOC{fileData.WorkItem.Index:D8}";
        string childId = hasAttachment ? $"{parentId}_A001" : parentId;
        return (parentId, childId, hasAttachment);
    }

    /// <summary>
    /// Resolves a hash-column name (e.g. "MD5HASH") to its value from the file's pre-computed hashes.
    /// Returns null when the column is not a hash column or the hash is not present.
    /// </summary>
    internal static string? ResolveHashColumn(string upperColumnName, FileData fileData)
    {
        if (fileData.Hashes is null)
        {
            return null;
        }

        var algo = upperColumnName switch
        {
            "MD5HASH" or "MD5_HASH" or "MD5 HASH" => Config.HashAlgorithm.MD5,
            "SHA1HASH" or "SHA1_HASH" or "SHA1 HASH" => Config.HashAlgorithm.SHA1,
            "SHA256HASH" or "SHA256_HASH" or "SHA256 HASH" => Config.HashAlgorithm.SHA256,
            _ => (Config.HashAlgorithm?)null,
        };

        if (algo.HasValue && fileData.Hashes.TryGetValue(algo.Value, out var hashValue))
        {
            return hashValue;
        }

        return null;
    }

    /// <summary>
    /// Returns the effective column-profile generator for Standard mode, or null when
    /// no profile applies. Falls back to built-in profiles when metadata columns are requested.
    /// </summary>
    internal static DataGenerator? GetEffectiveProfileGenerator(FileGenerationRequest request, DateTime now)
    {
        var profile = request.Metadata.ColumnProfile;
        if (profile is null && request.Metadata.ShouldIncludeMetadataColumns(request.Output))
        {
            profile = request.Output.IsEml
                ? BuiltInProfiles.LegacyEml
                : BuiltInProfiles.LegacyWithMetadata;
        }

        return profile is not null ? new DataGenerator(profile, request.Metadata.Seed, now) : null;
    }
}
