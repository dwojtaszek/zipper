using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Zipper.ArchiveTests;

/// <summary>
/// The named mutations applied to valid control Archives to build malformed and
/// policy-sensitive fixtures (tickets #839 and #840). Target offsets always come from
/// the control's captured layout, never from a signature scan, and every coordinate is
/// on the before-mutation basis. Malformed output is produced by editing control bytes
/// directly — it is never reopened with <c>ZipArchiveMode.Update</c>, which may repair
/// defects.
/// </summary>
internal static class ArchiveFixtureMutator
{
    /// <summary>The reserved method code 98 (PPMd): a defined method with no codec in the
    /// reference reader; used by the policy-sensitive unsupported-method case.</summary>
    private const ushort UnsupportedMethodCode = 98;

    /// <summary>The general-purpose bit 0: the "encrypted" flag.</summary>
    private const ushort EncryptionFlag = 0x0001;

    internal static MutatedArchiveFixture Apply(ArchiveTestMutationKind kind, ArchiveFixtureArtifact control)
    {
        ArgumentNullException.ThrowIfNull(control);
        var before = control.ArchiveBytes;

        return kind switch
        {
            ArchiveTestMutationKind.CrcLocalMismatch
                or ArchiveTestMutationKind.CrcCentralMismatch
                or ArchiveTestMutationKind.CrcBothMismatch => FlipCrc(kind, control, before),
            ArchiveTestMutationKind.TruncatePayloadTail => Truncate(
                kind, "file-data", control.Layout.Entries[^1].DataOffset + 10, control.Layout.Entries.Count - 1,
                "Truncates the Archive inside the final entry's compressed payload. By construction this also removes every later structure (the remaining payload bytes, the central directory, and the EOCD); the fixture is a tail-truncation case, not an isolated payload corruption.",
                before),
            ArchiveTestMutationKind.TruncateCentralTail => Truncate(
                kind, "central-header", control.Layout.CentralDirectoryOffset + 8, 0,
                "Truncates the Archive inside a central directory entry, removing the rest of the central directory and the EOCD; no entry becomes fully described.",
                before),
            ArchiveTestMutationKind.TruncateEocd => Truncate(
                kind, "eocd", control.Layout.EocdOffset + 10, null,
                "Removes part of the EOCD record; the Archive keeps a complete entry and central directory but an incomplete EOCD.",
                before),
            ArchiveTestMutationKind.MissingEocd => Truncate(
                kind, "eocd", control.Layout.EocdOffset, null,
                "Removes the entire EOCD record; entry and central directory bytes remain, but the Archive has no end record at all.",
                before),
            ArchiveTestMutationKind.NameLocalCentralMismatch => MismatchLocalName(control, before),
            ArchiveTestMutationKind.MethodLocalCentralMismatch => MismatchLocalMethod(control, before),
            ArchiveTestMutationKind.SizeLocalCentralMismatch => MismatchLocalCompressedSize(control, before),
            ArchiveTestMutationKind.OffsetOutsideArchive => PointCentralOffsetOutsideArchive(control, before),
            ArchiveTestMutationKind.OffsetIntoPayload => PointCentralOffsetIntoPayload(control, before),
            ArchiveTestMutationKind.ExtraFieldLengthOverrun => OverrunExtraFieldLength(control, before),
            ArchiveTestMutationKind.UnsupportedMethod => DeclareUnsupportedMethod(control, before),
            ArchiveTestMutationKind.EncryptionFlagWithPlaintext => SetEncryptionFlagOnPlaintext(control, before),
            ArchiveTestMutationKind.OverlappingEntryRanges => OverlapEntryRanges(control, before),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Archive Test mutation kind."),
        };
    }

