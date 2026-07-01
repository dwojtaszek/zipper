
namespace Zipper;

/// <summary>
/// Provides secure path validation to prevent directory traversal attacks.
/// </summary>
public static class PathValidator
{
    /// <summary>
    /// Validates and creates a secure DirectoryInfo, preventing path traversal attacks outside a base directory.
    /// </summary>
    /// <param name="path">The path to validate and create DirectoryInfo from.</param>
    /// <param name="baseDirectory">The allowed base directory. Defaults to current directory if null.</param>
    /// <returns>Validated DirectoryInfo if safe, null if path is invalid.</returns>
    public static DirectoryInfo? ResolveSecurePath(string? path, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            Console.Error.WriteLine("Error: Output path cannot be null or empty.");
            return null;
        }

        try
        {
            // Let the OS resolve the full canonical path, resolving any ".." or "." components
            string fullPath = GetRealPath(path);

            if (baseDirectory is not null)
            {
                // Determine the base directory to restrict access to
                string restrictToBase = GetRealPath(baseDirectory);

                // Ensure the resolved canonical path starts with the allowed base directory
                // We add a trailing separator to ensure /var/www doesn't allow /var/www-backup
                string fullPathWithTrailing = fullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string baseDirWithTrailing = restrictToBase.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                if (!fullPathWithTrailing.StartsWith(baseDirWithTrailing, comparison))
                {
                    Console.Error.WriteLine($"Error: Path traversal detected. Destination '{fullPath}' is outside the allowed base directory.");
                    return null;
                }
            }

            // Create and return the DirectoryInfo (Path.GetFullPath inherently prevents directory traversal
            // outside of the resolved canonical root, returning a valid, normalized absolute path).
            return new DirectoryInfo(fullPath);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: Invalid path format. {ex.Message}");
            return null;
        }
        catch (PathTooLongException ex)
        {
            Console.Error.WriteLine($"Error: Path too long. {ex.Message}");
            return null;
        }
        catch (NotSupportedException ex)
        {
            Console.Error.WriteLine($"Error: Path format not supported. {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Unable to validate path. {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Validates if a path is safe (without creating DirectoryInfo or writing to console).
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <param name="baseDirectory">The allowed base directory. Defaults to current directory if null.</param>
    /// <returns>True if path is safe, false otherwise.</returns>
    public static bool IsPathSafe(string path, string? baseDirectory = null)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = GetRealPath(path);

            if (baseDirectory is not null)
            {
                string restrictToBase = GetRealPath(baseDirectory);
                string fullPathWithTrailing = fullPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string baseDirWithTrailing = restrictToBase.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                StringComparison comparison = OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                return fullPathWithTrailing.StartsWith(baseDirWithTrailing, comparison);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetRealPath(string path)
    {
        return GetRealPathInternal(path, 0);
    }

    private static string GetRealPathInternal(string path, int depth)
    {
        if (depth > 32)
        {
            throw new System.IO.IOException("Too many levels of symbolic links (possible circular reference).");
        }

        string fullPath = Path.GetFullPath(path);
        var linkResult = ResolveLinkTargetWithSuffix(fullPath);

        if (linkResult.HasValue)
        {
            string rebuilt = RebuildPathFromParts(linkResult.Value.resolvedRootPath, linkResult.Value.suffixParts);
            return GetRealPathInternal(rebuilt, depth + 1);
        }

        return fullPath;
    }

    private static (string resolvedRootPath, System.Collections.Generic.IEnumerable<string> suffixParts)? ResolveLinkTargetWithSuffix(string fullPath)
    {
        DirectoryInfo? current = new DirectoryInfo(fullPath);
        var parts = new System.Collections.Generic.List<string>();

        while (current is not null)
        {
            if (current.Exists && current.LinkTarget is not null)
            {
                var resolved = current.ResolveLinkTarget(true);
                if (resolved is not null)
                {
                    parts.Reverse();
                    return (resolved.FullName, parts);
                }
            }
            parts.Add(current.Name);
            current = current.Parent;
        }

        return null;
    }

    private static string RebuildPathFromParts(string basePath, System.Collections.Generic.IEnumerable<string> suffixParts)
    {
        string resolvedPath = basePath;
        foreach (var p in suffixParts)
        {
            if (!string.IsNullOrEmpty(p))
            {
                resolvedPath = Path.Combine(resolvedPath, p);
            }
        }
        return resolvedPath;
    }
}
