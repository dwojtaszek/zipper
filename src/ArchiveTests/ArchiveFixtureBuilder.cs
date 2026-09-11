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

        if (definition.Construction is ArchiveControlConstruction.Zip64HandBuilt)
        {
            return BuildHandBuiltZip64(definition, seed, cancellationToken);
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

        // Baseline construction (valid, never a defect): applied after the standard
        // writer finalized the Archive, before layout capture, so the captured layout
        // reflects the constructed bytes.
        if (definition.Construction is ArchiveControlConstruction.LocalExtraField)
        {
            archiveBytes = EnrichLocalExtraField(archiveBytes, definition.CaseKey);
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

    /// <summary>The fixed DOS timestamp (2024-01-01 UTC) as the little-endian time+date words.</summary>
    private static void WriteDosTimestamp(Span<byte> timeAndDate)
    {
        // 2024-01-01 00:00:00 → DOS time 0x0000, DOS date 0x5821 (year 44 offset, month 1, day 1).
        BinaryPrimitives.WriteUInt16LittleEndian(timeAndDate, 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(timeAndDate.Slice(2), 0x5821);
    }

    /// <summary>CRC-32 (IEEE, reflected, polynomial 0xEDB88320) over the payload; the
    /// reference reader validates the same value on read.</summary>
    private static uint Crc32(ReadOnlySpan<byte> data)
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