    private static MutatedArchiveFixture FlipCrc(ArchiveTestMutationKind kind, ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var localCrcOffset = entry.LocalHeaderOffset + 14;  // local header CRC-32 field
        var centralCrcOffset = entry.CentralDirectoryOffset + 16;  // central header CRC-32 field

        return Patch(kind, before,
        [
            kind is ArchiveTestMutationKind.CrcLocalMismatch or ArchiveTestMutationKind.CrcBothMismatch
                ? new MutationSpec(
                    ArchiveTestMutationKind.CrcLocalMismatch.ToCaseKey(), "local-header", localCrcOffset,
                    "Flips the low bit of the first entry's CRC-32 in the local header only; the payload and all other structures are unchanged.",
                    entry.Ordinal, 1, 1)
                : null,
            kind is ArchiveTestMutationKind.CrcCentralMismatch or ArchiveTestMutationKind.CrcBothMismatch
                ? new MutationSpec(
                    ArchiveTestMutationKind.CrcCentralMismatch.ToCaseKey(), "central-header", centralCrcOffset,
                    kind == ArchiveTestMutationKind.CrcCentralMismatch
                        ? "Flips the low bit of the first entry's CRC-32 in the central header only; the payload and all other structures are unchanged."
                        : "Flips the low bit of the first entry's CRC-32 in the central header, applied to the same before-mutation Archive as the local-header record (parallel field views of one transition, not a chain); the payload and all other structures are unchanged.",
                    entry.Ordinal, 1, 1)
                : null,
        ], after =>
        {
            if (kind is ArchiveTestMutationKind.CrcLocalMismatch or ArchiveTestMutationKind.CrcBothMismatch)
            {
                after[localCrcOffset] ^= 0x01;
            }

            if (kind is ArchiveTestMutationKind.CrcCentralMismatch or ArchiveTestMutationKind.CrcBothMismatch)
            {
                after[centralCrcOffset] ^= 0x01;
            }
        });
    }

    /// <summary>
    /// Replaces the local header's name bytes with a same-length different name while the
    /// central header keeps the control's name: both raw names are recorded (the control
    /// name in the entries' physical facts, the local replacement in the record's hex).
    /// </summary>
    private static MutatedArchiveFixture MismatchLocalName(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var nameOffset = entry.LocalHeaderOffset + 30;  // local header name field
        var nameLength = Encoding.UTF8.GetByteCount(entry.Name);
        var replacement = DifferentSameLengthName(entry.Name);
        if (Encoding.UTF8.GetByteCount(replacement) != nameLength)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{ArchiveTestMutationKind.NameLocalCentralMismatch.ToCaseKey()}': the replacement name '{replacement}' must have the same UTF-8 byte length as '{entry.Name}'.");
        }

