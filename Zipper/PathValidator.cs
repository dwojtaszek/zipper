using System;
using System.IO;

namespace Zipper
{
    /// <summary>
    /// Provides secure path validation to prevent directory traversal attacks.
    /// </summary>
    public static class PathValidator
    {
        /// <summary>
        /// Validates and creates a secure DirectoryInfo, preventing path traversal attacks.
        /// </summary>
        /// <param name="path">The path to validate and create DirectoryInfo from</param>
        /// <returns>Validated DirectoryInfo if safe, null if path is invalid</returns>
        public static DirectoryInfo? ValidateAndCreateDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                Console.Error.WriteLine("Error: Output path cannot be null or empty.");
                return null;
            }

            // Check for potentially problematic characters that could cause issues across platforms
            char[] problematicChars = { '<', '>', ':', '"', '|', '?', '*' };
            foreach (char problematicChar in problematicChars)
            {
                if (path.Contains(problematicChar))
                {
                    Console.Error.WriteLine($"Error: Invalid character '{problematicChar}' in path. These characters are not allowed in directory names.");
                    return null;
                }
            }

            try
            {
                // Normalize the path to resolve any ".." or "." components
                string fullPath = Path.GetFullPath(path);

                // Check for directory traversal patterns
                string normalizedPath = path.Replace('\\', '/');
                if (normalizedPath.Contains(".."))
                {
                    Console.Error.WriteLine("Error: Path traversal detected. Relative paths with '..' are not allowed.");
                    return null;
                }

                // Check for platform-specific invalid path characters
                char[] invalidChars = Path.GetInvalidPathChars();
                foreach (char invalidChar in invalidChars)
                {
                    if (path.Contains(invalidChar))
                    {
                        Console.Error.WriteLine($"Error: Invalid character '{invalidChar}' in path.");
                        return null;
                    }
                }

                // Additional check for absolute path traversal attempts
                if (path.StartsWith("..") || path.Contains("/../") || path.Contains("\\..\\"))
                {
                    Console.Error.WriteLine("Error: Path traversal detected. Paths containing '..' sequences are not allowed.");
                    return null;
                }

                // Create and return the DirectoryInfo
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
        /// Validates if a path is safe (without creating DirectoryInfo).
        /// </summary>
        /// <param name="path">The path to validate</param>
        /// <returns>True if path is safe, false otherwise</returns>
        public static bool IsPathSafe(string path)
        {
            return ValidateAndCreateDirectory(path) != null;
        }
    }
}