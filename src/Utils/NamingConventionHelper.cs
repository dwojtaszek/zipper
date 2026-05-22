using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace Zipper.Utils;

/// <summary>
/// Utility for applying field naming conventions (UPPERCASE, PascalCase, lowercase, snake_case).
/// </summary>
internal static class NamingConventionHelper
{
    private static readonly ConcurrentDictionary<(string Name, string? Convention), string> Cache = new();

    /// <summary>
    /// Applies the specified naming convention to a field name.
    /// </summary>
    /// <param name="name">The original field name.</param>
    /// <param name="convention">The convention to apply (UPPERCASE, PascalCase, lowercase, snake_case).</param>
    /// <returns>The transformed field name.</returns>
    public static string ApplyConvention(string name, string? convention)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var normalizedConvention = convention?.Trim().ToUpperInvariant();

        return Cache.GetOrAdd((name, normalizedConvention), key =>
            key.Convention switch
            {
                "LOWERCASE" => key.Name.ToLowerInvariant(),
                "UPPERCASE" => key.Name.ToUpperInvariant(),
                "PASCALCASE" => ToPascalCase(key.Name),
                "SNAKE_CASE" => ToSnakeCase(key.Name),
                _ => key.Name, // Default to preserving original casing if no valid convention is specified
            });
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        // First, normalize by inserting spaces before uppercase letters (if not already there)
        // e.g., "DocID" -> "Doc ID", "CustodianName" -> "Custodian Name"
        var normalized = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1 $2");
        normalized = Regex.Replace(normalized, @"([A-Z])([A-Z][a-z])", "$1 $2");

        var words = Regex.Split(normalized, @"[\s_-]+");
        var sb = new StringBuilder();
        foreach (var word in words)
        {
            if (word.Length > 0)
            {
                sb.Append(char.ToUpperInvariant(word[0]));
                if (word.Length > 1)
                {
                    sb.Append(word.Substring(1).ToLowerInvariant());
                }
            }
        }

        return sb.ToString();
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        // Handle "DocID" -> "doc_id", "CustodianName" -> "custodian_name"
        var result = Regex.Replace(name, @"([a-z0-9])([A-Z])", "$1_$2");
        result = Regex.Replace(result, @"([A-Z])([A-Z][a-z])", "$1_$2");

        // Convert to lowercase and collapse consecutive spaces, hyphens, and underscores into a single underscore
        return Regex.Replace(result.ToLowerInvariant(), @"[\s_-]+", "_");
    }
}
