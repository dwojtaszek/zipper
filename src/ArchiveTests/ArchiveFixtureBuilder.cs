using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Zipper.ArchiveTests;

/// <summary>
/// One built entry: the recipe's expected content bytes and hash, returned from the
/// recipe itself — the oracle is not derived by re-reading the writer's result.
/// </summary>
internal sealed record ArchiveFixtureEntryExpectation(
    int Ordinal,
    string Name,
    bool IsDirectory,
    ushort Method,
    byte[] Content,
    string ContentSha256);

internal sealed record ArchiveFixtureArtifact(
    byte[] ArchiveBytes,
    string ArchiveSha256,
    ArchiveFixtureLayout Layout,
    IReadOnlyList<ArchiveFixtureEntryExpectation> Entries);

/// <summary>
/// Builds the valid baseline (control) Archives for Archive Test cases using the
/// standard <see cref="ZipArchive"/> library only (never the Standard-mode pipeline),
/// then captures the exact structure layout. Budgets (REQ-213) are enforced with
/// checked arithmetic during writes, one artifact at a time. No filesystem output.
/// </summary>
internal static class ArchiveFixtureBuilder
{
    private static readonly DateTimeOffset FixedDosTimestamp = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static ArchiveFixtureArtifact BuildControl(string caseKey, int seed, CancellationToken cancellationToken) =>
        Build(ArchiveTestCatalog.GetCase(caseKey), seed, cancellationToken);

    internal static ArchiveFixtureArtifact Build(ArchiveTestCaseDefinition definition, int seed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();

        var expectations = new List<ArchiveFixtureEntryExpectation>(definition.Recipe.Entries.Count);
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var ordinal = 0;
            long expandedBytes = 0;
            foreach (var recipeEntry in definition.Recipe.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ordinal >= ArchiveTestCaseSemantics.MaxEntries)
                {
                    throw new InvalidDataException($"Archive Test case '{definition.CaseKey}' exceeds the {ArchiveTestCaseSemantics.MaxEntries}-entry budget.");
                }

                ValidatePathDepth(definition.CaseKey, recipeEntry.Name);

                var content = GeneratePayload(definition.CaseKey, seed, recipeEntry);
                expandedBytes = checked(expandedBytes + content.Length);
                if (expandedBytes > ArchiveTestCaseSemantics.MaxExpandedBytesBudget)
                {
                    throw new InvalidDataException(
                        $"Archive Test case '{definition.CaseKey}' exceeds the {ArchiveTestCaseSemantics.MaxExpandedBytesBudget}-byte expanded budget.");
                }

                if (stream.Length + 30L + Encoding.UTF8.GetByteCount(recipeEntry.Name) + content.Length > ArchiveTestCaseSemantics.MaxArchivePhysicalBytes)
                {
                    throw new InvalidDataException(
                        $"Archive Test case '{definition.CaseKey}' exceeds the {ArchiveTestCaseSemantics.MaxArchivePhysicalBytes}-byte physical Archive budget.");
                }

                var level = recipeEntry.Method == "stored" ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                var entry = archive.CreateEntry(recipeEntry.Name, level);
                entry.LastWriteTime = FixedDosTimestamp;
                using (var entryStream = entry.Open())
                {
                    entryStream.Write(content);
                }

                expectations.Add(new ArchiveFixtureEntryExpectation(
                    Ordinal: ordinal,
                    Name: recipeEntry.Name,
                    IsDirectory: recipeEntry.IsDirectory,
                    Method: recipeEntry.Method == "stored" ? (ushort)0 : (ushort)8,
                    Content: content,
                    ContentSha256: Convert.ToHexStringLower(SHA256.HashData(content))));

                ordinal++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var archiveBytes = stream.ToArray();
        if (archiveBytes.Length > ArchiveTestCaseSemantics.MaxArchivePhysicalBytes)
        {
            throw new InvalidDataException($"Archive Test case '{definition.CaseKey}' Archive is {archiveBytes.Length} bytes, over the physical budget.");
        }

        var layout = ArchiveFixtureLayout.Read(archiveBytes);
        return new ArchiveFixtureArtifact(
            ArchiveBytes: archiveBytes,
            ArchiveSha256: Convert.ToHexStringLower(SHA256.HashData(archiveBytes)),
            Layout: layout,
            Entries: expectations);
    }

    /// <summary>
    /// Deterministic seed-varying payload from an explicit stable recipe: SHA-256 chain
    /// over (caseKey, seed, entry name, block counter). No shared mutable Random state
    /// and no dependence on suite selection or generation order.
    /// </summary>
    private static byte[] GeneratePayload(string caseKey, int seed, ArchiveTestRecipeEntry entry)
    {
        if (entry.IsDirectory)
        {
            return [];
        }

        var bytes = new byte[entry.Length];
        var filled = 0;
        var counter = 0;
        while (filled < entry.Length)
        {
            var block = SHA256.HashData([
                .. Encoding.UTF8.GetBytes(caseKey),
                0x0a,
                .. Encoding.ASCII.GetBytes(seed.ToString(CultureInfo.InvariantCulture)),
                0x0a,
                .. Encoding.UTF8.GetBytes(entry.Name),
                0x0a,
                .. BitConverter.GetBytes(counter),
            ]);

            if (entry.PayloadPrefix is { Length: > 0 } prefix && filled == 0)
            {
                var prefixLength = Math.Min(prefix.Length, entry.Length);
                prefix.AsSpan()[..prefixLength].CopyTo(bytes);
                filled = prefixLength;
            }
            else
            {
                var copyLength = Math.Min(block.Length, entry.Length - filled);
                block.AsSpan()[..copyLength].CopyTo(bytes.AsSpan(filled));
                filled += copyLength;
            }

            counter = checked(counter + 1);
        }

        return bytes;
    }

    private static void ValidatePathDepth(string caseKey, string name)
    {
        var segments = name.TrimEnd('/').Split('/');
        if (segments.Length > 2)
        {
            throw new InvalidDataException($"Archive Test case '{caseKey}' entry '{name}' exceeds the depth-2 entry path budget.");
        }

        foreach (var segment in segments)
        {
            if (segment.Length == 0 || segment is "." or "..")
            {
                throw new InvalidDataException($"Archive Test case '{caseKey}' entry '{name}' has an empty or relative path segment.");
            }
        }
    }
}