        return Patch(ArchiveTestMutationKind.NameLocalCentralMismatch, before,
        [
            new MutationSpec(
                ArchiveTestMutationKind.NameLocalCentralMismatch.ToCaseKey(), "local-header", nameOffset,
                $"Replaces the local header's name bytes with the same-length name '{replacement}' while the central header still records '{entry.Name}'; lenient central-based readers list '{entry.Name}', local-header-strict readers see '{replacement}'. The payload and all other structures are unchanged.",
                entry.Ordinal, nameLength, nameLength, DeclaredValue: $"local-name={replacement} central-name={entry.Name}"),
        ], after => WriteBytes(after, nameOffset, Encoding.UTF8.GetBytes(replacement)));
    }

    /// <summary>
    /// Changes the local header's compression method to deflate while the central header
    /// retains the control's stored method: the payload bytes stay unchanged, so the
    /// defect is a method-code inconsistency, not corrupt compressed data.
    /// </summary>
    private static MutatedArchiveFixture MismatchLocalMethod(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var localMethodOffset = entry.LocalHeaderOffset + 8;  // local header method field (u16)
        var declaredMethod = entry.Method == 0 ? (ushort)8 : (ushort)0;

        return Patch(ArchiveTestMutationKind.MethodLocalCentralMismatch, before,
        [
            new MutationSpec(
                ArchiveTestMutationKind.MethodLocalCentralMismatch.ToCaseKey(), "local-header", localMethodOffset,
                $"Changes the local header's compression method from {entry.Method} to {declaredMethod} while the central header retains {entry.Method}; the payload bytes are unchanged, so a lenient central-method reader still reads them and a local-method reader fails to decode.",
                entry.Ordinal, 2, 2, DeclaredValue: $"local-method={declaredMethod} central-method={entry.Method}"),
        ], after => BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)localMethodOffset, 2), declaredMethod));
    }

    /// <summary>
    /// Changes the local header's compressed size by one byte while the entry content and
    /// the central compressed size stay unchanged.
    /// </summary>
    private static MutatedArchiveFixture MismatchLocalCompressedSize(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var localCompressedSizeOffset = entry.LocalHeaderOffset + 18;  // local header compressed size (u32)
        var declaredSize = checked(entry.CompressedSize + 1);

        return Patch(ArchiveTestMutationKind.SizeLocalCentralMismatch, before,
        [
            new MutationSpec(
                ArchiveTestMutationKind.SizeLocalCentralMismatch.ToCaseKey(), "local-header", localCompressedSizeOffset,
                $"Changes the local header's compressed size from {entry.CompressedSize} to {declaredSize} while the entry content and the central header's compressed size ({entry.CompressedSize}) stay unchanged.",
                entry.Ordinal, 4, 4, DeclaredValue: $"local-compressed-size={declaredSize} central-compressed-size={entry.CompressedSize}"),
        ], after => BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)localCompressedSizeOffset, 4), declaredSize));
    }

    /// <summary>
    /// Rewrites the first entry's central relative-local-header offset to a fixed value
    /// near the UInt32 boundary, far beyond physical EOF. The physical bytes and the
    /// control layout are unchanged; the lie lives in the declared offset only.
    /// </summary>
    private static MutatedArchiveFixture PointCentralOffsetOutsideArchive(ArchiveFixtureArtifact control, byte[] before)
    {
        const uint declaredOffset = 0x7FFF_0000;
        var entry = control.Layout.Entries[0];
        var centralRelativeOffsetOffset = entry.CentralDirectoryOffset + 42;  // central header relative offset (u32)

        if (declaredOffset <= (uint)before.Length)
        {
            throw new InvalidOperationException("The outside-archive offset must point beyond physical EOF.");
        }

        return Patch(ArchiveTestMutationKind.OffsetOutsideArchive, before,
        [
            new MutationSpec(
                ArchiveTestMutationKind.OffsetOutsideArchive.ToCaseKey(), "central-header", centralRelativeOffsetOffset,
                $"Rewrites the first entry's central relative-local-header offset from {entry.LocalHeaderOffset} to {declaredOffset} (0x7FFF0000), far beyond the {before.Length}-byte physical Archive; seeking the declared local header fails at EOF.",
                entry.Ordinal, 4, 4, DeclaredValue: $"declared-local-header-offset={declaredOffset}"),
        ], after => BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)centralRelativeOffsetOffset, 4), declaredOffset));
    }

    /// <summary>
    /// Rewrites the first entry's central relative-local-header offset into the entry's
    /// own payload, which begins with signature-like bytes (PK\x03\x04) that are data,
    /// not a genuine local header.
    /// </summary>
    private static MutatedArchiveFixture PointCentralOffsetIntoPayload(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var centralRelativeOffsetOffset = entry.CentralDirectoryOffset + 42;
        var declaredOffset = (uint)entry.DataOffset;

        return Patch(ArchiveTestMutationKind.OffsetIntoPayload, before,
        [
            new MutationSpec(
                ArchiveTestMutationKind.OffsetIntoPayload.ToCaseKey(), "central-header", centralRelativeOffsetOffset,
                $"Rewrites the first entry's central relative-local-header offset from {entry.LocalHeaderOffset} to {declaredOffset}, inside the entry's own payload at its leading signature-like bytes (PK\\x03\\x04); the declared bytes are data, not a genuine local header.",
                entry.Ordinal, 4, 4, DeclaredValue: $"declared-local-header-offset={declaredOffset} (payload data at 0x{entry.DataOffset:x})"),
        ], after => BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)centralRelativeOffsetOffset, 4), declaredOffset));
    }

    /// <summary>
    /// Rewrites the first entry's local extra subfield size from 4 to 16, claiming more
    /// data than the enclosing 8-byte extra area holds, while the enclosing header's
    /// extra-length field stays 8 so the defect is precise.
    /// </summary>
    private static MutatedArchiveFixture OverrunExtraFieldLength(ArchiveFixtureArtifact control, byte[] before)
    {
        const ushort declaredSubfieldSize = 16;
        var entry = control.Layout.Entries[0];
        if (entry.LocalExtraLength is not 8)
        {
            throw new InvalidOperationException(
                $"extra-field-length-overrun expects the control's entry 0 to carry the enriched 8-byte local extra field; found {entry.LocalExtraLength}.");
        }

        var subfieldSizeOffset = entry.LocalHeaderOffset + 30 + entry.LocalNameLength + 2;  // subfield header: ID (u16), then size (u16)

        return Patch(ArchiveTestMutationKind.ExtraFieldLengthOverrun, before,
        [
            new MutationSpec(
                ArchiveTestMutationKind.ExtraFieldLengthOverrun.ToCaseKey(), "local-header", subfieldSizeOffset,
                $"Rewrites the local extra subfield's data size from 4 to {declaredSubfieldSize}, claiming more bytes than the enclosing 8-byte extra area holds; the enclosing local header extra-length stays 8 so the overrun is the only defect.",
                entry.Ordinal, 2, 2, DeclaredValue: $"subfield-size={declaredSubfieldSize} enclosing-extra-area=8"),
        ], after => BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)subfieldSizeOffset, 2), declaredSubfieldSize));
    }

    /// <summary>
    /// Rewrites the compression method in both the local and the central header to the
    /// reserved code 98 (PPMd): the codes are consistent, the payload bytes stay the
    /// stored control bytes, and the reader simply has no codec for the declared method.
    /// Policy-sensitive, not universally malformed.
    /// </summary>
    private static MutatedArchiveFixture DeclareUnsupportedMethod(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var localMethodOffset = entry.LocalHeaderOffset + 8;
        var centralMethodOffset = entry.CentralDirectoryOffset + 10;

        return Patch(ArchiveTestMutationKind.UnsupportedMethod, before,
        [
            new MutationSpec(
                ArchiveTestMutationKind.UnsupportedMethod.ToCaseKey(), "local-header", localMethodOffset,
                $"Rewrites the local header's compression method from {entry.Method} to {UnsupportedMethodCode} (PPMd), consistent with the central header; the payload bytes are unchanged stored data and no codec for the declared method is implemented. Applied in parallel with the central-header record, not chained.",
                entry.Ordinal, 2, 2, DeclaredValue: $"method={UnsupportedMethodCode}"),
            new MutationSpec(
                ArchiveTestMutationKind.UnsupportedMethod.ToCaseKey(), "central-header", centralMethodOffset,
                $"Rewrites the central header's compression method from {entry.Method} to {UnsupportedMethodCode} (PPMd), consistent with the local header; the payload bytes are unchanged stored data and no codec for the declared method is implemented. Applied in parallel with the local-header record, not chained.",
                entry.Ordinal, 2, 2, DeclaredValue: $"method={UnsupportedMethodCode}"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)localMethodOffset, 2), UnsupportedMethodCode);
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)centralMethodOffset, 2), UnsupportedMethodCode);
        });
    }

    /// <summary>
    /// Sets the encrypted flag (general-purpose bit 0) in both headers while the payload
    /// stays plaintext stored bytes: the recorded flag conflicts with the plaintext
    /// structure. Explicitly malformed; not support for generating encrypted Archives.
    /// </summary>
    private static MutatedArchiveFixture SetEncryptionFlagOnPlaintext(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var localFlagsOffset = entry.LocalHeaderOffset + 6;
        var centralFlagsOffset = entry.CentralDirectoryOffset + 8;

        return Patch(ArchiveTestMutationKind.EncryptionFlagWithPlaintext, before,
        [
            new MutationSpec(
                ArchiveTestMutationKind.EncryptionFlagWithPlaintext.ToCaseKey(), "local-header", localFlagsOffset,
                $"Sets the encrypted flag (general-purpose bit 0) on the local header while the payload stays plaintext stored bytes; readers that honor the flag attempt decryption of plaintext and fail. Applied in parallel with the central-header record, not chained.",
                entry.Ordinal, 2, 2, DeclaredValue: "general-purpose-bits+=0x0001"),
            new MutationSpec(
                ArchiveTestMutationKind.EncryptionFlagWithPlaintext.ToCaseKey(), "central-header", centralFlagsOffset,
                $"Sets the encrypted flag (general-purpose bit 0) on the central header while the payload stays plaintext stored bytes; readers that honor the flag attempt decryption of plaintext and fail. Applied in parallel with the local-header record, not chained.",
                entry.Ordinal, 2, 2, DeclaredValue: "general-purpose-bits+=0x0001"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)localFlagsOffset, 2), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(after.AsSpan((int)localFlagsOffset, 2)) | EncryptionFlag));
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)centralFlagsOffset, 2), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(after.AsSpan((int)centralFlagsOffset, 2)) | EncryptionFlag));
        });
    }

    /// <summary>
    /// Rewrites the second entry's central relative-local-header offset to the first
    /// entry's data offset: the two central records then claim overlapping owned byte
    /// ranges. The interval evidence — both claimed ranges — is recorded in the
    /// explanation and declared value; the physical bytes are unchanged.
    /// </summary>
    private static MutatedArchiveFixture OverlapEntryRanges(ArchiveFixtureArtifact control, byte[] before)
    {
        if (control.Layout.Entries.Count < 2)
        {
            throw new InvalidOperationException("overlapping-entry-ranges expects a control with at least two entries.");
        }

        var first = control.Layout.Entries[0];
        var second = control.Layout.Entries[1];
        var centralRelativeOffsetOffset = second.CentralDirectoryOffset + 42;
        var declaredOffset = (uint)first.DataOffset;
        var firstRangeEnd = checked(first.DataOffset + first.CompressedSize);
        var secondClaimedEnd = checked((long)declaredOffset + second.CompressedSize);

        return Patch(ArchiveTestMutationKind.OverlappingEntryRanges, before,
        [
            new MutationSpec(
                ArchiveTestMutationKind.OverlappingEntryRanges.ToCaseKey(), "central-header", centralRelativeOffsetOffset,
                $"Rewrites the second entry's central relative-local-header offset from {second.LocalHeaderOffset} to {declaredOffset}, inside the first entry's payload: the first entry owns bytes {first.DataOffset}..{firstRangeEnd} and the second entry now claims {declaredOffset}..{secondClaimedEnd}, overlapping ranges. The physical bytes are unchanged; strict structure policy treats the claims as conflicting.",
                second.Ordinal, 4, 4, DeclaredValue: $"entry0-range={first.DataOffset}..{firstRangeEnd} entry1-claimed={declaredOffset}..{secondClaimedEnd}"),
        ], after => BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)centralRelativeOffsetOffset, 4), declaredOffset));
    }

    /// <summary>One intended field change: what changed, where, and how much.</summary>
    private sealed record MutationSpec(
        string Code,
        string Structure,
        long Offset,
        string Explanation,
        int? Ordinal,
        int DeletedLength,
        int InsertedLength,
        string? DeclaredValue = null);

    private static MutatedArchiveFixture Patch(
        ArchiveTestMutationKind kind, byte[] before, MutationSpec?[] specs, Action<byte[]> apply)
    {
        var after = (byte[])before.Clone();
        apply(after);
        var mutations = specs.Where(spec => spec is not null).Select(spec => Record(spec!, before, after)).ToList();
        return Finalize(kind, before, after, mutations);
    }

    /// <summary>
    /// Records a field rewrite: the hex windows show the changed range plus context (at
    /// least 4 bytes, at most 32), with before/after hashes and sizes covering the whole
    /// Archive. Multiple records of one fixture describe the same control → final
    /// transition (parallel field views, not a chain).
    /// </summary>
    private static ArchiveTestMutation Record(MutationSpec spec, byte[] before, byte[] after)
    {
        var window = Math.Clamp(spec.DeletedLength, 4, 32);
        return new ArchiveTestMutation(
            Code: spec.Code,
            Structure: spec.Structure,
            OffsetBasis: "before-mutation",
            Offset: spec.Offset,
            Explanation: spec.Explanation,
            Ordinal: spec.Ordinal,
            DeletedLength: spec.DeletedLength,
            InsertedLength: spec.InsertedLength,
            BeforeSize: before.Length,
            AfterSize: after.Length,
            BeforeSha256: Hash(before),
            AfterSha256: Hash(after),
            BeforeHex: Hex(before, spec.Offset, Math.Min(window, before.Length - (int)spec.Offset)),
            AfterHex: Hex(after, spec.Offset, Math.Min(window, after.Length - (int)spec.Offset)),
            DeclaredValue: spec.DeclaredValue);
    }

    private static MutatedArchiveFixture Truncate(
        ArchiveTestMutationKind kind, string structure, long offset, int? ordinal, string explanation, byte[] before)
    {
        var after = before[..(int)offset];

        // Unlike Record's field windows, the truncation BeforeHex is the removed content:
        // up to 32 of the deleted tail bytes, not an in-place window (which no longer exists).
        var mutation = Record(
            new MutationSpec(kind.ToCaseKey(), structure, offset, explanation, ordinal, 0, 0),
            before, after) with
        {
            DeletedLength = before.Length - offset,
            InsertedLength = 0,
            BeforeHex = Hex(before, offset, Math.Min(32, before.Length - (int)offset)),
            AfterHex = null,
        };

        return Finalize(kind, before, after, [mutation]);
    }

    private static MutatedArchiveFixture Finalize(
        ArchiveTestMutationKind kind, byte[] before, byte[] after, List<ArchiveTestMutation> mutations)
    {
        if (after.AsSpan().SequenceEqual(before))
        {
            throw new InvalidOperationException($"Archive Test mutation '{kind.ToCaseKey()}' produced no byte change.");
        }

        return new MutatedArchiveFixture(after, Hash(after), mutations);
    }

    private static void WriteBytes(byte[] bytes, long offset, byte[] replacement) =>
        replacement.CopyTo(bytes.AsSpan((int)offset));

    private static string DifferentSameLengthName(string name)
    {
        var replacement = name.ToCharArray();
        replacement[0] = name[0] == 'z' ? 'a' : 'z';
        return new string(replacement);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string Hex(byte[] bytes, long offset, int length) =>
        Convert.ToHexStringLower(bytes.AsSpan((int)offset, length));
}

