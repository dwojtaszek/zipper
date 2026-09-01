using System.IO.Compression;
using System.Text;

namespace Zipper;

/// <summary>
/// Seeded golden-baseline harness for load-file / audit-file / production-set writers.
/// Produces a timestamp/filename-independent SHA-256 manifest that enables byte-parity
/// checks after refactors.
/// </summary>
public static class GoldenBaselineHarness
{
    /// <summary>
    /// Generates a deterministic SHA-256 manifest for every artifact in <paramref name="rootDir"/>.
    /// </summary>
    /// <param name="rootDir">The root directory produced by a Zipper run.</param>
    /// <returns>A manifest string of <c>sha256  relative-path</c> lines, path-sorted.</returns>
    public static string GenerateTimestampNormalizedSha256Manifest(string rootDir)
    {
        ArgumentNullException.ThrowIfNull(rootDir);

        var entries = new List<(string Key, string Line)>();

        foreach (var fullPath in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(rootDir, fullPath).Replace('\\', '/');
            if (relPath.StartsWith("./", StringComparison.Ordinal))
            {
                relPath = relPath[2..];
            }

            var ext = Path.GetExtension(fullPath);
            if (string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var zip = new ZipArchive(fileStream, ZipArchiveMode.Read);
                    foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
                    {
                        if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        using var stream = entry.Open();
                        using var sha256 = System.Security.Cryptography.SHA256.Create();
                        var hash = sha256.ComputeHash(stream);
                        var hashHex = Convert.ToHexString(hash).ToLowerInvariant();
                        var entryKey = $"{relPath}::{entry.FullName}";
                        entries.Add((entryKey, $"{hashHex}  {entryKey}"));
                    }
                }
                catch
                {
                    // Skip unreadable archives
                }
            }
            else
            {
                using var stream = File.OpenRead(fullPath);
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var hash = sha256.ComputeHash(stream);
                var hashHex = Convert.ToHexString(hash).ToLowerInvariant();
                entries.Add((relPath, $"{hashHex}  {relPath}"));
            }
        }

        entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));

        var sb = new StringBuilder();
        for (var i = 0; i < entries.Count; i++)
        {
            sb.Append(entries[i].Line);
            if (i < entries.Count - 1)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }
}
