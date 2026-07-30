using System.Text.Json;

namespace Zipper.Profiles;

/// <summary>
/// Provides built-in column profiles for common e-discovery workflows.
/// Profile data lives in embedded JSON resources under Profiles/BuiltIns/ and is
/// deserialized with the same JSON format and serializer options as custom JSON profiles.
/// </summary>
public static class BuiltInProfiles
{
    /// <summary>
    /// Gets the available built-in profile names.
    /// </summary>
    public static readonly string[] ProfileNames = { "minimal", "standard", "litigation", "full" };

    private static readonly string[] ResourceNames =
    {
        "minimal", "standard", "litigation", "full", "legacywithmetadata", "legacyeml", "legacywithcollectionmetadata",
    };

    private static readonly Lazy<IReadOnlyDictionary<string, ColumnProfile>> Profiles = new(LoadProfiles);

    /// <summary>
    /// Gets the minimal profile (5 columns).
    /// </summary>
    public static ColumnProfile Minimal => GetShared("minimal");

    /// <summary>
    /// Gets the standard profile (24 columns).
    /// </summary>
    public static ColumnProfile Standard => GetShared("standard");

    /// <summary>
    /// Gets the litigation profile (48 columns).
    /// </summary>
    public static ColumnProfile Litigation => GetShared("litigation");

    /// <summary>
    /// Gets the full profile (138 columns).
    /// </summary>
    public static ColumnProfile Full => GetShared("full");

    /// <summary>
    /// Gets the legacy metadata pseudo-profile activated by --with-metadata on non-EML files.
    /// Four fixed columns: CUSTODIAN (folder-based), DATESENT, AUTHOR, FILESIZE.
    /// </summary>
    public static ColumnProfile LegacyWithMetadata => GetShared("legacywithmetadata");

    /// <summary>
    /// Gets the legacy EML pseudo-profile for EML files.
    /// Includes metadata columns plus five email columns read from fileData.Email.
    /// </summary>
    public static ColumnProfile LegacyEml => GetShared("legacyeml");

    /// <summary>
    /// Gets the collection metadata profile activated by --with-collection-metadata.
    /// Five columns: DATA_SOURCE, COLLECTION_DATE, DENISTED, DEDUPE_GROUP_ID, PROCESSING_STATUS.
    /// </summary>
    public static ColumnProfile LegacyWithCollectionMetadata => GetShared("legacywithcollectionmetadata");

    /// <summary>
    /// Gets a built-in profile by name.
    /// </summary>
    public static ColumnProfile? GetProfile(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = name.ToLowerInvariant();
        return key is "minimal" or "standard" or "litigation" or "full" or "legacywithmetadata" or "legacyeml"
            ? Profiles.Value[key].Clone()
            : null;
    }

    /// <summary>
    /// Creates a merged profile combining the base profile's columns with additional columns.
    /// Data sources from the additional profile are merged into the base.
    /// </summary>
    public static ColumnProfile MergeWithCollectionMetadata(ColumnProfile baseProfile)
    {
        ArgumentNullException.ThrowIfNull(baseProfile);
        var merged = new ColumnProfile
        {
            Name = baseProfile.Name,
            Description = baseProfile.Description,
            Version = baseProfile.Version,
            FieldNamingConvention = baseProfile.FieldNamingConvention,
            Settings = baseProfile.Settings,
            DataSources = new Dictionary<string, DataSourceConfig>(baseProfile.DataSources, StringComparer.Ordinal),
            Columns = new List<ColumnDefinition>(baseProfile.Columns),
        };

        foreach (var ds in LegacyWithCollectionMetadata.DataSources)
        {
            if (!merged.DataSources.ContainsKey(ds.Key))
            {
                merged.DataSources[ds.Key] = ds.Value;
            }
        }

        foreach (var col in LegacyWithCollectionMetadata.Columns)
        {
            if (!merged.Columns.Any(c => string.Equals(c.Name, col.Name, StringComparison.Ordinal)))
            {
                merged.Columns.Add(col);
            }
        }

        return merged;
    }

    private static ColumnProfile GetShared(string name) => Profiles.Value[name];

    private static IReadOnlyDictionary<string, ColumnProfile> LoadProfiles()
    {
        var assembly = typeof(BuiltInProfiles).Assembly;
        var profiles = new Dictionary<string, ColumnProfile>(StringComparer.Ordinal);
        foreach (var name in ResourceNames)
        {
            var resourceName = $"Zipper.Profiles.BuiltIns.{name}.json";
            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded column profile resource '{resourceName}' is missing.");
            ColumnProfile? profile;
            try
            {
                profile = JsonSerializer.Deserialize<ColumnProfile>(stream, ColumnProfileLoader.ProfileSerializerOptions);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Embedded column profile resource '{resourceName}' failed to parse: {ex.Message}", ex);
            }

            if (profile is null)
            {
                throw new InvalidOperationException($"Embedded column profile resource '{resourceName}' failed to parse.");
            }

            NormalizeGeneratorParams(profile);
            profiles[name] = profile;
        }

        return profiles;
    }

    /// <summary>
    /// Converts JsonElement generator parameter values produced by deserialization back to the
    /// primitives a C# object literal would hold (int/double/bool/string), so generator consumers
    /// see identical types for built-in and in-code profiles.
    /// </summary>
    private static void NormalizeGeneratorParams(ColumnProfile profile)
    {
        foreach (var column in profile.Columns)
        {
            if (column.GeneratorParams is null)
            {
                continue;
            }

            foreach (var key in column.GeneratorParams.Keys.ToList())
            {
                if (column.GeneratorParams[key] is JsonElement element)
                {
                    column.GeneratorParams[key] = element.ValueKind switch
                    {
                        JsonValueKind.Number when element.TryGetInt32(out var intValue) => intValue,
                        JsonValueKind.Number => element.GetDouble(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String => element.GetString()!,
                        _ => element,
                    };
                }
            }
        }
    }
}
