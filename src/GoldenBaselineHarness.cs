using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zipper;

/// <summary>
/// Seeded golden-baseline harness for load-file / audit-file / production-set writers.
/// Produces a timestamp/filename-independent SHA-256 manifest that enables byte-parity
/// checks after refactors.
/// </summary>
public static class GoldenBaselineHarness
{
    private static readonly HashSet<string> TransientJsonProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "productionDate",
        "production_date",
        "generationTime",
        "generation_time",
        "dateGenerated",
        "date_generated",
        "timestamp",
    };

    private static readonly string[] TransientJsonArtifactNames = new[]
    {
        "_manifest.json",
        "_validation_report.json",
    };

    private const string TransientJsonPropertiesSuffix = "_properties.json";

    private static readonly byte[] NormalizedValueBytes = Encoding.UTF8.GetBytes("\"__normalized__\"");

    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    /// <summary>
    /// Generates a deterministic SHA-256 manifest for every artifact in <paramref name="rootDir"/>,
    /// normalizing transient timestamp and wall-clock execution values in JSON artifacts in-place
    /// while preserving all other original JSON formatting, key ordering, indentation, and non-JSON bytes.
    /// </summary>
    /// <param name="rootDir">The root directory produced by a Zipper run.</param>
    /// <returns>A manifest string of <c>sha256  relative-path</c> lines, path-sorted.</returns>
    public static string GenerateTimestampNormalizedSha256Manifest(string rootDir)
    {
        return GenerateManifestCore(rootDir, normalizeTimestamps: true);
    }

    /// <summary>
    /// Generates a SHA-256 manifest for every artifact in <paramref name="rootDir"/>
    /// without normalizing timestamps or transient fields.
    /// </summary>
    /// <param name="rootDir">The root directory produced by a Zipper run.</param>
    /// <returns>A manifest string of <c>sha256  relative-path</c> lines, path-sorted.</returns>
    public static string GenerateRawSha256Manifest(string rootDir)
    {
        return GenerateManifestCore(rootDir, normalizeTimestamps: false);
    }

    private static string GenerateManifestCore(string rootDir, bool normalizeTimestamps)
    {
        ArgumentNullException.ThrowIfNull(rootDir);

        var entries = new List<(string Key, string Line)>();

        foreach (var fullPath in Directory.EnumerateFiles(rootDir, "*", SearchOption.AllDirectories))
        {
            var relPath = NormalizeRelativePath(rootDir, fullPath);
            if (normalizeTimestamps && IsArchive(fullPath))
            {
                CollectArchiveEntries(fullPath, relPath, normalizeTimestamps, entries);
            }
            else
            {
                AddFileEntry(fullPath, relPath, normalizeTimestamps, entries);
            }
        }

        entries.Sort((a, b) => StringComparer.Ordinal.Compare(a.Key, b.Key));
        return string.Join('\n', entries.Select(e => e.Line));
    }

    private static void AddFileEntry(
        string fullPath,
        string relPath,
        bool normalizeTimestamps,
        List<(string Key, string Line)> entries)
    {
        using var stream = File.OpenRead(fullPath);
        var hashHex = ComputeHash(relPath, stream, normalizeTimestamps);
        entries.Add((relPath, $"{hashHex}  {relPath}"));
    }

    private static string NormalizeRelativePath(string rootDir, string fullPath)
    {
        var relPath = Path.GetRelativePath(rootDir, fullPath).Replace('\\', '/');
        return relPath.StartsWith("./", StringComparison.Ordinal) ? relPath[2..] : relPath;
    }

    private static bool IsArchive(string fullPath)
    {
        var ext = Path.GetExtension(fullPath);
        return string.Equals(ext, ".zip", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(ext, ".xlsx", StringComparison.OrdinalIgnoreCase);
    }

    private static void CollectArchiveEntries(
        string fullPath,
        string relPath,
        bool normalizeTimestamps,
        List<(string Key, string Line)> entries)
    {
        try
        {
            var archiveEntries = new List<(string Key, string Line)>();
            using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(fileStream, ZipArchiveMode.Read);
            foreach (var entry in zip.Entries.OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                if (entry.FullName.EndsWith("/", StringComparison.Ordinal))
                {
                    if (normalizeTimestamps)
                    {
                        // Directories have no content bytes to hash; canonicalize by
                        // relative-path-in-archive and full directory name to keep parity stable.
                        var dirKey = $"{relPath}::{entry.FullName}";
                        var payload = Encoding.UTF8.GetBytes($"{relPath}\n{entry.FullName}");
                        archiveEntries.Add((dirKey, $"{HashBytes(payload)}  {dirKey}"));
                    }
                    continue;
                }

                using var entryStream = entry.Open();
                var hashHex = ComputeHash(entry.FullName, entryStream, normalizeTimestamps);
                var entryKey = $"{relPath}::{entry.FullName}";
                archiveEntries.Add((entryKey, $"{hashHex}  {entryKey}"));
            }

            if (archiveEntries.Count == 0)
            {
                // A directory-only archive has no file entries to hash and its raw bytes
                // embed per-entry LastWriteTime (ZipArchive.CreateEntry stamps the current
                // time with 2-second DOS granularity), which would break timestamp
                // normalization. In normalized mode we already emitted canonical entries for
                // directory-only archives above. If none were added, canonicalize by the
                // directory name set; raw mode keeps hashing the bytes as-is.
                if (normalizeTimestamps)
                {
                    var directoryNames = string.Join('\n', zip.Entries.Select(static e => e.FullName).OrderBy(static n => n, StringComparer.Ordinal));
                    var payload = Encoding.UTF8.GetBytes($"{relPath}\n{directoryNames}");
                    entries.Add((relPath, $"{HashBytes(payload)}  {relPath}"));
                }
                else
                {
                    using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    var hashHex = ComputeHash(relPath, fs, normalizeTimestamps: false);
                    entries.Add((relPath, $"{hashHex}  {relPath}"));
                }
            }
            else
            {
                entries.AddRange(archiveEntries);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            AddFileEntry(fullPath, relPath, normalizeTimestamps, entries);
        }
    }

    private static string ComputeHash(string pathOrName, Stream stream, bool normalizeTimestamps)
    {
        if (normalizeTimestamps && pathOrName.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && ShouldNormalizeJsonArtifact(pathOrName))
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            var rawBytes = ms.ToArray();
            var normalizedBytes = NormalizeTransientJsonValues(rawBytes);
            return HashBytes(normalizedBytes);
        }

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string HashBytes(byte[] bytes)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static byte[] NormalizeTransientJsonValues(byte[] jsonBytes)
    {
        try
        {
            var replacements = FindTransientJsonReplacements(jsonBytes);
            return replacements.Count == 0 ? jsonBytes : ApplyReplacements(jsonBytes, replacements);
        }
        catch (JsonException)
        {
            return jsonBytes;
        }
    }

    private static List<(int Start, int Length)> FindTransientJsonReplacements(byte[] jsonBytes)
    {
        int bomOffset = jsonBytes.AsSpan().StartsWith(Utf8Bom) ? 3 : 0;
        var reader = new Utf8JsonReader(jsonBytes.AsSpan(bomOffset));
        var replacements = new List<(int Start, int Length)>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var propName = reader.GetString();
                if (propName is not null && TransientJsonProperties.Contains(propName) && reader.Read())
                {
                    int valueStart = bomOffset + (int)reader.TokenStartIndex;
                    if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    {
                        reader.Skip();
                    }

                    int valueEnd = bomOffset + (int)reader.BytesConsumed;
                    replacements.Add((valueStart, valueEnd - valueStart));
                }
            }
        }

        return replacements;
    }

    private static byte[] ApplyReplacements(byte[] jsonBytes, List<(int Start, int Length)> replacements)
    {
        replacements.Sort((a, b) => a.Start.CompareTo(b.Start));
        using var ms = new MemoryStream();
        int currentOffset = 0;
        foreach (var (start, length) in replacements)
        {
            ms.Write(jsonBytes, currentOffset, start - currentOffset);
            ms.Write(NormalizedValueBytes, 0, NormalizedValueBytes.Length);
            currentOffset = start + length;
        }

        if (currentOffset < jsonBytes.Length)
        {
            ms.Write(jsonBytes, currentOffset, jsonBytes.Length - currentOffset);
        }

        return ms.ToArray();
    }

    private static readonly char[] PathSeparators = ['/', '\\'];

    private static bool ShouldNormalizeJsonArtifact(string pathOrName)
    {
        int lastSlash = pathOrName.LastIndexOfAny(PathSeparators);
        var fileName = lastSlash >= 0 ? pathOrName[(lastSlash + 1)..] : pathOrName;
        foreach (var name in TransientJsonArtifactNames)
        {
            if (fileName.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Load File properties artifacts are named <load-file>_properties.json, so the
        // suffix pattern is required; matching on the file-name segment keeps sibling
        // artifacts such as metadata.json or userdata.json out of the allowlist.
        return fileName.EndsWith(TransientJsonPropertiesSuffix, StringComparison.OrdinalIgnoreCase);
    }
}
