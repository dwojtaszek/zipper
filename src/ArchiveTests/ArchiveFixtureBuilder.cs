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

    /// <summary>
    /// The 64-byte non-Archive prefix for the prefixed cases (ticket #873): ASCII text
    /// carrying no ZIP signatures, so it is inert data, never a structure. Shared with
    /// the unrebased mutation so both cases carry the identical prefix bytes.
    /// </summary>
    internal static byte[] ArchivePrefixStub { get; } = BuildPrefixStub();

    private static byte[] BuildPrefixStub()
    {
        var stub = Encoding.ASCII.GetBytes("# zipper prefixed-archive stub (CD rebased past this line).");
        if (stub.Length > 63)
        {
            throw new InvalidOperationException("The Archive prefix stub exceeds 63 bytes.");
        }

        var padded = new byte[64];
        stub.CopyTo(padded, 0);
        Array.Fill(padded, (byte)'.', stub.Length, 63 - stub.Length);
        padded[63] = (byte)'\n';
        return padded;
    }

    internal static ArchiveFixtureArtifact BuildControl(string caseKey, int seed, CancellationToken cancellationToken) =>
        Build(ArchiveTestCatalog.GetCase(caseKey), seed, cancellationToken);

    internal static ArchiveFixtureArtifact Build(ArchiveTestCaseDefinition definition, int seed, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        cancellationToken.ThrowIfCancellationRequested();

        // Hard budget pre-checks (REQ-213, ticket #843): reject one-over recipes before
        // generating any payload — an oversized recipe never allocates or hashes its
        // content. The uncompressed member length is exact for stored entries and a
        // safe v1 upper bound overall: no catalog case may carry a single member whose
        // expanded content alone exceeds the physical Archive budget (that is the
        // decompression-bomb shape this catalog refuses by scope). The in-loop checks
        // below remain as write-time backstops.
        if (definition.Recipe.Entries.Count > ArchiveTestCaseSemantics.MaxEntries)
        {
            throw new InvalidDataException(
                $"Archive Test case '{definition.CaseKey}' exceeds the {ArchiveTestCaseSemantics.MaxEntries}-entry budget.");
        }

        long totalExpanded = 0;
        foreach (var recipeEntry in definition.Recipe.Entries)
        {
            totalExpanded = checked(totalExpanded + recipeEntry.Length);
        }

        if (totalExpanded > ArchiveTestCaseSemantics.MaxExpandedBytesBudget)
        {
            throw new InvalidDataException(
                $"Archive Test case '{definition.CaseKey}' exceeds the {ArchiveTestCaseSemantics.MaxExpandedBytesBudget}-byte expanded budget.");
        }

        foreach (var recipeEntry in definition.Recipe.Entries)
        {
            if (30L + Encoding.UTF8.GetByteCount(recipeEntry.Name) + recipeEntry.Length
                > ArchiveTestCaseSemantics.MaxArchivePhysicalBytes)
            {
                throw new InvalidDataException(
                    $"Archive Test case '{definition.CaseKey}' exceeds the {ArchiveTestCaseSemantics.MaxArchivePhysicalBytes}-byte physical Archive budget.");
            }
        }

        // Malformed cases start from their named valid control at the same Seed, then apply
        // the ordered mutation chain; the artifact keeps the control's layout and entry
        // expectations (before-mutation coordinates) plus the ordered mutation records.
        if (definition.Mutations is { Count: > 0 } chain && definition.ControlCaseKey is { } controlCaseKey)
        {
            var controlDefinition = ArchiveTestCatalog.GetCase(controlCaseKey);
            if (controlDefinition.IsMutation)
            {
                throw new InvalidOperationException(
                    $"Archive Test case '{definition.CaseKey}': its control '{controlCaseKey}' is itself a mutation case; controls must be valid cases.");
            }

            // The control's own comment expectation carries over to every re-read: a
            // comment-bearing control must not be misread mid-chain.
            var controlExpectedCommentLength = controlDefinition.Construction is ArchiveControlConstruction.ArchiveComment
                ? ArchiveTestCatalog.SignatureCommentLength
                : 0;
            var current = Build(controlDefinition, seed, cancellationToken);
            var records = new List<ArchiveTestMutation>();
            for (var index = 0; index < chain.Count; index++)
            {
                var mutated = ArchiveFixtureMutator.Apply(chain[index], current);
                records.AddRange(mutated.Mutations);
                current = current with
                {
                    ArchiveBytes = mutated.ArchiveBytes,
                    ArchiveSha256 = mutated.ArchiveSha256,
                    Mutations = [],
                };

                // A later mutation still needs field offsets: recompute the layout from
                // the current bytes instead of reusing the control's (now possibly
                // stale) coordinates — never a stale offset. An unreadable intermediate
                // (e.g. a truncation already removed the EOCD) is an invalid recipe
                // sequence and is rejected descriptively.
                if (index + 1 < chain.Count)
                {
                    try
                    {
                        current = current with
                        {
                            Layout = ArchiveFixtureLayout.Read(current.ArchiveBytes, controlExpectedCommentLength),
                        };
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"Archive Test case '{definition.CaseKey}': invalid mutation chain — the intermediate Archive after '{chain[index].ToCaseKey()}' has no readable layout, so '{chain[index + 1].ToCaseKey()}' cannot be applied ({ex.GetType().Name}: {ex.Message}).", ex);
                    }
                }
            }

            return current with { Mutations = records };
        }

        if (definition.Construction is ArchiveControlConstruction.Zip64HandBuilt)
        {
            return BuildHandBuiltZip64(definition, seed, cancellationToken);
        }

        if (definition.Construction is ArchiveControlConstruction.OverlappingEntries)
        {
            return BuildHandBuiltOverlapping(definition, seed, cancellationToken);
        }

        var expectations = new List<ArchiveFixtureEntryExpectation>(definition.Recipe.Entries.Count);
        using var stream = new MemoryStream();
        // Non-seekable write target: the standard writer cannot patch the local header
        // afterwards, so it emits data descriptors with the signature (APPNOTE §4.3.9).
        // The unsigned-descriptor baseline is built the same way first, then stripped.
        Stream writeTarget = definition.Construction is ArchiveControlConstruction.NonSeekableDescriptor
            or ArchiveControlConstruction.StripDescriptorSignature
            ? new NonSeekableMemoryStream(stream)
            : stream;
        using (var archive = new ZipArchive(writeTarget, ZipArchiveMode.Create, leaveOpen: true))
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

                // Policy-sensitive entries (ticket #842) keep their names verbatim: the
                // name bytes are the hazard under test, so the generation-time guard is
                // bypassed for them alone. This builder never materializes member paths;
                // it only writes Archive bytes to memory.
                if (!recipeEntry.IsPolicyName)
                {
                    ValidatePathDepth(definition.CaseKey, recipeEntry.Name);
                }

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
                if (recipeEntry.ExternalAttributes != 0)
                {
                    // Documented Unix mode bits (e.g. S_IFLNK | 0777 for symlink entries);
                    // pure metadata bytes — no filesystem object is ever created.
                    entry.ExternalAttributes = unchecked((int)recipeEntry.ExternalAttributes);
                }
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

        // Baseline construction (valid, never a defect): applied after the standard
        // writer finalized the Archive, before layout capture, so the captured layout
        // reflects the constructed bytes.
        if (definition.Construction is ArchiveControlConstruction.LocalExtraField)
        {
            archiveBytes = EnrichLocalExtraField(archiveBytes, definition.CaseKey);
        }
        else if (definition.Construction is ArchiveControlConstruction.InfoZipUnicodePath)
        {
            archiveBytes = EnrichUnicodePath(archiveBytes, definition.CaseKey);
        }
        else if (definition.Construction is ArchiveControlConstruction.PrefixedArchive)
        {
            archiveBytes = PrependPrefix(archiveBytes, definition.CaseKey);
        }
        else if (definition.Construction is ArchiveControlConstruction.HostileNameNullByte)
        {
            archiveBytes = PatchHostileName(archiveBytes, definition.CaseKey, static name =>
            {
                if (name.Length != 15 || name[10] != (byte)'X')
                {
                    throw new InvalidOperationException("The null-byte baseline expects the 15-byte placeholder name 'report.pdfX.exe' with 'X' at name index 10.");
                }

                name[10] = 0x00;
            });
        }
        else if (definition.Construction is ArchiveControlConstruction.HostileNameControlChars)
        {
            archiveBytes = PatchHostileName(archiveBytes, definition.CaseKey, static name =>
            {
                if (name.Length != 15 || name[0] != (byte)'r' || name[1] != (byte)'e' || name[2] != (byte)'p' || name[10] != (byte)'X')
                {
                    throw new InvalidOperationException("The C0 baseline expects the 15-byte placeholder name 'report.pdfX.exe'.");
                }

                name[0] = 0x01;
                name[1] = 0x02;
                name[2] = 0x03;
            });
        }
        else if (definition.Construction is ArchiveControlConstruction.StripDescriptorSignature)
        {
            archiveBytes = StripDescriptorSignature(archiveBytes, definition.CaseKey);
        }
        else if (definition.Construction is ArchiveControlConstruction.Cp437Name)
        {
            archiveBytes = SwapCp437NameByte(archiveBytes, definition.CaseKey);
        }
        else if (definition.Construction is ArchiveControlConstruction.ArchiveComment)
        {
            archiveBytes = AppendSignatureComment(archiveBytes, definition.CaseKey);
        }

        if (archiveBytes.Length > ArchiveTestCaseSemantics.MaxArchivePhysicalBytes)
        {
            throw new InvalidDataException($"Archive Test case '{definition.CaseKey}' Archive is {archiveBytes.Length} bytes, over the physical budget.");
        }

        var expectedCommentLength = definition.Construction is ArchiveControlConstruction.ArchiveComment
            ? ArchiveTestCatalog.SignatureCommentLength
            : 0;
        var layout = ArchiveFixtureLayout.Read(archiveBytes, expectedCommentLength);
        // Policy-sensitive Unix metadata (ticket #842): the standard writer always
        // records the DOS host; symlink entries additionally declare the Unix host so
        // the attribute type bits are read as Unix mode bits. Only the central
        // version-made-by host byte changes — no captured layout field moves.
        if (definition.Recipe.Entries.Any(e => e.HostSystem != 0))
        {
            PatchUnixHostSystem(archiveBytes, definition.Recipe.Entries, layout);
        }
        if (definition.Construction is ArchiveControlConstruction.Cp437Name)
        {
            // The raw name bytes are CP437: record the legacy-decoded readable name while
            // the hex stays the physical bytes.
            layout = layout with
            {
                Entries = [.. layout.Entries.Select(e => e.Ordinal == 0 ? e with { Name = "café.txt" } : e)],
            };
        }
        return new ArchiveFixtureArtifact(
            ArchiveBytes: archiveBytes,
            ArchiveSha256: Convert.ToHexStringLower(SHA256.HashData(archiveBytes)),
            Layout: layout,
            Entries: expectations,
            Mutations: []);
    }

    /// <summary>
    /// Rewrites the central version-made-by host byte (upper byte at central + 4) to the
    /// recipe's declared host system for entries that carry one (Unix, for symlink
    /// metadata). The version low byte and every other field stay untouched, so the
    /// previously captured layout remains valid without a re-read. Patches the freshly
    /// built buffer in place — no other holder of these bytes exists.
    /// </summary>
    private static void PatchUnixHostSystem(
        byte[] archiveBytes,
        IReadOnlyList<ArchiveTestRecipeEntry> recipeEntries,
        ArchiveFixtureLayout layout)
    {
        for (var ordinal = 0; ordinal < recipeEntries.Count; ordinal++)
        {
            var recipeEntry = recipeEntries[ordinal];
            if (recipeEntry.HostSystem == 0)
            {
                continue;
            }

            var entry = layout.Entries[ordinal];
            var versionMadeBy = BinaryPrimitives.ReadUInt16LittleEndian(
                archiveBytes.AsSpan((int)entry.CentralDirectoryOffset + 4, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(
                archiveBytes.AsSpan((int)entry.CentralDirectoryOffset + 4, 2),
                (ushort)((recipeEntry.HostSystem << 8) | (versionMadeBy & 0xFF)));
        }
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
    /// Baseline construction for the unicode-path-extra-mismatch case (ticket #871):
    /// inserts an Info-ZIP Unicode Path subfield (0x7075, version 1, CRC-32 of the
    /// standard name, discrepant UTF-8 Unicode name) into entry 0's local header and
    /// central header. The local insert shifts every later physical offset by the
    /// subfield length (re-linked like <see cref="EnrichLocalExtraField"/>); the
    /// central insert grows the central directory by the same length. Entry 0's own
    /// relative-local-header offset is unchanged; the EOCD central-directory offset
    /// shifts by one subfield length and its size grows by one subfield length.
    /// </summary>
    private static byte[] EnrichUnicodePath(byte[] archiveBytes, string caseKey)
    {
        var unicodeName = Encoding.UTF8.GetBytes("../../escaped.txt");
        var subfield = new byte[checked(4 + 1 + 4 + unicodeName.Length)];
        BinaryPrimitives.WriteUInt16LittleEndian(subfield, 0x7075);
        BinaryPrimitives.WriteUInt16LittleEndian(subfield.AsSpan(2), checked((ushort)(1 + 4 + unicodeName.Length)));
        subfield[4] = 1;
        var baseLayout = ArchiveFixtureLayout.Read(archiveBytes);
        if (baseLayout.Entries.Count == 0 || baseLayout.Entries[0].LocalExtraLength != 0 || baseLayout.Entries[0].CentralExtraLength != 0)
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': the Unicode Path baseline expects at least one entry and no pre-existing extra fields.");
        }

        var first = baseLayout.Entries[0];
        if (first.Name != "safe.txt")
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': the Unicode Path baseline expects the standard name 'safe.txt'; found '{first.Name}'.");
        }

        BinaryPrimitives.WriteUInt32LittleEndian(subfield.AsSpan(5), Crc32(Convert.FromHexString(first.NameHex)));
        unicodeName.CopyTo(subfield.AsSpan(9));

        var localInsertAt = checked(first.LocalHeaderOffset + 30 + first.LocalNameLength);
        var localEnriched = new byte[checked(archiveBytes.Length + subfield.Length)];
        archiveBytes.AsSpan()[..(int)localInsertAt].CopyTo(localEnriched);
        subfield.CopyTo(localEnriched.AsSpan((int)localInsertAt));
        archiveBytes.AsSpan((int)localInsertAt).CopyTo(localEnriched.AsSpan((int)localInsertAt + subfield.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(localEnriched.AsSpan((int)first.LocalHeaderOffset + 28, 2), checked((ushort)subfield.Length));
        foreach (var entry in baseLayout.Entries)
        {
            if (entry.Ordinal == 0)
            {
                continue;
            }

            var centralRelativeOffset = (int)entry.CentralDirectoryOffset + subfield.Length + 42;
            var relative = BinaryPrimitives.ReadUInt32LittleEndian(localEnriched.AsSpan(centralRelativeOffset, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(localEnriched.AsSpan(centralRelativeOffset, 4), checked(relative + (uint)subfield.Length));
        }

        var localEocdOffset = (int)baseLayout.EocdOffset + subfield.Length;
        var localCentralOffset = BinaryPrimitives.ReadUInt32LittleEndian(localEnriched.AsSpan(localEocdOffset + 16, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(localEnriched.AsSpan(localEocdOffset + 16, 4), checked(localCentralOffset + (uint)subfield.Length));

        var relaidCentralOffset = checked(baseLayout.Entries[0].CentralDirectoryOffset + subfield.Length);
        var relaidEocdOffset = checked(baseLayout.EocdOffset + subfield.Length);
        var centralInsertAt = checked(relaidCentralOffset + 46 + first.LocalNameLength);
        var enriched = new byte[checked(localEnriched.Length + subfield.Length)];
        localEnriched.AsSpan()[..(int)centralInsertAt].CopyTo(enriched);
        subfield.CopyTo(enriched.AsSpan((int)centralInsertAt));
        localEnriched.AsSpan((int)centralInsertAt).CopyTo(enriched.AsSpan((int)centralInsertAt + subfield.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(enriched.AsSpan((int)relaidCentralOffset + 30, 2), checked((ushort)subfield.Length));

        // The central insert lands inside the central directory, after its start: the
        // EOCD shifts by one more subfield length and the central size grows, while
        // the central-directory offset field (already rebased by the local insert)
        // stays put.
        var eocdOffset = (int)relaidEocdOffset + subfield.Length;
        var centralSize = BinaryPrimitives.ReadUInt32LittleEndian(enriched.AsSpan(eocdOffset + 12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(enriched.AsSpan(eocdOffset + 12, 4), checked(centralSize + (uint)subfield.Length));

        return enriched;
    }

    /// <summary>
    /// Baseline construction for the prefix-rebased case (ticket #873): prepends the
    /// 64-byte stub before the control's bytes and rebases every central
    /// relative-local-header offset and the EOCD central-directory offset past it.
    /// Counts and sizes are unchanged; the result stays a readable Archive for
    /// prefix-tolerant readers.
    /// </summary>
    private static byte[] PrependPrefix(byte[] archiveBytes, string caseKey)
    {
        var prefix = ArchivePrefixStub;
        var baseLayout = ArchiveFixtureLayout.Read(archiveBytes);

        // Pinned to a plain (non-Zip64, comment-free) control: Zip64 sentinels
        // would need the Zip64 EOCD locator and extended fields rebased too.
        if (baseLayout.CentralDirectoryOffset == ArchiveFixtureLayout.Zip64SizeSentinel
            || baseLayout.Entries.Any(e => e.LocalHeaderOffset == ArchiveFixtureLayout.Zip64SizeSentinel))
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': the prefix baseline cannot rebase Zip64 sentinel offsets.");
        }

        var prefixed = new byte[checked(archiveBytes.Length + prefix.Length)];
        prefix.CopyTo(prefixed, 0);
        archiveBytes.CopyTo(prefixed, prefix.Length);

        foreach (var entry in baseLayout.Entries)
        {
            var centralRelativeOffset = checked((int)entry.CentralDirectoryOffset + prefix.Length + 42);
            var relative = BinaryPrimitives.ReadUInt32LittleEndian(prefixed.AsSpan(centralRelativeOffset, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(prefixed.AsSpan(centralRelativeOffset, 4), checked(relative + (uint)prefix.Length));
        }

        var eocdOffset = checked((int)baseLayout.EocdOffset + prefix.Length);
        var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(prefixed.AsSpan(eocdOffset + 16, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(prefixed.AsSpan(eocdOffset + 16, 4), checked(centralDirectoryOffset + (uint)prefix.Length));

        return prefixed;
    }

    /// <summary>
    /// Baseline construction for the hostile-name cases (ticket #876): rewrites the
    /// standard header name bytes in place, identically in the local and the central
    /// header, via the .NET writer's placeholder name. Same-length only: no structure
    /// moves, so the captured layout stays valid and the sidecar records the exact
    /// hostile raw bytes. The writer itself would reject these names, which is why
    /// the raw-name path exists.
    /// </summary>
    private static byte[] PatchHostileName(byte[] archiveBytes, string caseKey, Action<byte[]> patch)
    {
        var layout = ArchiveFixtureLayout.Read(archiveBytes);
        if (layout.Entries.Count != 1)
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': the hostile-name baseline expects exactly one entry.");
        }

        var entry = layout.Entries[0];
        if (entry.LocalNameLength != entry.CentralNameLength)
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': the hostile-name baseline expects agreeing local and central name lengths.");
        }

        var name = archiveBytes.AsSpan((int)entry.LocalHeaderOffset + 30, entry.LocalNameLength).ToArray();
        patch(name);
        name.CopyTo(archiveBytes.AsSpan((int)entry.LocalHeaderOffset + 30));
        name.CopyTo(archiveBytes.AsSpan((int)entry.CentralDirectoryOffset + 46));
        return archiveBytes;
    }

    /// <summary>
    /// Baseline construction for the valid-descriptor-no-signature control: removes the
    /// 4-byte optional signature (PK\x07\x08) from the single entry's data descriptor.
    /// The linked mechanical change is the EOCD central-directory offset (-4); the
    /// 32-bit descriptor itself (CRC-32, compressed size, uncompressed size) follows
    /// immediately at the descriptor offset. Both forms are valid (APPNOTE §4.3.9).
    /// </summary>
    private static byte[] StripDescriptorSignature(byte[] archiveBytes, string caseKey)
    {
        var layout = ArchiveFixtureLayout.Read(archiveBytes);
        if (layout.Entries.Count != 1 || !layout.Entries[0].HasDataDescriptor)
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': the strip-signature baseline expects exactly one entry written with a data descriptor.");
        }

        var descriptorOffset = (int)layout.Entries[0].DataDescriptorOffset;
        if (BinaryPrimitives.ReadUInt32LittleEndian(archiveBytes.AsSpan(descriptorOffset, 4)) != 0x08074b50)
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': expected the data descriptor signature 0x08074b50 at offset {descriptorOffset}.");
        }

        var stripped = new byte[archiveBytes.Length - 4];
        archiveBytes.AsSpan()[..descriptorOffset].CopyTo(stripped);
        archiveBytes.AsSpan(descriptorOffset + 4).CopyTo(stripped.AsSpan(descriptorOffset));

        // Linked change: the EOCD (and with it the central directory start) shifts -4;
        // the EOCD's central-directory offset field must reflect the shift.
        var eocdOffset = stripped.Length - 22;
        var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(stripped.AsSpan(eocdOffset + 16, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(stripped.AsSpan(eocdOffset + 16, 4), centralDirectoryOffset - 4);

        return stripped;
    }

    /// <summary>
    /// Baseline construction for the valid-cp437-name control: swaps one ASCII name byte
    /// ('x' at index 3 of the placeholder name) for the legacy CP437 byte 0x82 ("é") in
    /// both the local and the central header. The byte length is unchanged, so no other
    /// structure moves; bit 11 stays clear (the known-raw-bytes approach, never an OS
    /// default code page).
    /// </summary>
    private static byte[] SwapCp437NameByte(byte[] archiveBytes, string caseKey)
    {
        const byte Cp437EAcute = 0x82;
        const int SwapIndex = 3;
        var layout = ArchiveFixtureLayout.Read(archiveBytes);
        if (layout.Entries.Count == 0)
        {
            throw new InvalidOperationException($"Archive Test case '{caseKey}': the CP437 baseline expects at least one entry.");
        }

        var first = layout.Entries[0];
        if (first.LocalNameLength != 8 || first.NameHex != Convert.ToHexStringLower("cafx.txt"u8))
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': the CP437 baseline expects the 8-byte placeholder name 'cafx.txt'; found '{first.Name}'.");
        }

        archiveBytes[(int)first.LocalHeaderOffset + 30 + SwapIndex] = Cp437EAcute;
        archiveBytes[(int)first.CentralDirectoryOffset + 46 + SwapIndex] = Cp437EAcute;
        return archiveBytes;
    }

    /// <summary>
    /// Baseline construction for the valid-signatures-in-comment control: appends the
    /// signature-like comment after the EOCD and sets the EOCD comment-length field.
    /// The comment bytes are pure data; the declared length (never a signature search)
    /// is what locates the EOCD afterwards.
    /// </summary>
    private static byte[] AppendSignatureComment(byte[] archiveBytes, string caseKey)
    {
        var comment = ArchiveTestCatalog.SignatureComment;
        if (comment.Length != ArchiveTestCatalog.SignatureCommentLength)
        {
            throw new InvalidOperationException($"Archive Test case '{caseKey}': the comment bytes and declared length disagree.");
        }

        var eocdOffset = archiveBytes.Length - 22;
        var commented = new byte[checked(archiveBytes.Length + comment.Length)];
        archiveBytes.AsSpan().CopyTo(commented);
        comment.CopyTo(commented.AsSpan(archiveBytes.Length));
        BinaryPrimitives.WriteUInt16LittleEndian(commented.AsSpan(eocdOffset + 20, 2), (ushort)comment.Length);

        return commented;
    }

    /// <summary>
    /// Baseline construction for the valid-zip64-small control: serializes the tiny
    /// genuine Zip64 records directly, because the standard writer never selects Zip64
    /// for small Archives. One stored entry with 32-bit sentinels everywhere required:
    /// local header sizes 0xFFFFFFFF with a local Zip64 extra field (uncompressed then
    /// compressed size, in specification order), a central header with size and offset
    /// sentinels resolved by its Zip64 extra field (uncompressed, compressed, local
    /// header offset), and a coherent Zip64 EOCD (56 bytes) plus locator (20 bytes)
    /// before the EOCD, whose counts and span fields are sentinels. Required version
    /// fields are 45 throughout. The physical content stays tiny (ticket #841 step 6).
    /// </summary>
    private static ArchiveFixtureArtifact BuildHandBuiltZip64(
        ArchiveTestCaseDefinition definition, int seed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (definition.Recipe.Entries.Count != 1 || definition.Recipe.Entries[0].IsDirectory || definition.Recipe.Entries[0].Method != "stored")
        {
            throw new InvalidOperationException(
                $"Archive Test case '{definition.CaseKey}': the Zip64 baseline expects exactly one stored file entry.");
        }

        var recipeEntry = definition.Recipe.Entries[0];
        var content = GeneratePayload(definition.CaseKey, seed, recipeEntry);
        var name = Encoding.UTF8.GetBytes(recipeEntry.Name);
        var crc = Crc32(content);

        // Local header + Zip64 local extra (uncompressed then compressed size).
        var localExtra = new byte[20];
        BinaryPrimitives.WriteUInt16LittleEndian(localExtra, 0x0001);                 // Zip64 extra field id
        BinaryPrimitives.WriteUInt16LittleEndian(localExtra.AsSpan(2), 16);           // subfield data size
        BinaryPrimitives.WriteUInt64LittleEndian(localExtra.AsSpan(4), (ulong)content.Length);     // uncompressed size
        BinaryPrimitives.WriteUInt64LittleEndian(localExtra.AsSpan(12), (ulong)content.Length);    // compressed size (stored)

        // Central header Zip64 extra (uncompressed, compressed, local header offset).
        var centralExtra = new byte[28];
        BinaryPrimitives.WriteUInt16LittleEndian(centralExtra, 0x0001);
        BinaryPrimitives.WriteUInt16LittleEndian(centralExtra.AsSpan(2), 24);
        BinaryPrimitives.WriteUInt64LittleEndian(centralExtra.AsSpan(4), (ulong)content.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(centralExtra.AsSpan(12), (ulong)content.Length);
        BinaryPrimitives.WriteUInt64LittleEndian(centralExtra.AsSpan(20), 0);         // local header offset

        var localHeaderLength = 30 + name.Length + localExtra.Length;
        var centralDirectoryOffset = localHeaderLength + content.Length;
        var centralHeaderLength = 46 + name.Length + centralExtra.Length;
        var zip64EocdOffset = centralDirectoryOffset + centralHeaderLength;

        var bytes = new byte[checked(zip64EocdOffset + 56 + 20 + 22)];
        Span<byte> b = bytes;

        // Local header.
        BinaryPrimitives.WriteUInt32LittleEndian(b, 0x04034b50);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(4), 45);                     // version needed: Zip64
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(6), 0);                      // flags
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(8), 0);                      // method: stored
        WriteDosTimestamp(b.Slice(10));
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(14), crc);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(18), 0xFFFFFFFF);            // compressed size sentinel
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(22), 0xFFFFFFFF);            // uncompressed size sentinel
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(26), (ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(28), (ushort)localExtra.Length);
        name.CopyTo(b.Slice(30));
        localExtra.CopyTo(b.Slice(30 + name.Length));
        content.CopyTo(b.Slice(localHeaderLength));

        // Central header.
        var c = centralDirectoryOffset;
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c), 0x02014b50);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 4), 45);                 // version made by
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 6), 45);                 // version needed
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 8), 0);                  // flags
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 10), 0);                 // method
        WriteDosTimestamp(b.Slice(c + 12));
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c + 16), crc);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c + 20), 0xFFFFFFFF);        // compressed size sentinel
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c + 24), 0xFFFFFFFF);        // uncompressed size sentinel
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 28), (ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 30), (ushort)centralExtra.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 32), 0);                 // entry comment length
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 34), 0);                 // disk number
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 36), 0);                 // internal attributes
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c + 38), 0);                 // external attributes
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c + 42), 0xFFFFFFFF);        // local header offset sentinel
        name.CopyTo(b.Slice(c + 46));
        centralExtra.CopyTo(b.Slice(c + 46 + name.Length));

        // Zip64 EOCD (56 bytes).
        var z = zip64EocdOffset;
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(z), 0x06064b50);
        BinaryPrimitives.WriteUInt64LittleEndian(b.Slice(z + 4), 44);                 // size of remaining record
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(z + 12), 45);                // version made by
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(z + 14), 45);                // version needed
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(z + 16), 0);                 // disk number
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(z + 20), 0);                 // disk with central directory
        BinaryPrimitives.WriteUInt64LittleEndian(b.Slice(z + 24), 1);                 // entries on this disk
        BinaryPrimitives.WriteUInt64LittleEndian(b.Slice(z + 32), 1);                 // total entries
        BinaryPrimitives.WriteUInt64LittleEndian(b.Slice(z + 40), (ulong)centralHeaderLength);
        BinaryPrimitives.WriteUInt64LittleEndian(b.Slice(z + 48), (ulong)centralDirectoryOffset);
        // z + 56: extensible data (empty).

        // Zip64 EOCD locator (20 bytes), immediately before the EOCD.
        var l = zip64EocdOffset + 56;
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(l), 0x07064b50);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(l + 4), 0);                  // disk with Zip64 EOCD
        BinaryPrimitives.WriteUInt64LittleEndian(b.Slice(l + 8), (ulong)zip64EocdOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(l + 16), 1);                 // total disks

        // EOCD with sentinels: counts and span resolved through the Zip64 EOCD.
        var e = zip64EocdOffset + 56 + 20;
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(e), 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(e + 4), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(e + 6), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(e + 8), 0xFFFF);             // entry count sentinel
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(e + 10), 0xFFFF);            // total count sentinel
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(e + 12), 0xFFFFFFFF);        // central size sentinel
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(e + 16), 0xFFFFFFFF);        // central offset sentinel
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(e + 20), 0);                 // comment length

        var layout = ArchiveFixtureLayout.Read(bytes);
        return new ArchiveFixtureArtifact(
            ArchiveBytes: bytes,
            ArchiveSha256: Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Layout: layout,
            Entries:
            [
                new ArchiveFixtureEntryExpectation(
                    Ordinal: 0,
                    Name: recipeEntry.Name,
                    IsDirectory: false,
                    Method: 0,
                    Content: content,
                    ContentSha256: Convert.ToHexStringLower(SHA256.HashData(content))),
            ],
            Mutations: []);
    }

    /// <summary>
    /// Baseline construction for the zip-bomb-overlapping-deflate case (ticket #872):
    /// one real DEFLATE stream under a single local header at offset 0, with
    /// one identical central header per recipe entry all pointing at that local
    /// header. The shared physical range is the whole point (Fifield's overlapping
    /// construction): a tiny physical Archive declares N times the payload's
    /// expansion, honestly and within the 32 MiB expanded budget. All recipe entries
    /// must share one deflate file entry; the content varies with the seed like
    /// every other payload. The compressed bytes come from DeflateStream Optimal
    /// and are deterministic per seed on one runtime but not frozen across
    /// runtimes (same caveat as valid-deflate; only stored/header-only Fixture IDs
    /// are byte goldens per #846).
    /// </summary>
    private static ArchiveFixtureArtifact BuildHandBuiltOverlapping(
        ArchiveTestCaseDefinition definition, int seed, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var count = definition.Recipe.Entries.Count;
        if (count < 2 || count > ArchiveTestCaseSemantics.MaxEntries)
        {
            throw new InvalidOperationException(
                $"Archive Test case '{definition.CaseKey}': the overlapping baseline expects 2 to {ArchiveTestCaseSemantics.MaxEntries} entries; found {count}.");
        }

        var recipeEntry = definition.Recipe.Entries[0];
        if (recipeEntry.IsDirectory || recipeEntry.Method != "deflate"
            || definition.Recipe.Entries.Any(e => e.Name != recipeEntry.Name || e.Length != recipeEntry.Length || e.Method != recipeEntry.Method || e.IsDirectory))
        {
            throw new InvalidOperationException(
                $"Archive Test case '{definition.CaseKey}': the overlapping baseline expects every entry to be the same deflate file.");
        }

        var content = GeneratePayload(definition.CaseKey, seed, recipeEntry);
        byte[] compressed;
        using (var squeezed = new MemoryStream())
        {
            using (var deflate = new DeflateStream(squeezed, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(content);
            }

            compressed = squeezed.ToArray();
        }

        var name = Encoding.UTF8.GetBytes(recipeEntry.Name);
        var crc = Crc32(content);
        var localHeaderLength = checked(30 + name.Length);
        var centralDirectoryOffset = checked(localHeaderLength + compressed.Length);
        var centralHeaderLength = checked(46 + name.Length);
        var eocdOffset = checked(centralDirectoryOffset + centralHeaderLength * count);

        var bytes = new byte[checked(eocdOffset + 22)];
        Span<byte> b = bytes;

        BinaryPrimitives.WriteUInt32LittleEndian(b, 0x04034b50);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(4), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(6), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(8), 8);
        WriteDosTimestamp(b.Slice(10));
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(14), crc);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(18), (uint)compressed.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(22), (uint)content.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(26), (ushort)name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(28), 0);
        name.CopyTo(b.Slice(30));
        compressed.CopyTo(b.Slice(localHeaderLength));

        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var c = checked(centralDirectoryOffset + centralHeaderLength * ordinal);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c), 0x02014b50);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 4), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 6), 20);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 8), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 10), 8);
            WriteDosTimestamp(b.Slice(c + 12));
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c + 16), crc);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c + 20), (uint)compressed.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c + 24), (uint)content.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 28), (ushort)name.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 30), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 32), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 34), 0);
            BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(c + 36), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c + 38), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(c + 42), 0);
            name.CopyTo(b.Slice(c + 46));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(eocdOffset), 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(eocdOffset + 4), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(eocdOffset + 6), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(eocdOffset + 8), (ushort)count);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(eocdOffset + 10), (ushort)count);
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(eocdOffset + 12), (uint)(centralHeaderLength * count));
        BinaryPrimitives.WriteUInt32LittleEndian(b.Slice(eocdOffset + 16), (uint)centralDirectoryOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(b.Slice(eocdOffset + 20), 0);

        if (bytes.Length > ArchiveTestCaseSemantics.MaxArchivePhysicalBytes)
        {
            throw new InvalidDataException($"Archive Test case '{definition.CaseKey}' Archive is {bytes.Length} bytes, over the physical budget.");
        }

        var layout = ArchiveFixtureLayout.Read(bytes);
        var contentSha = Convert.ToHexStringLower(SHA256.HashData(content));
        return new ArchiveFixtureArtifact(
            ArchiveBytes: bytes,
            ArchiveSha256: Convert.ToHexStringLower(SHA256.HashData(bytes)),
            Layout: layout,
            Entries: [.. definition.Recipe.Entries.Select((entry, ordinal) => new ArchiveFixtureEntryExpectation(
                Ordinal: ordinal,
                Name: entry.Name,
                IsDirectory: false,
                Method: 8,
                Content: content,
                ContentSha256: contentSha))],
            Mutations: []);
    }

    /// <summary>The fixed DOS timestamp (2024-01-01 UTC) as the little-endian time+date words.</summary>
    private static void WriteDosTimestamp(Span<byte> timeAndDate)
    {
        // 2024-01-01 00:00:00 → DOS time 0x0000, DOS date 0x5821 (year 44 offset, month 1, day 1).
        BinaryPrimitives.WriteUInt16LittleEndian(timeAndDate, 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(timeAndDate.Slice(2), 0x5821);
    }

    /// <summary>CRC-32 (IEEE, reflected, polynomial 0xEDB88320) over the payload; the
    /// reference reader validates the same value on read. Internal so the orphan-hidden
    /// mutation (ticket #869) builds a consistent hidden header without duplicating it.</summary>
    internal static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFF_FFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB8_8320u : crc >> 1;
            }
        }

        return ~crc;
    }

    /// <summary>
    /// A narrow non-seekable view over bounded owned memory: the standard writer sees
    /// <see cref="Stream.CanSeek"/> false and emits data descriptors with the signature.
    /// Not a generic stream-adapter framework (ticket #841 step 1): it delegates every
    /// operation to the owned inner buffer and forbids seeking and position reads.
    /// </summary>
    private sealed class NonSeekableMemoryStream(MemoryStream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;

        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.WriteAsync(buffer, offset, count, cancellationToken);
        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("The non-seekable descriptor target is write-only.");
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException("The non-seekable descriptor target forbids seeking.");
        public override void SetLength(long value) => throw new NotSupportedException("The non-seekable descriptor target forbids resizing.");
        public override long Position
        {
            get => throw new NotSupportedException("The non-seekable descriptor target forbids position reads.");
            set => throw new NotSupportedException("The non-seekable descriptor target forbids seeking.");
        }
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
