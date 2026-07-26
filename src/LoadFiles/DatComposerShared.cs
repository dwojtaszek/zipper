using Zipper.Profiles;

namespace Zipper.LoadFiles;

/// <summary>
/// Static helpers shared across DAT mode composers (Standard, Loadfile-Only, Production).
/// </summary>
internal static class DatComposerShared
{
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
