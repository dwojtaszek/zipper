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

    /// <summary>The Deflate64 method code (ticket #877): documented but undecodable by the
    /// reference reader; used by the policy-sensitive unsupported-method-deflate64 case.</summary>
    private const ushort UnsupportedMethodDeflate64Code = 9;

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
            ArchiveTestMutationKind.InvalidUtf8Name => InvalidateUtf8Name(control, before),
            ArchiveTestMutationKind.Zip64MissingExtra => HideZip64Extra(control, before),
            ArchiveTestMutationKind.Zip64TruncatedExtra => OverrunZip64Extra(control, before),
            ArchiveTestMutationKind.DeclaredSizeOversized => DeclareOversizedUncompressedSize(control, before),
            ArchiveTestMutationKind.OrphanLocalHeader => InsertOrphanLocalHeader(control, before),
            ArchiveTestMutationKind.PrefixUnrebased => InsertPrefixWithoutRebase(control, before),
            ArchiveTestMutationKind.DeflateInvalidBtype => CorruptDeflateBtype(control, before, ArchiveTestMutationKind.DeflateInvalidBtype),
            ArchiveTestMutationKind.DeflateCorruptHuffman => CorruptDeflateHuffman(control, before),
            ArchiveTestMutationKind.Bzip2CorruptBlockMagic => CorruptBzip2Magic(control, before),
            ArchiveTestMutationKind.Bzip2TruncatedStream => TruncateCodedTail(control, before, ArchiveTestMutationKind.Bzip2TruncatedStream, 12),
            ArchiveTestMutationKind.Bzip2WrongCrc => CorruptBzip2Crc(control, before),
            ArchiveTestMutationKind.Deflate64CorruptStream => CorruptDeflateBtype(control, before, ArchiveTestMutationKind.Deflate64CorruptStream),
            ArchiveTestMutationKind.Deflate64TruncatedStream => TruncateCodedTail(control, before, ArchiveTestMutationKind.Deflate64TruncatedStream, 9),
            ArchiveTestMutationKind.MethodCrossDeflate64Deflate => SetLocalMethodCode(control, before, ArchiveTestMutationKind.MethodCrossDeflate64Deflate, 9),
            ArchiveTestMutationKind.MethodCrossBzip2Stored => SetLocalMethodCode(control, before, ArchiveTestMutationKind.MethodCrossBzip2Stored, 12),
            ArchiveTestMutationKind.MethodDataDeflateAsBzip2 => DeclareMethodCode(control, before, ArchiveTestMutationKind.MethodDataDeflateAsBzip2, 8, "Deflate"),
            ArchiveTestMutationKind.MethodDataBzip2AsStored => DeclareMethodCode(control, before, ArchiveTestMutationKind.MethodDataBzip2AsStored, 12, "BZip2"),
            ArchiveTestMutationKind.UnsupportedMethodDeflate64 => DeclareUnsupportedMethodDeflate64(control, before),
            ArchiveTestMutationKind.MixedMethodsOneCorruptMember => CorruptMixedBzip2Member(control, before),
            ArchiveTestMutationKind.MixedMethodsOneUnsupportedMember => DeclareMixedUnsupportedMember(control, before),
            ArchiveTestMutationKind.MultidiskEocdDeclared => DeclareMultidiskEocd(control, before),
            ArchiveTestMutationKind.MultidiskCentralEntryDeclared => DeclareMultidiskCentralEntry(control, before),
            ArchiveTestMutationKind.EocdEntryCountMismatch => DeclareEocdEntryCountMismatch(control, before),
            ArchiveTestMutationKind.Zip64EocdEntryCountMismatch => DeclareZip64EocdEntryCountMismatch(control, before),
            ArchiveTestMutationKind.Zip64LocatorDiskMismatch => DeclareZip64LocatorDiskMismatch(control, before),
            ArchiveTestMutationKind.DescriptorCrcDisagreement => DisagreeDescriptorCrc(control, before),
            ArchiveTestMutationKind.DescriptorSizeDisagreement => DisagreeDescriptorSize(control, before),
            ArchiveTestMutationKind.DuplicateZip64Extra => DuplicateZip64ExtraField(control, before),
            ArchiveTestMutationKind.DuplicateUnicodePath => DuplicateUnicodePathField(control, before),
            ArchiveTestMutationKind.Utf8FlagCp437Name => SetUtf8FlagOnCp437Name(control, before),
            ArchiveTestMutationKind.UnicodePathCrcMismatch => MismatchUnicodePathCrc(control, before),
            ArchiveTestMutationKind.UnicodePathNameDivergence => DivergeUnicodePathName(control, before),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Archive Test mutation kind."),
        };
    }

    /// <summary>
    /// Declares a multi-disk/spanned Archive in the EOCD (ticket #933): the disk
    /// number and the central-directory start disk both become 1 while the
    /// physical Archive is one flat file. Readers that honor spanning refuse;
    /// readers that ignore spanning read on.
    /// </summary>
    private static MutatedArchiveFixture DeclareMultidiskEocd(ArchiveFixtureArtifact control, byte[] before)
    {
        const ushort SpannedDisk = 1;
        var eocd = control.Layout.EocdOffset;
        var diskOffset = eocd + 4;
        var code = ArchiveTestMutationKind.MultidiskEocdDeclared.ToCaseKey();

        // One 4-byte record covers both adjacent disk fields: parallel records
        // would overlap their hex windows and break control reconstruction.
        return Patch(ArchiveTestMutationKind.MultidiskEocdDeclared, before,
        [
            new MutationSpec(
                code, "eocd", diskOffset,
                "Rewrites the EOCD disk number and central-directory start disk from 0 to 1, declaring this Archive a later disk of a spanned set while the file is a complete single-disk Archive.",
                null, 4, 4, DeclaredValue: "disk-number=1 central-directory-disk=1"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)diskOffset, 2), SpannedDisk);
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)diskOffset + 2, 2), SpannedDisk);
        });
    }

    /// <summary>
    /// Declares a multi-disk Archive in entry 0's central header (ticket #933):
    /// the entry's disk-number-start becomes 1 while the EOCD still describes a
    /// single disk. The EOCD/central disagreement isolates the parser decision
    /// from the EOCD-level declaration above.
    /// </summary>
    private static MutatedArchiveFixture DeclareMultidiskCentralEntry(ArchiveFixtureArtifact control, byte[] before)
    {
        const ushort SpannedDisk = 1;
        var entry = control.Layout.Entries[0];
        var diskOffset = entry.CentralDirectoryOffset + 34;
        var code = ArchiveTestMutationKind.MultidiskCentralEntryDeclared.ToCaseKey();

        return Patch(ArchiveTestMutationKind.MultidiskCentralEntryDeclared, before,
        [
            new MutationSpec(
                code, "central-header", diskOffset,
                "Rewrites entry 0's central-header disk-number-start from 0 to 1, placing the entry on a second disk the EOCD never declares.",
                entry.Ordinal, 2, 2, DeclaredValue: "entry-disk-number-start=1"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)diskOffset, 2), SpannedDisk);
        });
    }

    /// <summary>
    /// Makes the EOCD entry counts disagree (ticket #933): the this-disk count
    /// becomes 1 while the total stays 2 and the central directory physically
    /// holds 2 entries. One field, one parser decision.
    /// </summary>
    private static MutatedArchiveFixture DeclareEocdEntryCountMismatch(ArchiveFixtureArtifact control, byte[] before)
    {
        var eocd = control.Layout.EocdOffset;
        var thisDiskOffset = eocd + 8;
        var totalOffset = eocd + 10;
        var total = BinaryPrimitives.ReadUInt16LittleEndian(before.AsSpan((int)totalOffset, 2));
        var code = ArchiveTestMutationKind.EocdEntryCountMismatch.ToCaseKey();

        return Patch(ArchiveTestMutationKind.EocdEntryCountMismatch, before,
        [
            new MutationSpec(
                code, "eocd", thisDiskOffset,
                $"Rewrites the EOCD this-disk entry count from {total} to {total - 1} while the total stays {total} and the central directory holds {total} entries.",
                null, 2, 2, DeclaredValue: $"this-disk-entries={total - 1} total-entries={total}"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)thisDiskOffset, 2), (ushort)(total - 1));
        });
    }

    /// <summary>
    /// Locates the control's Zip64 EOCD through its locator (immediately before
    /// the EOCD in owned Archives, never a signature search), verifying both
    /// signatures on the before-mutation basis.
    /// </summary>
    private static long Zip64EocdOffset(ArchiveFixtureArtifact control, string caseKey)
    {
        var locatorOffset = control.Layout.EocdOffset - 20;
        if (BinaryPrimitives.ReadUInt32LittleEndian(control.ArchiveBytes.AsSpan((int)locatorOffset, 4)) != ArchiveFixtureLayout.Zip64LocatorSignature)
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': expected the Zip64 EOCD locator signature at offset {locatorOffset}.");
        }

        var zip64OffsetLong = (long)BinaryPrimitives.ReadUInt64LittleEndian(control.ArchiveBytes.AsSpan((int)locatorOffset + 8, 8));
        if (zip64OffsetLong > int.MaxValue
            || BinaryPrimitives.ReadUInt32LittleEndian(control.ArchiveBytes.AsSpan((int)zip64OffsetLong, 4)) != ArchiveFixtureLayout.Zip64EocdSignature)
        {
            throw new InvalidOperationException(
                $"Archive Test case '{caseKey}': expected the Zip64 EOCD signature at offset {zip64OffsetLong}.");
        }

        return zip64OffsetLong;
    }

    /// <summary>
    /// Makes the Zip64 EOCD entry counts disagree (ticket #933): the 8-byte
    /// total becomes 2 while the this-disk count stays 1 and the central
    /// directory physically holds 1 entry.
    /// </summary>
    private static MutatedArchiveFixture DeclareZip64EocdEntryCountMismatch(ArchiveFixtureArtifact control, byte[] before)
    {
        const ulong DeclaredTotal = 2;
        var code = ArchiveTestMutationKind.Zip64EocdEntryCountMismatch.ToCaseKey();
        var totalOffset = Zip64EocdOffset(control, code) + 32;

        return Patch(ArchiveTestMutationKind.Zip64EocdEntryCountMismatch, before,
        [
            new MutationSpec(
                code, "zip64-eocd", totalOffset,
                "Rewrites the Zip64 EOCD 8-byte total entry count from 1 to 2 while the this-disk count stays 1 and the central directory holds 1 entry.",
                null, 8, 8, DeclaredValue: "zip64-total-entries=2"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt64LittleEndian(after.AsSpan((int)totalOffset, 8), DeclaredTotal);
        });
    }

    /// <summary>
    /// Makes the Zip64 locator declare more disks than exist (ticket #933): the
    /// total-disks field becomes 2 while every disk reference stays 0 in a
    /// single flat file.
    /// </summary>
    private static MutatedArchiveFixture DeclareZip64LocatorDiskMismatch(ArchiveFixtureArtifact control, byte[] before)
    {
        const uint DeclaredDisks = 2;
        var code = ArchiveTestMutationKind.Zip64LocatorDiskMismatch.ToCaseKey();
        _ = Zip64EocdOffset(control, code);
        var totalDisksOffset = control.Layout.EocdOffset - 20 + 16;

        return Patch(ArchiveTestMutationKind.Zip64LocatorDiskMismatch, before,
        [
            new MutationSpec(
                code, "zip64-locator", totalDisksOffset,
                "Rewrites the Zip64 locator total-disks field from 1 to 2 while every disk reference stays 0 in a single flat file.",
                null, 4, 4, DeclaredValue: "zip64-total-disks=2"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)totalDisksOffset, 4), DeclaredDisks);
        });
    }

    /// <summary>
    /// Flips a bit in the data descriptor CRC (ticket #934): the descriptor
    /// disagrees with both headers, which still carry the true CRC. Readers
    /// that verify CRC reject; streaming readers that never check pass on.
    /// </summary>
    private static MutatedArchiveFixture DisagreeDescriptorCrc(ArchiveFixtureArtifact control, byte[] before)
    {
        var descriptorOffset = DescriptorOffset(control, nameof(ArchiveTestMutationKind.DescriptorCrcDisagreement));
        var crcOffset = descriptorOffset + 4;
        var code = ArchiveTestMutationKind.DescriptorCrcDisagreement.ToCaseKey();

        return Patch(ArchiveTestMutationKind.DescriptorCrcDisagreement, before,
        [
            new MutationSpec(
                code, "data-descriptor", crcOffset,
                "Flips a bit in the data descriptor CRC while both headers keep the true CRC; the descriptor is the lone liar.",
                control.Layout.Entries[0].Ordinal, 1, 1, DeclaredValue: "descriptor-crc-corrupt"),
        ], after => after[crcOffset] ^= 0x01);
    }

    /// <summary>
    /// Rewrites the data descriptor compressed size (ticket #934): the
    /// descriptor disagrees with both headers over framing-critical bytes.
    /// </summary>
    private static MutatedArchiveFixture DisagreeDescriptorSize(ArchiveFixtureArtifact control, byte[] before)
    {
        var descriptorOffset = DescriptorOffset(control, nameof(ArchiveTestMutationKind.DescriptorSizeDisagreement));
        var sizeOffset = descriptorOffset + 8;
        var actual = BinaryPrimitives.ReadUInt32LittleEndian(before.AsSpan((int)sizeOffset, 4));
        const uint Declared = 16;
        var code = ArchiveTestMutationKind.DescriptorSizeDisagreement.ToCaseKey();

        return Patch(ArchiveTestMutationKind.DescriptorSizeDisagreement, before,
        [
            new MutationSpec(
                code, "data-descriptor", sizeOffset,
                $"Rewrites the data descriptor compressed size from {actual} to {actual + Declared} while both headers keep the true size.",
                control.Layout.Entries[0].Ordinal, 4, 4, DeclaredValue: $"descriptor-compressed-size={actual + Declared} actual-compressed-size={actual}"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)sizeOffset, 4), actual + Declared);
        });
    }

    /// <summary>Requires the control's single entry to carry a data descriptor
    /// with the 4-byte signature, returning its offset.</summary>
    private static long DescriptorOffset(ArchiveFixtureArtifact control, string caller)
    {
        if (control.Layout.Entries.Count != 1 || !control.Layout.Entries[0].HasDataDescriptor)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{caller}': the control must carry exactly one entry with a data descriptor.");
        }

        var offset = control.Layout.Entries[0].DataDescriptorOffset;
        if (BinaryPrimitives.ReadUInt32LittleEndian(control.ArchiveBytes.AsSpan((int)offset, 4)) != 0x08074b50)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{caller}': expected the data descriptor signature at offset {offset}.");
        }

        return offset;
    }

    /// <summary>
    /// Appends a second Zip64 extra subfield with a conflicting uncompressed
    /// size to entry 0's central header (ticket #934): duplicate 0x0001 ids
    /// isolate the first-wins versus last-wins parser decision. The central
    /// header grows 12 bytes; the Zip64 EOCD span and locator offset relink.
    /// </summary>
    private static MutatedArchiveFixture DuplicateZip64ExtraField(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var insertOffset = entry.CentralDirectoryOffset + 46 + entry.CentralNameLength + entry.CentralExtraLength;
        var extraLengthOffset = entry.CentralDirectoryOffset + 30;
        var conflicting = checked((ulong)control.Entries[0].Content.Length + 64);
        var code = ArchiveTestMutationKind.DuplicateZip64Extra.ToCaseKey();

        var zip64Offset = Zip64EocdOffset(control, code);
        var locatorOffset = control.Layout.EocdOffset - 20;
        var after = new byte[checked(before.Length + 12)];
        before.AsSpan()[..(int)insertOffset].CopyTo(after);
        BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)insertOffset, 2), 0x0001);
        BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)insertOffset + 2, 2), 8);
        BinaryPrimitives.WriteUInt64LittleEndian(after.AsSpan((int)insertOffset + 4, 8), conflicting);
        before.AsSpan((int)insertOffset).CopyTo(after.AsSpan((int)insertOffset + 12));

        // Linked changes: the central extra length, the Zip64 EOCD span, and
        // the locator's Zip64 offset all move past the insertion.
        var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(after.AsSpan((int)extraLengthOffset, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)extraLengthOffset, 2), checked((ushort)(extraLength + 12)));
        var spanOffset = zip64Offset + 40 + 12;
        var span = BinaryPrimitives.ReadUInt64LittleEndian(after.AsSpan((int)spanOffset, 8));
        BinaryPrimitives.WriteUInt64LittleEndian(after.AsSpan((int)spanOffset, 8), span + 12);
        var locatorTarget = locatorOffset + 8 + 12;
        var target = BinaryPrimitives.ReadUInt64LittleEndian(after.AsSpan((int)locatorTarget, 8));
        BinaryPrimitives.WriteUInt64LittleEndian(after.AsSpan((int)locatorTarget, 8), target + 12);

        var mutation = Record(
            new MutationSpec(
                code, "central-header", insertOffset,
                $"Appends a second Zip64 extra subfield declaring uncompressed size {conflicting} while the first declares {control.Entries[0].Content.Length}; duplicate ids isolate first-wins versus last-wins.",
                entry.Ordinal, 0, 12, DeclaredValue: $"duplicate-zip64-uncompressed-size={conflicting} actual-content-length={control.Entries[0].Content.Length}"),
            before, after);
        return Finalize(ArchiveTestMutationKind.DuplicateZip64Extra, before, after, [mutation]);
    }

    /// <summary>
    /// Appends a second Info-ZIP Unicode Path subfield with a conflicting name
    /// in both headers (ticket #934): the CRC stays valid over the standard
    /// name, so only name priority is under test. Each header grows by the
    /// subfield length; the data shifts past the local insert and the EOCD
    /// span relinks past both.
    /// </summary>
    private static MutatedArchiveFixture DuplicateUnicodePathField(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var localExtraStart = entry.LocalHeaderOffset + 30 + entry.LocalNameLength;
        var centralExtraStart = entry.CentralDirectoryOffset + 46 + entry.CentralNameLength;
        var (_, localSize) = UnicodePathData(control, localExtraStart, "local", nameof(ArchiveTestMutationKind.DuplicateUnicodePath));
        var (_, centralSize) = UnicodePathData(control, centralExtraStart, "central", nameof(ArchiveTestMutationKind.DuplicateUnicodePath));
        if (localSize != centralSize)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{nameof(ArchiveTestMutationKind.DuplicateUnicodePath)}': the local and central Unicode Path sizes disagree.");
        }

        var growth = checked(4 + localSize);
        var localInsert = localExtraStart + 4 + localSize;
        var centralInsert = centralExtraStart + 4 + centralSize;
        var code = ArchiveTestMutationKind.DuplicateUnicodePath.ToCaseKey();

        // Divergent first name byte in each duplicate: the duplicate carries a
        // different name while its CRC stays valid over the standard name.
        var middle = new byte[checked(before.Length + growth)];
        before.AsSpan()[..(int)localInsert].CopyTo(middle);
        before.AsSpan((int)localExtraStart, growth).CopyTo(middle.AsSpan((int)localInsert));
        middle[(int)localInsert + 9] ^= 0x20;
        before.AsSpan((int)localInsert).CopyTo(middle.AsSpan((int)localInsert + growth));

        var after = new byte[checked(middle.Length + growth)];
        var centralShifted = centralInsert + growth;
        middle.AsSpan()[..(int)centralShifted].CopyTo(after);
        // Source is the duplicate just inserted locally: middle positions past
        // the local insert are shifted, so the before-basis slice would copy
        // neighbors instead of the subfield. Its first name byte already diverges.
        middle.AsSpan((int)localInsert, growth).CopyTo(after.AsSpan((int)centralShifted));
        middle.AsSpan((int)centralShifted).CopyTo(after.AsSpan((int)centralShifted + growth));

        var localExtraLengthOffset = entry.LocalHeaderOffset + 28;
        var localExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(after.AsSpan((int)localExtraLengthOffset, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)localExtraLengthOffset, 2), checked((ushort)(localExtraLength + growth)));
        var centralExtraLengthOffset = entry.CentralDirectoryOffset + growth + 30;
        var centralExtraLength = BinaryPrimitives.ReadUInt16LittleEndian(after.AsSpan((int)centralExtraLengthOffset, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)centralExtraLengthOffset, 2), checked((ushort)(centralExtraLength + growth)));
        var eocdBase = control.Layout.EocdOffset + 2 * growth;
        var eocdOffset = BinaryPrimitives.ReadUInt32LittleEndian(after.AsSpan((int)eocdBase + 16, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)eocdBase + 16, 4), checked(eocdOffset + (uint)growth));
        var eocdSize = BinaryPrimitives.ReadUInt32LittleEndian(after.AsSpan((int)eocdBase + 12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)eocdBase + 12, 4), checked(eocdSize + (uint)growth));

        // One record for both inserts (parallel views must not overlap, and the
        // verifier spot-checks the final record's afterHex at its offset): the
        // local insert sits at the record offset, so its afterHex window is the
        // duplicate subfield header; the explanation carries both offsets.
        var mutation = Record(
            new MutationSpec(
                code, "whole-archive", localInsert,
                $"Appends a second Unicode Path subfield with a one-byte-divergent name in both headers (local at {localInsert}, central at {centralInsert}) while each CRC stays valid over the standard name; no header-asymmetry decision pollutes name priority.",
                entry.Ordinal, 0, 2 * growth, DeclaredValue: "duplicate-unicode-path-name-divergent"),
            before, after);
        return Finalize(ArchiveTestMutationKind.DuplicateUnicodePath, before, after, [mutation]);
    }

    /// <summary>
    /// Sets the UTF-8 flag (bit 11) in both headers over CP437 name bytes
    /// (ticket #934): the flag disagrees with the raw name, which is not valid
    /// UTF-8. In place; nothing moves.
    /// </summary>
    private static MutatedArchiveFixture SetUtf8FlagOnCp437Name(ArchiveFixtureArtifact control, byte[] before)
    {
        const ushort Utf8Flag = 0x0800;
        var entry = control.Layout.Entries[0];
        var localFlagsOffset = entry.LocalHeaderOffset + 6;
        var centralFlagsOffset = entry.CentralDirectoryOffset + 8;
        var code = ArchiveTestMutationKind.Utf8FlagCp437Name.ToCaseKey();

        return Patch(ArchiveTestMutationKind.Utf8FlagCp437Name, before,
        [
            new MutationSpec(
                code, "local-header", localFlagsOffset,
                "Sets the local header UTF-8 flag over CP437 name bytes that are not valid UTF-8; the raw name still carries 0x82.",
                entry.Ordinal, 2, 2, DeclaredValue: "utf-8-flag-set-over-cp437-name"),
            new MutationSpec(
                code, "central-header", centralFlagsOffset,
                "Sets the central header UTF-8 flag alongside the local header; the disagreement is flag versus bytes, not header versus header.",
                entry.Ordinal, 2, 2, DeclaredValue: "utf-8-flag-set-over-cp437-name"),
        ], after =>
        {
            var local = BinaryPrimitives.ReadUInt16LittleEndian(after.AsSpan((int)localFlagsOffset, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)localFlagsOffset, 2), (ushort)(local | Utf8Flag));
            var central = BinaryPrimitives.ReadUInt16LittleEndian(after.AsSpan((int)centralFlagsOffset, 2));
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)centralFlagsOffset, 2), (ushort)(central | Utf8Flag));
        });
    }

    /// <summary>Locates the 0x7075 subfield data in the given header's extra
    /// area, verifying the tag; returns the data start and size.</summary>
    private static (long DataStart, int Size) UnicodePathData(ArchiveFixtureArtifact control, long extraStart, string header, string caller)
    {
        var tagOffset = extraStart;
        if (BinaryPrimitives.ReadUInt16LittleEndian(control.ArchiveBytes.AsSpan((int)tagOffset, 2)) != 0x7075)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{caller}': expected the Unicode Path subfield first in the {header} extra area.");
        }

        var size = BinaryPrimitives.ReadUInt16LittleEndian(control.ArchiveBytes.AsSpan((int)tagOffset + 2, 2));
        if (size < 5 + 1)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{caller}': the Unicode Path subfield is {size} bytes, too small to mutate.");
        }

        return (tagOffset + 4, size);
    }

    /// <summary>
    /// Flips a bit in the Unicode Path CRC in both headers (ticket #934): the
    /// CRC lies while every name agrees, isolating CRC verification from name
    /// priority. In place; nothing moves.
    /// </summary>
    private static MutatedArchiveFixture MismatchUnicodePathCrc(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var localExtraStart = entry.LocalHeaderOffset + 30 + entry.LocalNameLength;
        var centralExtraStart = entry.CentralDirectoryOffset + 46 + entry.CentralNameLength;
        var (localData, _) = UnicodePathData(control, localExtraStart, "local", nameof(ArchiveTestMutationKind.UnicodePathCrcMismatch));
        var (centralData, _) = UnicodePathData(control, centralExtraStart, "central", nameof(ArchiveTestMutationKind.UnicodePathCrcMismatch));
        var localCrcOffset = localData + 1;
        var centralCrcOffset = centralData + 1;
        var code = ArchiveTestMutationKind.UnicodePathCrcMismatch.ToCaseKey();

        return Patch(ArchiveTestMutationKind.UnicodePathCrcMismatch, before,
        [
            new MutationSpec(
                code, "local-header", localCrcOffset,
                "Flips a bit in the local Unicode Path CRC while the standard and Unicode names agree everywhere; only CRC verification is under test.",
                entry.Ordinal, 1, 1, DeclaredValue: "unicode-path-crc-corrupt"),
            new MutationSpec(
                code, "central-header", centralCrcOffset,
                "Flips the same bit in the central Unicode Path CRC alongside the local header.",
                entry.Ordinal, 1, 1, DeclaredValue: "unicode-path-crc-corrupt"),
        ], after =>
        {
            after[localCrcOffset] ^= 0x01;
            after[centralCrcOffset] ^= 0x01;
        });
    }

    /// <summary>
    /// Changes one byte of the Unicode name in both headers (ticket #934): the
    /// CRC stays valid over the standard name while the Unicode names diverge
    /// from it, isolating name priority from CRC verification. Same length;
    /// nothing moves.
    /// </summary>
    private static MutatedArchiveFixture DivergeUnicodePathName(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        var localExtraStart = entry.LocalHeaderOffset + 30 + entry.LocalNameLength;
        var centralExtraStart = entry.CentralDirectoryOffset + 46 + entry.CentralNameLength;
        var (localData, localSize) = UnicodePathData(control, localExtraStart, "local", nameof(ArchiveTestMutationKind.UnicodePathNameDivergence));
        var (centralData, centralSize) = UnicodePathData(control, centralExtraStart, "central", nameof(ArchiveTestMutationKind.UnicodePathNameDivergence));
        if (localSize != centralSize)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{nameof(ArchiveTestMutationKind.UnicodePathNameDivergence)}': the local and central Unicode Path sizes disagree.");
        }

        var localNameOffset = localData + 5;
        var centralNameOffset = centralData + 5;
        var code = ArchiveTestMutationKind.UnicodePathNameDivergence.ToCaseKey();

        return Patch(ArchiveTestMutationKind.UnicodePathNameDivergence, before,
        [
            new MutationSpec(
                code, "local-header", localNameOffset,
                "Changes the first byte of the local Unicode name while its length and CRC stay consistent with the standard name.",
                entry.Ordinal, 1, 1, DeclaredValue: "unicode-path-name-divergent"),
            new MutationSpec(
                code, "central-header", centralNameOffset,
                "Changes the first byte of the central Unicode name alongside the local header.",
                entry.Ordinal, 1, 1, DeclaredValue: "unicode-path-name-divergent"),
        ], after =>
        {
            after[localNameOffset] ^= 0x20;
            after[centralNameOffset] ^= 0x20;
        });
    }

    /// <summary>
    /// Declares an uncompressed size beyond the 32 MiB expanded budget (ticket #843) in
    /// both headers while the physical content stays tiny: a pure declaration lie. The
    /// declared value is recorded separately from the actual known content length, and
    /// no reader may allocate or read to the untrusted declared size.
    /// </summary>
    private static MutatedArchiveFixture DeclareOversizedUncompressedSize(ArchiveFixtureArtifact control, byte[] before)
    {
        const uint Oversized = 32 * 1024 * 1024 + 1;
        var entry = control.Layout.Entries[0];
        var localSizeOffset = entry.LocalHeaderOffset + 22;
        var centralSizeOffset = entry.CentralDirectoryOffset + 24;
        var code = ArchiveTestMutationKind.DeclaredSizeOversized.ToCaseKey();

        return Patch(ArchiveTestMutationKind.DeclaredSizeOversized, before,
        [
            new MutationSpec(
                code, "local-header", localSizeOffset,
                $"Rewrites the local header's uncompressed size to {Oversized} bytes, beyond the {32 * 1024 * 1024}-byte expanded budget, while the entry physically holds {control.Entries[0].Content.Length} bytes; applied in parallel with the central-header record, not chained.",
                entry.Ordinal, 4, 4, DeclaredValue: $"declared-uncompressed-size={Oversized} actual-content-length={control.Entries[0].Content.Length}"),
            new MutationSpec(
                code, "central-header", centralSizeOffset,
                "Rewrites the central header's uncompressed size to the same oversized declaration; the lie is identical in both headers.",
                entry.Ordinal, 4, 4, DeclaredValue: $"declared-uncompressed-size={Oversized} actual-content-length={control.Entries[0].Content.Length}"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)localSizeOffset, 4), Oversized);
            BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)centralSizeOffset, 4), Oversized);
        });
    }

    /// <summary>
    /// Marks both headers' names UTF-8 (bit 11) while the raw name bytes are a fixed
    /// invalid UTF-8 sequence (0xC3 followed by a non-continuation byte): the sidecar's
    /// raw hex stays representable, and the physical name bytes are the defect (ticket
    /// #841 step 4).
    /// </summary>
    private static MutatedArchiveFixture InvalidateUtf8Name(ArchiveFixtureArtifact control, byte[] before)
    {
        const ushort Utf8Flag = 0x0800;
        var entry = control.Layout.Entries[0];
        var localNameOffset = entry.LocalHeaderOffset + 30;
        var centralNameOffset = entry.CentralDirectoryOffset + 46;
        var localFlagsOffset = entry.LocalHeaderOffset + 6;
        var centralFlagsOffset = entry.CentralDirectoryOffset + 8;
        var nameLength = entry.LocalNameLength;
        var invalidName = BuildInvalidUtf8Name(nameLength);
        var code = ArchiveTestMutationKind.InvalidUtf8Name.ToCaseKey();

        return Patch(ArchiveTestMutationKind.InvalidUtf8Name, before,
        [
            new MutationSpec(
                code, "local-header", localNameOffset,
                $"Replaces the local header's {nameLength}-byte name with a fixed invalid UTF-8 sequence (0xC3 followed by a non-continuation byte) while setting the UTF-8 flag (bit 11); the central header's name and flag are rewritten identically. The sidecar's raw name hex stays representable.",
                entry.Ordinal, nameLength, nameLength, DeclaredValue: $"local-name-hex={Convert.ToHexStringLower(invalidName)} utf8-flag=true"),
            new MutationSpec(
                code, "central-header", centralNameOffset,
                "Replaces the central header's name bytes with the same fixed invalid UTF-8 sequence; applied in parallel with the local-header record, not chained.",
                entry.Ordinal, nameLength, nameLength, DeclaredValue: $"central-name-hex={Convert.ToHexStringLower(invalidName)} utf8-flag=true"),
            new MutationSpec(
                code, "local-header", localFlagsOffset,
                "Sets the UTF-8 flag (general-purpose bit 11) on the local header, declaring an encoding the name bytes do not satisfy.",
                entry.Ordinal, 2, 2, DeclaredValue: "general-purpose-bits+=0x0800"),
            new MutationSpec(
                code, "central-header", centralFlagsOffset,
                "Sets the UTF-8 flag (general-purpose bit 11) on the central header; applied in parallel with the local-header record, not chained.",
                entry.Ordinal, 2, 2, DeclaredValue: "general-purpose-bits+=0x0800"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)localFlagsOffset, 2), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(after.AsSpan((int)localFlagsOffset, 2)) | Utf8Flag));
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)centralFlagsOffset, 2), (ushort)(BinaryPrimitives.ReadUInt16LittleEndian(after.AsSpan((int)centralFlagsOffset, 2)) | Utf8Flag));
            WriteBytes(after, localNameOffset, invalidName);
            WriteBytes(after, centralNameOffset, invalidName);
        });
    }

    /// <summary>
    /// A same-length name whose bytes are invalid UTF-8: 0xC3 0x28 pairs (a lead byte
    /// followed by a non-continuation byte), padded with ASCII if the name is odd-length.
    /// </summary>
    private static byte[] BuildInvalidUtf8Name(int nameLength)
    {
        var bytes = new byte[nameLength];
        for (var i = 0; i + 1 < nameLength; i += 2)
        {
            bytes[i] = 0xC3;
            bytes[i + 1] = 0x28;
        }

        if (nameLength % 2 == 1)
        {
            bytes[^1] = 0x74;  // 't'
        }

        return bytes;
    }

    /// <summary>
    /// Sets the central header's extra-length to 0 while the sizes stay Zip64 sentinels:
    /// the required Zip64 extended-information field is missing, so a resolving reader
    /// has no values to read. The 28 extra bytes remain in place as trailing bytes the
    /// declared central span no longer covers.
    /// </summary>
    private static MutatedArchiveFixture HideZip64Extra(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries[0];
        if (entry.CentralExtraLength is not 28)
        {
            throw new InvalidOperationException(
                $"zip64-missing-extra expects the control's entry 0 to carry the 28-byte central Zip64 extra field; found {entry.CentralExtraLength}.");
        }

        var centralExtraLengthOffset = entry.CentralDirectoryOffset + 30;
        var code = ArchiveTestMutationKind.Zip64MissingExtra.ToCaseKey();

        return Patch(ArchiveTestMutationKind.Zip64MissingExtra, before,
        [
            new MutationSpec(
                code, "central-header", centralExtraLengthOffset,
                $"Sets the central header's extra-length from 28 to 0 while the compressed, uncompressed, and local-header-offset fields stay Zip64 sentinels (0xFFFFFFFF): the required Zip64 extended-information field is missing, so a resolving reader has nothing to read. The 28 extra bytes remain physically present as trailing bytes outside the declared central span.",
                entry.Ordinal, 2, 2, DeclaredValue: "central-extra-length=0 sizes-remain=0xFFFFFFFF"),
        ], after => BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)centralExtraLengthOffset, 2), 0));
    }

    /// <summary>
    /// Rewrites the central Zip64 extra subfield's data size from 24 to 30, claiming
    /// more bytes than the enclosing 28-byte extra area holds (4-byte subfield header +
    /// 24 data bytes). The claim stays tiny, so a defensive reader must not allocate
    /// based on it (ticket #841 test plan).
    /// </summary>
    private static MutatedArchiveFixture OverrunZip64Extra(ArchiveFixtureArtifact control, byte[] before)
    {
        const ushort claimedSubfieldSize = 30;
        var entry = control.Layout.Entries[0];
        if (entry.CentralExtraLength is not 28)
        {
            throw new InvalidOperationException(
                $"zip64-truncated-extra expects the control's entry 0 to carry the 28-byte central Zip64 extra field; found {entry.CentralExtraLength}.");
        }

        var subfieldSizeOffset = entry.CentralDirectoryOffset + 46 + entry.CentralNameLength + 2;
        var code = ArchiveTestMutationKind.Zip64TruncatedExtra.ToCaseKey();

        return Patch(ArchiveTestMutationKind.Zip64TruncatedExtra, before,
        [
            new MutationSpec(
                code, "central-header", subfieldSizeOffset,
                $"Rewrites the central Zip64 extra subfield's data size from 24 to {claimedSubfieldSize}, claiming 6 more bytes than the enclosing 28-byte extra area holds (4-byte subfield header + 24 data bytes); the enclosing header lengths stay intact so the overrun is the only defect. The claim stays tiny: a defensive reader must never allocate from it.",
                entry.Ordinal, 2, 2, DeclaredValue: $"zip64-subfield-size={claimedSubfieldSize} enclosing-extra-area=28"),
        ], after => BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)subfieldSizeOffset, 2), claimedSubfieldSize));
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
    private static MutatedArchiveFixture DeclareUnsupportedMethod(ArchiveFixtureArtifact control, byte[] before) =>
        DeclareMethodCode(control, before, ArchiveTestMutationKind.UnsupportedMethod, UnsupportedMethodCode, "PPMd");

    /// <summary>
    /// Rewrites the compression method in both headers to Deflate64 (9) over stored
    /// payload bytes (ticket #877): consistent codes the reference reader cannot
    /// decode, mirroring the PPMd unsupported-method case for the Azure Data
    /// Factory / Synapse ingestion gap. A genuinely valid Deflate64 stream is the
    /// separate valid-deflate64 control (ticket #898).
    /// </summary>
    private static MutatedArchiveFixture DeclareUnsupportedMethodDeflate64(ArchiveFixtureArtifact control, byte[] before) =>
        DeclareMethodCode(control, before, ArchiveTestMutationKind.UnsupportedMethodDeflate64, UnsupportedMethodDeflate64Code, "Deflate64");

    private static MutatedArchiveFixture DeclareMethodCode(
        ArchiveFixtureArtifact control, byte[] before, ArchiveTestMutationKind kind, ushort code, string name)
    {
        var entry = control.Layout.Entries[0];
        var localMethodOffset = entry.LocalHeaderOffset + 8;
        var centralMethodOffset = entry.CentralDirectoryOffset + 10;

        return Patch(kind, before,
        [
            new MutationSpec(
                kind.ToCaseKey(), "local-header", localMethodOffset,
                $"Rewrites the local header's compression method from {entry.Method} to {code} ({name}), consistent with the central header; the payload bytes are unchanged stored data and no codec for the declared method is implemented. Applied in parallel with the central-header record, not chained.",
                entry.Ordinal, 2, 2, DeclaredValue: $"method={code}"),
            new MutationSpec(
                kind.ToCaseKey(), "central-header", centralMethodOffset,
                $"Rewrites the central header's compression method from {entry.Method} to {code} ({name}), consistent with the local header; the payload bytes are unchanged stored data and no codec for the declared method is implemented. Applied in parallel with the local-header record, not chained.",
                entry.Ordinal, 2, 2, DeclaredValue: $"method={code}"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)localMethodOffset, 2), code);
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)centralMethodOffset, 2), code);
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

    /// <summary>
    /// Inserts a complete hidden stored Local File Header plus payload at offset 0 before
    /// the control's bytes, with no Central Directory Header for the hidden entry (ticket
    /// #869, ZipDiff Type c1: no_cdh_for_lfh). All central relative-local-header offsets
    /// and the EOCD central-directory offset shift by the inserted length; EOCD entry
    /// counts and central size stay unchanged (only visible entries are indexed).
    /// Streaming readers see the hidden entry; CD-driven readers list only visibles.
    /// </summary>
    private static MutatedArchiveFixture InsertOrphanLocalHeader(ArchiveFixtureArtifact control, byte[] before)
    {
        byte[] hiddenName = "hidden.sh"u8.ToArray();
        byte[] hiddenPayload = "atc-hidden-payload"u8.ToArray();
        var hiddenLength = checked(30 + hiddenName.Length + hiddenPayload.Length);

        if (control.Layout.Entries.Count == 0)
        {
            throw new InvalidOperationException("orphan-local-header expects a control with at least one entry.");
        }

        var eocdOffset = control.Layout.EocdOffset;
        var thisDiskEntries = BinaryPrimitives.ReadUInt16LittleEndian(before.AsSpan((int)eocdOffset + 8, 2));
        var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(before.AsSpan((int)eocdOffset + 10, 2));
        var centralSize = BinaryPrimitives.ReadUInt32LittleEndian(before.AsSpan((int)eocdOffset + 12, 4));
        var centralOffset = BinaryPrimitives.ReadUInt32LittleEndian(before.AsSpan((int)eocdOffset + 16, 4));
        if (thisDiskEntries == 0xFFFF || totalEntries == 0xFFFF || centralSize == 0xFFFFFFFF || centralOffset == 0xFFFFFFFF)
        {
            throw new InvalidOperationException("orphan-local-header does not support Zip64 controls with sentinel EOCD fields.");
        }

        var hidden = new byte[hiddenLength];
        Span<byte> h = hidden;
        BinaryPrimitives.WriteUInt32LittleEndian(h, 0x04034b50);
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(4), 20);
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(6), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(8), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(10), 0x0000);
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(12), 0x5821);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(14), ArchiveFixtureBuilder.Crc32(hiddenPayload));
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(18), (uint)hiddenPayload.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(h.Slice(22), (uint)hiddenPayload.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(26), (ushort)hiddenName.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(h.Slice(28), 0);
        hiddenName.CopyTo(h.Slice(30));
        hiddenPayload.CopyTo(h.Slice(30 + hiddenName.Length));

        var after = new byte[checked(before.Length + hiddenLength)];
        hidden.CopyTo(after, 0);
        before.CopyTo(after, hiddenLength);

        foreach (var entry in control.Layout.Entries)
        {
            var centralRelativeOffset = (int)entry.CentralDirectoryOffset + hiddenLength + 42;
            var relative = BinaryPrimitives.ReadUInt32LittleEndian(after.AsSpan(centralRelativeOffset, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan(centralRelativeOffset, 4), checked(relative + (uint)hiddenLength));
        }

        var newEocdOffset = (int)eocdOffset + hiddenLength;
        var newCentralOffset = BinaryPrimitives.ReadUInt32LittleEndian(after.AsSpan(newEocdOffset + 16, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan(newEocdOffset + 16, 4), checked(newCentralOffset + (uint)hiddenLength));

        var code = ArchiveTestMutationKind.OrphanLocalHeader.ToCaseKey();
        var hiddenNameHex = Convert.ToHexStringLower(hiddenName);
        var hiddenPayloadSha = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(hiddenPayload));
        var revisedCentralOffset = centralOffset + (uint)hiddenLength;
        var mutation = new ArchiveTestMutation(
            Code: code,
            Structure: "local-header",
            OffsetBasis: "before-mutation",
            Offset: 0,
            Explanation: $"Inserts a complete hidden stored LFH plus {hiddenPayload.Length}-byte payload for '{System.Text.Encoding.UTF8.GetString(hiddenName)}' at offset 0 with no Central Directory Header; all {control.Layout.Entries.Count} central relative-local-header offsets and the EOCD central-directory offset shift by +{hiddenLength} (counts stay {totalEntries}, central size stays {centralSize}). Streaming readers extract the hidden file; CD-driven readers list only the visible entries.",
            Ordinal: null,
            DeletedLength: 0,
            InsertedLength: hiddenLength,
            BeforeSize: before.Length,
            AfterSize: after.Length,
            BeforeSha256: Hash(before),
            AfterSha256: Hash(after),
            BeforeHex: null,
            AfterHex: Convert.ToHexStringLower(hidden),
            DeclaredValue: $"hidden-lfh-offset=0 hidden-name-hex={hiddenNameHex} hidden-payload-sha256={hiddenPayloadSha} eocd-central-directory-offset={revisedCentralOffset} eocd-entry-counts={totalEntries}");

        return Finalize(ArchiveTestMutationKind.OrphanLocalHeader, before, after, [mutation]);
    }

    /// <summary>
    /// Inserts the 64-byte non-Archive stub at offset 0 without rebasing any offset
    /// (ticket #873): every central relative-local-header offset and the EOCD offset
    /// still point at their pre-prefix positions, so declared local headers land in
    /// the stub or mid-structure. The offset-desynchronization counterpart to the
    /// rebased prefixed-archive construction.
    /// </summary>
    private static MutatedArchiveFixture InsertPrefixWithoutRebase(ArchiveFixtureArtifact control, byte[] before)
    {
        var prefix = ArchiveFixtureBuilder.ArchivePrefixStub;
        if (control.Layout.Entries.Count == 0)
        {
            throw new InvalidOperationException("prefix-unrebased expects a control with at least one entry.");
        }

        var after = new byte[checked(before.Length + prefix.Length)];
        prefix.CopyTo(after, 0);
        before.CopyTo(after, prefix.Length);

        var code = ArchiveTestMutationKind.PrefixUnrebased.ToCaseKey();
        var staleLocals = string.Join(",", control.Layout.Entries.Select(e => e.LocalHeaderOffset));
        var mutation = new ArchiveTestMutation(
            Code: code,
            Structure: "whole-archive",
            OffsetBasis: "before-mutation",
            Offset: 0,
            Explanation: $"Inserts the {prefix.Length}-byte non-Archive stub at offset 0 without rebasing offsets: the EOCD central-directory offset still addresses its pre-prefix position (now shifted payload bytes, not central headers), so strict readers cannot even walk the directory; every central relative-local-header offset is likewise stale.",
            Ordinal: null,
            DeletedLength: 0,
            InsertedLength: prefix.Length,
            BeforeSize: before.Length,
            AfterSize: after.Length,
            BeforeSha256: Hash(before),
            AfterSha256: Hash(after),
            BeforeHex: null,
            AfterHex: Convert.ToHexStringLower(prefix),
            DeclaredValue: $"prefix-length={prefix.Length} rebased=false eocd-central-directory-offset={control.Layout.CentralDirectoryOffset} central-relative-local-header-offsets={staleLocals}");

        return Finalize(ArchiveTestMutationKind.PrefixUnrebased, before, after, [mutation]);
    }

    /// Sets the first DEFLATE-family block's type to the reserved value 3 (tickets
    /// #874 and #900): bits 1-2 of the first compressed byte become 11. Works on any
    /// block kind — stored, fixed, or dynamic — since every block opens with the same
    /// 3-bit header. The framing stays intact; only the decoder's block dispatch fails.
    /// </summary>
    private static MutatedArchiveFixture CorruptDeflateBtype(ArchiveFixtureArtifact control, byte[] before, ArchiveTestMutationKind kind)
    {
        var entry = DeflateEntry(control, kind);
        if (entry.CompressedSize < 1)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{kind.ToCaseKey()}': the control's compressed payload is empty, no block header to corrupt.");
        }

        var blockOffset = entry.DataOffset;
        var code = kind.ToCaseKey();
        var explanation = kind == ArchiveTestMutationKind.DeflateInvalidBtype
            ? $"Sets the first DEFLATE block's type bits to 11 (reserved): no decoder dispatches it, so entry reads fail while listing and structural walks still succeed."
            : $"Sets the first block's type bits to 11 (reserved): no decoder dispatches it, so entry reads fail while listing and structural walks still succeed.";

        return Patch(kind, before,
        [
            new MutationSpec(
                code, "file-data", blockOffset,
                explanation,
                entry.Ordinal, 1, 1, DeclaredValue: "btype=3 (reserved)"),
        ], after => after[(int)blockOffset] = (byte)((after[(int)blockOffset] & ~0x06) | 0x06));
    }

    /// <summary>
    /// Flips a bit in the first code-length code length of a dynamic Huffman block
    /// (ticket #874): bit 17 of the stream (byte 2, order[0] per RFC 1951 §3.2.7).
    /// The header is parsed, not assumed — HLIT, HDIST, and HCLEN are read and the
    /// pre-mutation BTYPE must be 10 (dynamic), otherwise the control no longer has
    /// the documented layout and the recipe fails loudly instead of corrupting an
    /// unknown field.
    /// </summary>
    private static MutatedArchiveFixture CorruptDeflateHuffman(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = DeflateEntry(control, ArchiveTestMutationKind.DeflateCorruptHuffman);
        if (entry.CompressedSize < 3)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{ArchiveTestMutationKind.DeflateCorruptHuffman.ToCaseKey()}': the control's compressed payload is {entry.CompressedSize} bytes, too small for a dynamic-block header.");
        }

        var header = (long)before[(int)entry.DataOffset]
            | ((long)before[checked((int)entry.DataOffset + 1)] << 8)
            | ((long)before[checked((int)entry.DataOffset + 2)] << 16);
        if (((header >> 1) & 0x03) != 0x02)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{ArchiveTestMutationKind.DeflateCorruptHuffman.ToCaseKey()}': the control's first block is not dynamic (BTYPE {((header >> 1) & 0x03)}); refusing to corrupt an undocumented field.");
        }

        var hlit = ((header >> 3) & 0x1F) + 257;
        var hdist = ((header >> 8) & 0x1F) + 1;
        var hclen = ((header >> 13) & 0x0F) + 4;
        var bitOffset = entry.DataOffset + 2;
        var code = ArchiveTestMutationKind.DeflateCorruptHuffman.ToCaseKey();

        return Patch(ArchiveTestMutationKind.DeflateCorruptHuffman, before,
        [
            new MutationSpec(
                code, "file-data", bitOffset,
                $"Flips bit 1 of the first dynamic-block code-length code length (order[0]): the documented header reads HLIT {hlit}, HDIST {hdist}, HCLEN {hclen} with BTYPE 10, so decoders build corrupt Huffman tables and entry reads fail.",
                entry.Ordinal, 1, 1, DeclaredValue: $"dynamic-hlit={hlit} dynamic-hdist={hdist} dynamic-hclen={hclen}"),
        ], after => after[(int)bitOffset] ^= 0x02);
    }

    /// <summary>The control's first entry as a DEFLATE-family payload mutation target
    /// (methods 8 and 9 share the 3-bit block header the bit edits address).</summary>
    private static ArchiveFixtureEntryLayout DeflateEntry(ArchiveFixtureArtifact control, ArchiveTestMutationKind kind)
    {
        if (control.Layout.Entries.Count == 0 || (control.Layout.Entries[0].Method != 8 && control.Layout.Entries[0].Method != 9))
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{kind.ToCaseKey()}': the control's entry 0 must be a DEFLATE-family entry.");
        }

        return control.Layout.Entries[0];
    }

    /// <summary>Requires the entry's stream to open with single-block BZip2
    /// magic ("BZh9" file magic, "1AY&amp;SY" block magic), returning nothing:
    /// the shared pin for whole-stream and mixed-member BZip2 corruption.</summary>
    private static void RequireBzip2Magic(byte[] before, ArchiveFixtureEntryLayout entry, ArchiveTestMutationKind kind)
    {
        if (entry.CompressedSize < 10
            || before[(int)entry.DataOffset] != 0x42
            || before[(int)entry.DataOffset + 1] != 0x5A
            || before[(int)entry.DataOffset + 2] != 0x68
            || before[(int)entry.DataOffset + 3] is < 0x31 or > 0x39
            || before[(int)entry.DataOffset + 4] != 0x31
            || before[(int)entry.DataOffset + 5] != 0x41
            || before[(int)entry.DataOffset + 6] != 0x59
            || before[(int)entry.DataOffset + 7] != 0x26
            || before[(int)entry.DataOffset + 8] != 0x53
            || before[(int)entry.DataOffset + 9] != 0x59)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{kind.ToCaseKey()}': the entry's payload is not a single-block BZip2 stream.");
        }
    }

    /// <summary>
    /// Corrupts the BZip2 member of a mixed-method Archive (ticket #935): the
    /// same block-magic flip as the whole-stream case, addressed by method
    /// instead of ordinal 0, so the Store and Deflate siblings stay healthy
    /// and independently readable.
    /// </summary>
    private static MutatedArchiveFixture CorruptMixedBzip2Member(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = control.Layout.Entries.SingleOrDefault(e => e.Method == 12)
            ?? throw new InvalidOperationException(
                $"Archive Test mutation '{ArchiveTestMutationKind.MixedMethodsOneCorruptMember.ToCaseKey()}': the control has no method-12 member.");
        RequireBzip2Magic(before, entry, ArchiveTestMutationKind.MixedMethodsOneCorruptMember);

        var magicOffset = entry.DataOffset + 4;
        var code = ArchiveTestMutationKind.MixedMethodsOneCorruptMember.ToCaseKey();

        return Patch(ArchiveTestMutationKind.MixedMethodsOneCorruptMember, before,
        [
            new MutationSpec(
                code, "file-data", magicOffset,
                $"Flips a bit in the BZip2 member's (ordinal {entry.Ordinal}) block-header magic; the Store and Deflate siblings decode untouched.",
                entry.Ordinal, 1, 1, DeclaredValue: "bzip2-member-block-magic-corrupt"),
        ], after => after[magicOffset] ^= 0x01);
    }

    /// <summary>
    /// Declares the BZip2 member of a mixed-method Archive as reserved method
    /// 98 in both headers (ticket #935): the Store and Deflate siblings stay
    /// readable while the member is cleanly unsupported, distinct from
    /// corruption under every reader profile.
    /// </summary>
    private static MutatedArchiveFixture DeclareMixedUnsupportedMember(ArchiveFixtureArtifact control, byte[] before)
    {
        const ushort UnsupportedCode = 98;
        var entry = control.Layout.Entries.SingleOrDefault(e => e.Method == 12)
            ?? throw new InvalidOperationException(
                $"Archive Test mutation '{ArchiveTestMutationKind.MixedMethodsOneUnsupportedMember.ToCaseKey()}': the control has no method-12 member.");
        var localMethodOffset = entry.LocalHeaderOffset + 8;
        var centralMethodOffset = entry.CentralDirectoryOffset + 10;
        var code = ArchiveTestMutationKind.MixedMethodsOneUnsupportedMember.ToCaseKey();

        return Patch(ArchiveTestMutationKind.MixedMethodsOneUnsupportedMember, before,
        [
            new MutationSpec(
                code, "local-header", localMethodOffset,
                $"Rewrites the BZip2 member's (ordinal {entry.Ordinal}) local method to {UnsupportedCode}; the Store and Deflate siblings stay readable.",
                entry.Ordinal, 2, 2, DeclaredValue: $"method={UnsupportedCode}"),
            new MutationSpec(
                code, "central-header", centralMethodOffset,
                $"Rewrites the BZip2 member's (ordinal {entry.Ordinal}) central method to {UnsupportedCode} alongside the local header.",
                entry.Ordinal, 2, 2, DeclaredValue: $"method={UnsupportedCode}"),
        ], after =>
        {
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)localMethodOffset, 2), UnsupportedCode);
            BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)centralMethodOffset, 2), UnsupportedCode);
        });
    }

    /// <summary>
    /// Flips a bit in the BZip2 block-header magic (ticket #900): the first block
    /// magic byte ("1AY&SY" at stream offset +4, after the "BZh9" file magic). No
    /// decoder starts a block without it; the ZIP framing stays intact.
    /// </summary>
    private static MutatedArchiveFixture CorruptBzip2Magic(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = CodedEntry(control, ArchiveTestMutationKind.Bzip2CorruptBlockMagic, 12);
        RequireBzip2Magic(before, entry, ArchiveTestMutationKind.Bzip2CorruptBlockMagic);

        var magicOffset = entry.DataOffset + 4;
        var code = ArchiveTestMutationKind.Bzip2CorruptBlockMagic.ToCaseKey();

        return Patch(ArchiveTestMutationKind.Bzip2CorruptBlockMagic, before,
        [
            new MutationSpec(
                code, "file-data", magicOffset,
                "Flips a bit in the first BZip2 block-header magic: decoders reject the stream at block start while listing and structural walks still succeed.",
                entry.Ordinal, 1, 1, DeclaredValue: "bzip2-block-magic-corrupt"),
        ], after => after[magicOffset] ^= 0x01);
    }

    /// <summary>
    /// Flips a bit in the BZip2 stored block CRC (ticket #900): the four bytes right
    /// after the six-byte block magic. The magic pin above already guarantees the
    /// layout; decoders comparing the computed CRC against this stored value reject
    /// the block.
    /// </summary>
    private static MutatedArchiveFixture CorruptBzip2Crc(ArchiveFixtureArtifact control, byte[] before)
    {
        var entry = CodedEntry(control, ArchiveTestMutationKind.Bzip2WrongCrc, 12);
        if (entry.CompressedSize < 10
            || before[(int)entry.DataOffset + 4] != 0x31
            || before[(int)entry.DataOffset + 5] != 0x41
            || before[(int)entry.DataOffset + 6] != 0x59
            || before[(int)entry.DataOffset + 7] != 0x26
            || before[(int)entry.DataOffset + 8] != 0x53
            || before[(int)entry.DataOffset + 9] != 0x59)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{ArchiveTestMutationKind.Bzip2WrongCrc.ToCaseKey()}': the control's payload is not a single-block BZip2 stream.");
        }

        var crcOffset = checked(entry.DataOffset + 10);
        var code = ArchiveTestMutationKind.Bzip2WrongCrc.ToCaseKey();

        return Patch(ArchiveTestMutationKind.Bzip2WrongCrc, before,
        [
            new MutationSpec(
                code, "file-data", crcOffset,
                "Flips a bit in the stored BZip2 block CRC right after the block magic: integrity-checking decoders reject the block.",
                entry.Ordinal, 1, 1, DeclaredValue: "bzip2-block-crc-corrupt"),
        ], after => after[crcOffset] ^= 0x01);
    }

    /// <summary>
    /// Deletes the trailing 16 compressed bytes of a coded payload while keeping
    /// header sizes (ticket #900): the stream ends mid-block and declared sizes
    /// over-read into the shifted directory. Central offsets and the EOCD offset
    /// are relinked past the deletion so the directory stays walkable; sizes and
    /// CRCs stay as the lies under test.
    /// </summary>
    private static MutatedArchiveFixture TruncateCodedTail(ArchiveFixtureArtifact control, byte[] before, ArchiveTestMutationKind kind, ushort expected)
    {
        var entry = CodedEntry(control, kind, expected);
        const int CutLength = 16;
        if (entry.CompressedSize <= 32)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{kind.ToCaseKey()}': the control's payload is {entry.CompressedSize} bytes, too small to truncate {CutLength} tail bytes from.");
        }

        var cutStart = checked(entry.DataOffset + entry.CompressedSize - CutLength);
        var after = new byte[checked(before.Length - CutLength)];
        before.AsSpan()[..(int)cutStart].CopyTo(after);
        before.AsSpan((int)cutStart + CutLength).CopyTo(after.AsSpan((int)cutStart));

        foreach (var layoutEntry in control.Layout.Entries)
        {
            var fieldAt = layoutEntry.CentralDirectoryOffset > cutStart
                ? checked(layoutEntry.CentralDirectoryOffset - CutLength)
                : layoutEntry.CentralDirectoryOffset;
            if (BinaryPrimitives.ReadUInt32LittleEndian(after.AsSpan((int)fieldAt, 4)) != 0x02014b50)
            {
                throw new InvalidOperationException(
                    $"Archive Test mutation '{kind.ToCaseKey()}': no central header at the relinked offset {fieldAt}.");
            }

            if (layoutEntry.LocalHeaderOffset > cutStart)
            {
                var centralRelativeOffset = checked(fieldAt + 42);
                var relative = BinaryPrimitives.ReadUInt32LittleEndian(after.AsSpan((int)centralRelativeOffset, 4));
                BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan((int)centralRelativeOffset, 4), checked(relative - (uint)CutLength));
            }
        }

        if (control.Layout.EocdOffset <= cutStart)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{kind.ToCaseKey()}': the EOCD does not sit past the truncation point.");
        }

        var newEocdOffset = (int)control.Layout.EocdOffset - CutLength;
        if (BinaryPrimitives.ReadUInt32LittleEndian(after.AsSpan(newEocdOffset, 4)) != 0x06054b50)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{kind.ToCaseKey()}': no EOCD at the relinked offset {newEocdOffset}.");
        }

        var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(after.AsSpan(newEocdOffset + 16, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(after.AsSpan(newEocdOffset + 16, 4), checked(centralDirectoryOffset - (uint)CutLength));

        var code = kind.ToCaseKey();
        var removed = Convert.ToHexStringLower(before.AsSpan((int)cutStart, CutLength));
        var mutation = new ArchiveTestMutation(
            Code: code,
            Structure: "file-data",
            OffsetBasis: "before-mutation",
            Offset: cutStart,
            Explanation: $"Deletes the trailing {CutLength} compressed bytes of the coded payload while keeping header sizes: the stream ends mid-block and the declared compressed size over-reads into the relinked directory. Central offsets shift by -{CutLength}; sizes and CRCs stay as the lies under test.",
            Ordinal: entry.Ordinal,
            DeletedLength: CutLength,
            InsertedLength: 0,
            BeforeSize: before.Length,
            AfterSize: after.Length,
            BeforeSha256: Hash(before),
            AfterSha256: Hash(after),
            BeforeHex: removed,
            AfterHex: null,
            DeclaredValue: $"truncated-tail={CutLength} declared-compressed-size={entry.CompressedSize}");

        return Finalize(kind, before, after, [mutation]);
    }

    /// <summary>The control's first entry as a coded-payload mutation target.</summary>
    private static ArchiveFixtureEntryLayout CodedEntry(ArchiveFixtureArtifact control, ArchiveTestMutationKind kind, ushort code)
    {
        if (control.Layout.Entries.Count == 0 || control.Layout.Entries[0].Method != code)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{kind.ToCaseKey()}': the control's entry 0 must be a method-{code} entry.");
        }

        return control.Layout.Entries[0];
    }

    /// <summary>
    /// Rewrites the local header's compression method to an arbitrary code while the
    /// central header keeps the control's code (ticket #899): the payload bytes stay
    /// exactly the control's real data for the central method, so central-based
    /// readers succeed and local-strict readers fail. The declared code must differ
    /// from the control's, otherwise there is no disagreement to record.
    /// </summary>
    private static MutatedArchiveFixture SetLocalMethodCode(
        ArchiveFixtureArtifact control, byte[] before, ArchiveTestMutationKind kind, ushort declared)
    {
        var entry = control.Layout.Entries[0];
        if (entry.Method == declared)
        {
            throw new InvalidOperationException(
                $"Archive Test mutation '{kind.ToCaseKey()}': the control already declares method {declared}; no cross-method disagreement.");
        }

        var localMethodOffset = entry.LocalHeaderOffset + 8;
        return Patch(kind, before,
        [
            new MutationSpec(
                kind.ToCaseKey(), "local-header", localMethodOffset,
                $"Changes the local header's compression method from {entry.Method} to {declared} while the central header retains {entry.Method}; the payload bytes are unchanged, so a lenient central-method reader still reads them and a local-method reader fails to decode.",
                entry.Ordinal, 2, 2, DeclaredValue: $"local-method={declared} central-method={entry.Method}"),
        ], after => BinaryPrimitives.WriteUInt16LittleEndian(after.AsSpan((int)localMethodOffset, 2), declared));
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
    InvalidUtf8Name,
    Zip64MissingExtra,
    Zip64TruncatedExtra,
    DeclaredSizeOversized,
    OrphanLocalHeader,
    PrefixUnrebased,
    DeflateInvalidBtype,
    DeflateCorruptHuffman,
    UnsupportedMethodDeflate64,
    Bzip2CorruptBlockMagic,
    Bzip2TruncatedStream,
    Bzip2WrongCrc,
    Deflate64CorruptStream,
    Deflate64TruncatedStream,
    MethodCrossDeflate64Deflate,
    MethodCrossBzip2Stored,
    MethodDataDeflateAsBzip2,
    MethodDataBzip2AsStored,
    MultidiskEocdDeclared,
    MultidiskCentralEntryDeclared,
    EocdEntryCountMismatch,
    Zip64EocdEntryCountMismatch,
    Zip64LocatorDiskMismatch,
    DescriptorCrcDisagreement,
    DescriptorSizeDisagreement,
    DuplicateZip64Extra,
    DuplicateUnicodePath,
    Utf8FlagCp437Name,
    UnicodePathCrcMismatch,
    UnicodePathNameDivergence,
    MixedMethodsOneCorruptMember,
    MixedMethodsOneUnsupportedMember,
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
        ArchiveTestMutationKind.InvalidUtf8Name => "invalid-utf8-name",
        ArchiveTestMutationKind.Zip64MissingExtra => "zip64-missing-extra",
        ArchiveTestMutationKind.Zip64TruncatedExtra => "zip64-truncated-extra",
        ArchiveTestMutationKind.DeclaredSizeOversized => "declared-size-oversized",
        ArchiveTestMutationKind.OrphanLocalHeader => "orphan-local-header",
        ArchiveTestMutationKind.PrefixUnrebased => "prefix-unrebased",
        ArchiveTestMutationKind.DeflateInvalidBtype => "deflate-invalid-btype",
        ArchiveTestMutationKind.DeflateCorruptHuffman => "deflate-corrupt-huffman",
        ArchiveTestMutationKind.MethodCrossDeflate64Deflate => "method-cross-deflate64-deflate",
        ArchiveTestMutationKind.MethodCrossBzip2Stored => "method-cross-bzip2-stored",
        ArchiveTestMutationKind.MethodDataDeflateAsBzip2 => "method-data-deflate-as-bzip2",
        ArchiveTestMutationKind.MethodDataBzip2AsStored => "method-data-bzip2-as-stored",
        ArchiveTestMutationKind.UnsupportedMethodDeflate64 => "unsupported-method-deflate64",
        ArchiveTestMutationKind.Bzip2CorruptBlockMagic => "bzip2-corrupt-block-magic",
        ArchiveTestMutationKind.Bzip2TruncatedStream => "bzip2-truncated-stream",
        ArchiveTestMutationKind.Bzip2WrongCrc => "bzip2-wrong-crc",
        ArchiveTestMutationKind.Deflate64CorruptStream => "deflate64-corrupt-stream",
        ArchiveTestMutationKind.Deflate64TruncatedStream => "deflate64-truncated-stream",
        ArchiveTestMutationKind.MultidiskEocdDeclared => "multidisk-eocd-declared",
        ArchiveTestMutationKind.MultidiskCentralEntryDeclared => "multidisk-central-entry-declared",
        ArchiveTestMutationKind.EocdEntryCountMismatch => "eocd-entry-count-mismatch",
        ArchiveTestMutationKind.Zip64EocdEntryCountMismatch => "zip64-eocd-entry-count-mismatch",
        ArchiveTestMutationKind.Zip64LocatorDiskMismatch => "zip64-locator-disk-mismatch",
        ArchiveTestMutationKind.DescriptorCrcDisagreement => "descriptor-crc-disagreement",
        ArchiveTestMutationKind.DescriptorSizeDisagreement => "descriptor-size-disagreement",
        ArchiveTestMutationKind.DuplicateZip64Extra => "duplicate-zip64-extra",
        ArchiveTestMutationKind.DuplicateUnicodePath => "duplicate-unicode-path",
        ArchiveTestMutationKind.Utf8FlagCp437Name => "utf8-flag-cp437-name",
        ArchiveTestMutationKind.UnicodePathCrcMismatch => "unicode-path-crc-mismatch",
        ArchiveTestMutationKind.UnicodePathNameDivergence => "unicode-path-name-divergence",
        ArchiveTestMutationKind.MixedMethodsOneCorruptMember => "mixed-methods-one-corrupt-member",
        ArchiveTestMutationKind.MixedMethodsOneUnsupportedMember => "mixed-methods-one-unsupported-member",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Archive Test mutation kind."),
    };
}
