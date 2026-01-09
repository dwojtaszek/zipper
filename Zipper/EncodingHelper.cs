using System.Text;

namespace Zipper;

/// <summary>
/// Provides centralized encoding resolution for the application
/// </summary>
internal static class EncodingHelper
{
    /// <summary>
    /// Gets an Encoding instance from an encoding name
    /// </summary>
    /// <param name="encodingName">Name of the encoding (e.g., "UTF-8", "ANSI", "UTF-16")</param>
    /// <returns>Encoding instance or null if not found</returns>
    public static Encoding? GetEncoding(string? encodingName)
    {
        return encodingName?.ToUpperInvariant() switch
        {
            "UTF-8" => Encoding.UTF8,
            "ANSI" => CodePagesEncodingProvider.Instance.GetEncoding(1252),
            "UTF-16" => Encoding.Unicode,
            "UNICODE" => Encoding.Unicode,
            "WESTERN EUROPEAN (WINDOWS)" => CodePagesEncodingProvider.Instance.GetEncoding(1252),
            _ => null
        };
    }

    /// <summary>
    /// Gets an Encoding instance from an encoding name with a default fallback
    /// </summary>
    /// <param name="encodingName">Name of the encoding</param>
    /// <returns>Encoding instance (defaults to UTF-8 if not found)</returns>
    public static Encoding GetEncodingOrDefault(string? encodingName)
    {
        return GetEncoding(encodingName) ?? Encoding.UTF8;
    }
}
