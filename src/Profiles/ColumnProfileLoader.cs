using System.Globalization;
using System.Text.Json;

namespace Zipper.Profiles;

/// <summary>
/// Loads and validates column profiles from built-in resources or custom files.
/// </summary>
public static class ColumnProfileLoader
{
    private const int MaxColumns = 200;

    private static readonly JsonSerializerOptions ProfileSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Loads a column profile by name or file path.
    /// </summary>
    /// <param name="nameOrPath">Built-in profile name or path to custom JSON file.</param>
    /// <returns>The loaded profile, or null if not found.</returns>
    public static ColumnProfile? Load(string nameOrPath)
    {
        // Check if it's a built-in profile name
        var builtIn = BuiltInProfiles.GetProfile(nameOrPath);
        if (builtIn is not null)
        {
            return builtIn;
        }

        // Check if it's a file path
        if (File.Exists(nameOrPath))
        {
            return LoadFromFile(nameOrPath);
        }

        return null;
    }

    /// <summary>
    /// Loads a column profile from a JSON file.
    /// </summary>
    /// <param name="filePath">Path to the JSON profile file.</param>
    /// <returns>The loaded profile.</returns>
    /// <exception cref="InvalidOperationException">If the file cannot be parsed or validated.</exception>
    public static ColumnProfile LoadFromFile(string filePath)
    {
        try
        {
            var json = File.ReadAllText(filePath);
            var profile = JsonSerializer.Deserialize<ColumnProfile>(json, ProfileSerializerOptions);

            if (profile is null)
            {
                throw new InvalidOperationException($"Failed to parse column profile from '{filePath}'.");
            }

            Validate(profile);
            return profile;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid JSON in column profile '{filePath}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Validates a column profile.
    /// </summary>
    /// <param name="profile">The profile to validate.</param>
    /// <exception cref="InvalidOperationException">If validation fails.</exception>
    public static void Validate(ColumnProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("Column profile must have a name.");
        }

        if (profile.FieldNamingConvention is not null)
        {
            var convention = profile.FieldNamingConvention.Trim().ToUpperInvariant();
            if (convention is not ("UPPERCASE" or "PASCALCASE" or "LOWERCASE" or "SNAKE_CASE"))
            {
                throw new InvalidOperationException(
                    $"Column profile '{profile.Name}' has an invalid fieldNamingConvention '{profile.FieldNamingConvention}'. " +
                    "Valid conventions are: UPPERCASE, PascalCase, lowercase, snake_case.");
            }
        }

        // Null guards
        if (profile.Columns is null || profile.Columns.Count is 0)
        {
            throw new InvalidOperationException("Column profile must have at least one column.");
        }

        if (profile.DataSources is null)
        {
            throw new InvalidOperationException("Column profile must have a DataSources dictionary (can be empty).");
        }

        if (profile.Columns.Count > MaxColumns)
        {
            throw new InvalidOperationException($"Column profile exceeds maximum of {MaxColumns} columns (has {profile.Columns.Count}).");
        }

        // Validate unique, non-empty column names
        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var column in profile.Columns)
        {
            if (string.IsNullOrWhiteSpace(column.Name))
            {
                throw new InvalidOperationException("Column profile contains a column with null or empty name.");
            }

            if (!columnNames.Add(column.Name))
            {
                throw new InvalidOperationException($"Duplicate column name '{column.Name}' found in profile.");
            }
        }

        // Validate that at least one identifier column exists
        var hasIdentifier = profile.Columns.Any(c => c.Type.Equals("identifier", StringComparison.OrdinalIgnoreCase));
        if (!hasIdentifier)
        {
            throw new InvalidOperationException("Column profile must have at least one 'identifier' type column.");
        }

        // Validate data source references
        foreach (var column in profile.Columns.Where(c => !string.IsNullOrEmpty(c.DataSource)))
        {
            if (!profile.DataSources.ContainsKey(column.DataSource!))
            {
                throw new InvalidOperationException($"Column '{column.Name}' references undefined data source '{column.DataSource}'.");
            }
        }

        // Validate column types
        foreach (var column in profile.Columns)
        {
            if (!Generation.ColumnValueGeneratorRegistry.IsKnownType(column.Type))
            {
                throw new InvalidOperationException($"Column '{column.Name}' has invalid type '{column.Type}'. Valid types: {string.Join(", ", Generation.ColumnValueGeneratorRegistry.KnownTypes)}");
            }
        }

        // Validate date ranges
        foreach (var column in profile.Columns.Where(c => c.DateRange is not null))
        {
            if (!DateTime.TryParse(column.DateRange!.Min, CultureInfo.InvariantCulture, DateTimeStyles.None, out var minDate))
            {
                throw new InvalidOperationException($"Column '{column.Name}' has invalid DateRange.Min '{column.DateRange.Min}'. Must be a valid date string.");
            }

            if (!DateTime.TryParse(column.DateRange.Max, CultureInfo.InvariantCulture, DateTimeStyles.None, out var maxDate))
            {
                throw new InvalidOperationException($"Column '{column.Name}' has invalid DateRange.Max '{column.DateRange.Max}'. Must be a valid date string.");
            }

            if (minDate > maxDate)
            {
                throw new InvalidOperationException($"Column '{column.Name}' has DateRange.Min ({column.DateRange.Min}) greater than DateRange.Max ({column.DateRange.Max}).");
            }
        }

        // Validate empty percentages
        foreach (var column in profile.Columns.Where(c => c.EmptyPercentage.HasValue))
        {
            if (column.EmptyPercentage < 0 || column.EmptyPercentage > 100)
            {
                throw new InvalidOperationException($"Column '{column.Name}' has invalid emptyPercentage {column.EmptyPercentage}. Must be 0-100.");
            }
        }

        // Validate boolean true percentages
        foreach (var column in profile.Columns.Where(c => c.Type.Equals("boolean", StringComparison.OrdinalIgnoreCase)))
        {
            if (column.TruePercentage.HasValue && (column.TruePercentage < 0 || column.TruePercentage > 100))
            {
                throw new InvalidOperationException($"Column '{column.Name}' has invalid truePercentage {column.TruePercentage}. Must be 0-100.");
            }
        }

        // Validate data source weights
        foreach (var (name, cfg) in profile.DataSources)
        {
            if (cfg.Weights is not null && cfg.Weights.Count > 0)
            {
                var valCount = (cfg.Values is not null && cfg.Values.Count > 0) ? cfg.Values.Count : cfg.Count;
                if (cfg.Weights.Count > valCount)
                {
                    throw new InvalidOperationException(
                        $"Data source '{name}' has an invalid configuration: it has more weights than values " +
                        $"({cfg.Weights.Count} weights specified, but only {valCount} values exist).");
                }
            }
        }
    }

    /// <summary>
    /// Checks if a name corresponds to a built-in profile.
    /// </summary>
    /// <param name="name">The profile name to check.</param>
    /// <returns>True if it's a built-in profile name.</returns>
    public static bool IsBuiltInProfile(string name)
    {
        return BuiltInProfiles.ProfileNames.Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
