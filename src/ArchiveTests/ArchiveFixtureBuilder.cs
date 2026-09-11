using System.Buffers.Binary;
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
    IReadOnlyList<ArchiveFixtureEntryExpectation> Entries,
    IReadOnlyList<ArchiveTestMutation> Mutations);

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

        // Malformed cases start from their named valid control at the same Seed, then apply
        // the recorded mutation; the artifact keeps the control's layout and entry expectations
        // (before-mutation coordinates) plus the mutation records.
        if (definition.Mutation is { } mutation && definition.ControlCaseKey is { } controlCaseKey)
        {
            var controlDefinition = ArchiveTestCatalog.GetCase(controlCaseKey);
            if (controlDefinition.IsMutation)
            {
                throw new InvalidOperationException(
                    $"Archive Test case '{definition.CaseKey}': its control '{controlCaseKey}' is itself a mutation case; controls must be valid cases.");
            }

            var control = Build(controlDefinition, seed, cancellationToken);
            var mutated = ArchiveFixtureMutator.Apply(mutation, control);
            return control with
            {
                ArchiveBytes = mutated.ArchiveBytes,
                ArchiveSha256 = mutated.ArchiveSha256,
                Mutations = mutated.Mutations,
            };
        }

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

        // Baseline enrichment (valid construction, never a defect): applied after the
        // standard writer finalized the Archive, before layout capture, so the captured
        // layout reflects the enriched bytes.
        if (definition.Enrichment is ArchiveControlEnrichment.LocalExtraField)
        {
            archiveBytes = EnrichLocalExtraField(archiveBytes, definition.CaseKey);
        }

        if (archiveBytes.Length > ArchiveTestCaseSemantics.MaxArchivePhysicalBytes)
        {
            throw new InvalidDataException($"Archive Test case '{definition.CaseKey}' Archive is {archiveBytes.Length} bytes, over the physical budget.");
        }

        var layout = ArchiveFixtureLayout.Read(archiveBytes);
        return new ArchiveFixtureArtifact(
            ArchiveBytes: archiveBytes,
            ArchiveSha256: Convert.ToHexStringLower(SHA256.HashData(archiveBytes)),
            Layout: layout,
            Entries: expectations,
            Mutations: []);
    }

    /// <summary>
    /// Baseline construction for the valid-extra-field control: inserts an 8-byte
    /// local-header extra subfield (subfield ID 0x9999, 4 data bytes) after entry 0's
    /// local name. The linked mechanical changes of the baseline — recorded here,
    /// separately from any intentional mutation — are entry 0's local extra-length
    /// (0 → 8), every later entry's central relative-local-header offset (+8), and the
    /// EOCD central-directory offset (+8; the central directory's own size is unchanged
    /// because the extra field lives in the local header). The central directory keeps its
    /// own empty extra area: local and central extra fields may legitimately differ.
    /// The result is re-read by <see cref="ArchiveFixtureLayout.Read"/> and by tests
    /// through the reference reader with matching content hashes.
    /// </summary>
    private static byte[] EnrichLocalExtraField(byte[] archiveBytes, string caseKey)
    {
        const int ExtraLength = 8;
        ReadOnlySpan<byte> extra = [0x99, 0x99, 0x04, 0x00, 0xaa, 0xbb, 0xcc, 0xdd];

        var baseLayout = ArchiveFixtureLayout.Read(archiveBytes);
        if (baseLayout.Entries.Count == 0 || baseLayout.Entries[0].LocalExtraLength != 0 || baseLayout.Entries[0].CentralExtraLength != 0)
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': the extra-field baseline expects at least one entry and no pre-existing extra fields.");
        }

        var first = baseLayout.Entries[0];
        var insertAt = checked(first.LocalHeaderOffset + 30 + first.LocalNameLength);

        var enriched = new byte[checked(archiveBytes.Length + ExtraLength)];
        archiveBytes.AsSpan()[..(int)insertAt].CopyTo(enriched);
        extra.CopyTo(enriched.AsSpan((int)insertAt));
        archiveBytes.AsSpan((int)insertAt).CopyTo(enriched.AsSpan((int)insertAt + ExtraLength));

        // Linked change 1: entry 0's local extra-length, 0 → 8.
        BinaryPrimitives.WriteUInt16LittleEndian(enriched.AsSpan((int)first.LocalHeaderOffset + 28, 2), ExtraLength);

        // Linked change 2: later entries' central relative-local-header offsets, +8. The
        // central headers themselves have shifted +8 in the enriched bytes.
        foreach (var entry in baseLayout.Entries)
        {
            if (entry.Ordinal == 0)
            {
                continue;
            }

            var centralRelativeOffset = (int)entry.CentralDirectoryOffset + ExtraLength + 42;
            var relative = BinaryPrimitives.ReadUInt32LittleEndian(enriched.AsSpan(centralRelativeOffset, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(enriched.AsSpan(centralRelativeOffset, 4), relative + ExtraLength);
        }

        // Linked change 3: the EOCD central-directory offset shifts +8 (the central
        // directory bytes themselves are unchanged, so its size does not grow). The EOCD
        // record itself has shifted by the inserted length in the enriched bytes.
        var eocdOffset = (int)baseLayout.EocdOffset + ExtraLength;
        var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(enriched.AsSpan(eocdOffset + 16, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(enriched.AsSpan(eocdOffset + 16, 4), centralDirectoryOffset + ExtraLength);

        return enriched;
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