/// <summary>
/// A control Archive after one named mutation: final bytes, final hash, and the mutation
/// records describing every changed range on the before-mutation coordinate system.
/// </summary>
internal sealed record MutatedArchiveFixture(
    byte[] ArchiveBytes,
    string ArchiveSha256,
    IReadOnlyList<ArchiveTestMutation> Mutations);

internal enum ArchiveTestMutationKind
{
    CrcLocalMismatch,
    CrcCentralMismatch,
    CrcBothMismatch,
    TruncatePayloadTail,
    TruncateCentralTail,
    TruncateEocd,
    MissingEocd,
    NameLocalCentralMismatch,
    MethodLocalCentralMismatch,
    SizeLocalCentralMismatch,
    OffsetOutsideArchive,
    OffsetIntoPayload,
    ExtraFieldLengthOverrun,
    UnsupportedMethod,
    EncryptionFlagWithPlaintext,
    OverlappingEntryRanges,
}

internal static class ArchiveTestMutationKindExtensions
{
    /// <summary>The canonical Case Key of each named mutation: the malformed catalog entries
    /// and the JSON mutation codes both derive from this single mapping.</summary>
    internal static string ToCaseKey(this ArchiveTestMutationKind kind) => kind switch
    {
        ArchiveTestMutationKind.CrcLocalMismatch => "crc-local-mismatch",
        ArchiveTestMutationKind.CrcCentralMismatch => "crc-central-mismatch",
        ArchiveTestMutationKind.CrcBothMismatch => "crc-both-mismatch",
        ArchiveTestMutationKind.TruncatePayloadTail => "truncate-payload-tail",
        ArchiveTestMutationKind.TruncateCentralTail => "truncate-central-tail",
        ArchiveTestMutationKind.TruncateEocd => "truncate-eocd",
        ArchiveTestMutationKind.MissingEocd => "missing-eocd",
        ArchiveTestMutationKind.NameLocalCentralMismatch => "name-local-central-mismatch",
        ArchiveTestMutationKind.MethodLocalCentralMismatch => "method-local-central-mismatch",
        ArchiveTestMutationKind.SizeLocalCentralMismatch => "size-local-central-mismatch",
        ArchiveTestMutationKind.OffsetOutsideArchive => "offset-outside-archive",
        ArchiveTestMutationKind.OffsetIntoPayload => "offset-into-payload",
        ArchiveTestMutationKind.ExtraFieldLengthOverrun => "extra-field-length-overrun",
        ArchiveTestMutationKind.UnsupportedMethod => "unsupported-method",
        ArchiveTestMutationKind.EncryptionFlagWithPlaintext => "encryption-flag-with-plaintext",
        ArchiveTestMutationKind.OverlappingEntryRanges => "overlapping-entry-ranges",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Archive Test mutation kind."),
    };
}
