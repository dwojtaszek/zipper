// <copyright file="ColumnProfileLoader.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text.Json;

namespace Zipper.Profiles;

/// <summary>
/// Loads and validates column profiles from built-in resources or custom files.
/// </summary>
public static class ColumnProfileLoader
{
    private const int MaxColumns = 200;

    /// <summary>
    /// Loads a column profile by name or file path.
    /// </summary>
    /// <param name="nameOrPath">Built-in profile name or path to custom JSON file.</param>
    /// <returns>The loaded profile, or null if not found.</returns>
    public static ColumnProfile? Load(string nameOrPath)
    {
        // Check if it's a built-in profile name
        var builtIn = BuiltInProfiles.GetProfile(nameOrPath);
        if (builtIn != null)
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
            var profile = JsonSerializer.Deserialize<ColumnProfile>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            });

            if (profile == null)
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
        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            throw new InvalidOperationException("Column profile must have a name.");
        }

        // Null guards
        if (profile.Columns == null || profile.Columns.Count == 0)
        {
            throw new InvalidOperationException("Column profile must have at least one column.");
        }

        if (profile.DataSources == null)
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
        var validTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "identifier", "text", "longtext", "date", "datetime", "number", "boolean", "coded", "email",
        };

        foreach (var column in profile.Columns)
        {
            if (!validTypes.Contains(column.Type))
            {
                throw new InvalidOperationException($"Column '{column.Name}' has invalid type '{column.Type}'. Valid types: {string.Join(", ", validTypes)}");
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
