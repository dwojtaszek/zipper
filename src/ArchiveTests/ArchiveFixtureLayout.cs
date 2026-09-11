using System.Buffers.Binary;
using System.Text;

namespace Zipper.ArchiveTests;

/// <summary>
/// Exact structure offsets of one owned generated Archive, captured with checked
/// BinaryPrimitives reads after the standard writer finalized its central directory.
/// This is not a general untrusted-ZIP parser: it only reads archives produced by
/// <see cref="ArchiveFixtureBuilder"/> (EOCD last, a declared comment, counts known).
/// Signature-like bytes inside payloads, names, or comments are data and are never
/// searched for: the EOCD is located from the archive length minus its record minus the
/// caller-declared comment length. Zip64 sentinels in the EOCD or a central header are
/// resolved through the Zip64 EOCD locator and the central Zip64 extra field (APPNOTE
/// §4.5.3), reading only the fields whose 32-bit counterparts are sentinels, in
/// specification order.
/// </summary>
internal sealed record ArchiveFixtureEntryLayout(
    int Ordinal,
    long LocalHeaderOffset,
    long DataOffset,
    long CentralDirectoryOffset,
    int LocalNameLength,
    int LocalExtraLength,
    int CentralNameLength,
    int CentralExtraLength,
    int CentralCommentLength,
    uint Crc32,
    uint CompressedSize,
    uint UncompressedSize,
    ushort Method,
    bool HasDataDescriptor,
    long DataDescriptorOffset,
    string Name,
    string NameHex);

internal sealed record ArchiveFixtureLayout(
    long EocdOffset,
    long CentralDirectoryOffset,
    uint CentralDirectorySize,
    int EntryCount,
    long TotalBytes,
    IReadOnlyList<ArchiveFixtureEntryLayout> Entries)
{
    internal const uint EocdSignature = 0x06054b50;
    internal const uint CentralHeaderSignature = 0x02014b50;
    internal const uint LocalHeaderSignature = 0x04034b50;
    internal const uint Zip64EocdSignature = 0x06064b50;
    internal const uint Zip64LocatorSignature = 0x07064b50;
    internal const uint Zip64ExtraFieldId = 0x0001;
    internal const uint Zip64SizeSentinel = 0xFFFFFFFF;
    internal const ushort Zip64CountSentinel = 0xFFFF;
    internal const ushort DataDescriptorFlag = 0x0008;

    /// <summary>
    /// Reads the layout of an owned Archive. The caller declares the archive comment
    /// length when it constructed one (ticket #841): the EOCD sits at
    /// <c>length - 22 - expectedCommentLength</c>, located by length, never by search.
    /// </summary>
    internal static ArchiveFixtureLayout Read(ReadOnlySpan<byte> archive, int expectedCommentLength = 0)
    {
        const int EocdLength = 22;

        if (expectedCommentLength is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedCommentLength), "The expected comment length must not be negative.");
        }

        if (archive.Length < checked(EocdLength + expectedCommentLength))
        {
            throw new InvalidDataException(
                $"Archive is {archive.Length} bytes, too small for an EOCD record ({EocdLength}) plus the declared {expectedCommentLength}-byte comment.");
        }

        var eocdOffset = archive.Length - EocdLength - expectedCommentLength;
        if (BinaryPrimitives.ReadUInt32LittleEndian(Slice(archive, eocdOffset, 4, "EOCD signature")) != EocdSignature)
        {
            throw new InvalidDataException($"Expected the EOCD signature 0x06054b50 at offset {eocdOffset} (owned archives declare their comment length); bytes differ.");
        }

        var commentLength = ReadUInt16(archive, eocdOffset + 20, "EOCD comment length");
        if (commentLength != expectedCommentLength)
        {
            throw new InvalidDataException($"EOCD comment length is {commentLength}, expected the declared {expectedCommentLength}.");
        }

        var diskNumber = ReadUInt16(archive, eocdOffset + 4, "EOCD disk number");
        var centralDirectoryDisk = ReadUInt16(archive, eocdOffset + 6, "EOCD central directory disk");
        if (diskNumber != 0 || centralDirectoryDisk != 0)
        {
            throw new InvalidDataException($"EOCD disk fields must be zero for a single-disk Archive; got disk {diskNumber}, central directory disk {centralDirectoryDisk}.");
        }

        var thisDiskEntries = ReadUInt16(archive, eocdOffset + 8, "EOCD entry count on this disk");
        var totalEntries = ReadUInt16(archive, eocdOffset + 10, "EOCD total entry count");
        var centralDirectorySize = ReadUInt32(archive, eocdOffset + 12, "EOCD central directory size");
        var centralDirectoryOffset = ReadUInt32(archive, eocdOffset + 16, "EOCD central directory offset");

        // Zip64: when the EOCD's counts or spans are sentinels, the real values live in
        // the Zip64 EOCD, located through the locator that immediately precedes the EOCD
        // in owned archives (never a search).
        if (thisDiskEntries == Zip64CountSentinel || totalEntries == Zip64CountSentinel
            || centralDirectorySize == Zip64SizeSentinel || centralDirectoryOffset == Zip64SizeSentinel)
        {
            var locatorOffset = checked(eocdOffset - 20);
            if (BinaryPrimitives.ReadUInt32LittleEndian(Slice(archive, locatorOffset, 4, "Zip64 EOCD locator signature")) != Zip64LocatorSignature)
            {
                throw new InvalidDataException($"Expected the Zip64 EOCD locator signature 0x07064b50 at offset {locatorOffset} (immediately before the EOCD in owned archives).");
            }

            var zip64EocdOffset = ReadUInt32(archive, locatorOffset + 8, "Zip64 EOCD locator offset");
            if (BinaryPrimitives.ReadUInt32LittleEndian(Slice(archive, (long)zip64EocdOffset, 4, "Zip64 EOCD signature")) != Zip64EocdSignature)
            {
                throw new InvalidDataException($"Expected the Zip64 EOCD signature 0x06064b50 at offset {zip64EocdOffset}.");
            }

            thisDiskEntries = totalEntries = (ushort)ReadUInt32(archive, (long)zip64EocdOffset + 32, "Zip64 EOCD total entry count");
            centralDirectorySize = ReadUInt32(archive, (long)zip64EocdOffset + 40, "Zip64 EOCD central directory size");
            centralDirectoryOffset = ReadUInt32(archive, (long)zip64EocdOffset + 48, "Zip64 EOCD central directory offset");
            if (checked((long)centralDirectoryOffset + centralDirectorySize) != (long)zip64EocdOffset)
            {
                throw new InvalidDataException(
                    $"Central directory span ({centralDirectoryOffset}..{centralDirectoryOffset + centralDirectorySize}) does not end at the Zip64 EOCD offset {zip64EocdOffset}.");
            }
        }
        else if (checked((long)centralDirectoryOffset + centralDirectorySize) != eocdOffset)
        {
            throw new InvalidDataException(
                $"Central directory span ({centralDirectoryOffset}..{centralDirectoryOffset + centralDirectorySize}) does not end at the EOCD offset {eocdOffset}.");
        }

        if (thisDiskEntries != totalEntries)
        {
            throw new InvalidDataException($"EOCD entry counts disagree: disk {thisDiskEntries}, total {totalEntries}.");
        }

        var entries = new ArchiveFixtureEntryLayout[totalEntries];
        var cursor = (long)centralDirectoryOffset;
        for (var ordinal = 0; ordinal < totalEntries; ordinal++)
        {
            entries[ordinal] = ReadCentralEntry(archive, ordinal, ref cursor);
        }

        // The span check (central directory ends at the EOCD, or at the Zip64 EOCD for
        // Zip64 archives) already ran per branch above; here the walked bytes must cover
        // exactly the declared span.
        var centralEnd = checked((long)centralDirectoryOffset + centralDirectorySize);
        if (cursor != centralEnd)
        {
            throw new InvalidDataException($"Central directory walk ended at {cursor}, expected {centralEnd}.");
        }

        for (var ordinal = 0; ordinal < totalEntries; ordinal++)
        {
            entries[ordinal] = AttachLocalHeader(archive, entries[ordinal]);
        }

        return new ArchiveFixtureLayout(
            EocdOffset: eocdOffset,
            CentralDirectoryOffset: centralDirectoryOffset,
            CentralDirectorySize: centralDirectorySize,
            EntryCount: totalEntries,
            TotalBytes: archive.Length,
            Entries: entries);
    }

    private static ArchiveFixtureEntryLayout ReadCentralEntry(ReadOnlySpan<byte> archive, int ordinal, ref long cursor)
    {
        var start = cursor;
        if (BinaryPrimitives.ReadUInt32LittleEndian(Slice(archive, start, 4, $"entry {ordinal} central signature")) != CentralHeaderSignature)
        {
            throw new InvalidDataException($"Expected the central header signature 0x02014b50 at offset {start} for entry {ordinal}.");
        }

        var method = ReadUInt16(archive, start + 10, $"entry {ordinal} method");
        var crc32 = ReadUInt32(archive, start + 16, $"entry {ordinal} CRC-32");
        var compressedSize = ReadUInt32(archive, start + 20, $"entry {ordinal} compressed size");
        var uncompressedSize = ReadUInt32(archive, start + 24, $"entry {ordinal} uncompressed size");
        var nameLength = ReadUInt16(archive, start + 28, $"entry {ordinal} central name length");
        var extraLength = ReadUInt16(archive, start + 30, $"entry {ordinal} central extra length");
        var commentLength = ReadUInt16(archive, start + 32, $"entry {ordinal} central comment length");
        var localHeaderOffset = ReadUInt32(archive, start + 42, $"entry {ordinal} local header offset");
        var nameBytes = Slice(archive, start + 46, nameLength, $"entry {ordinal} name");

        // Zip64 sentinels resolve through the central Zip64 extra field (0x0001),
        // reading exactly the fields whose 32-bit counterparts are sentinels, in
        // specification order: uncompressed size (offset +4), compressed size (+12),
        // local header offset (+20) — each an 8-byte field.
        if (uncompressedSize == Zip64SizeSentinel || compressedSize == Zip64SizeSentinel || localHeaderOffset == Zip64SizeSentinel)
        {
            var zip64ExtraOffset = checked(start + 46 + nameLength);
            if (extraLength < 4 || ReadUInt16(archive, zip64ExtraOffset, $"entry {ordinal} Zip64 extra field id") != Zip64ExtraFieldId)
            {
                throw new InvalidDataException(
                    $"Entry {ordinal} declares Zip64 sentinel sizes but its central extra field is not a Zip64 (0x0001) extra field.");
            }

            if (uncompressedSize == Zip64SizeSentinel)
            {
                uncompressedSize = ReadUInt32(archive, zip64ExtraOffset + 4, $"entry {ordinal} Zip64 uncompressed size");
            }

            if (compressedSize == Zip64SizeSentinel)
            {
                compressedSize = ReadUInt32(archive, zip64ExtraOffset + 12, $"entry {ordinal} Zip64 compressed size");
            }

            if (localHeaderOffset == Zip64SizeSentinel)
            {
                localHeaderOffset = ReadUInt32(archive, zip64ExtraOffset + 20, $"entry {ordinal} Zip64 local header offset");
            }
        }

        cursor = checked(start + 46 + nameLength + extraLength + commentLength);

        return new ArchiveFixtureEntryLayout(
            Ordinal: ordinal,
            LocalHeaderOffset: localHeaderOffset,
            DataOffset: 0,
            CentralDirectoryOffset: start,
            LocalNameLength: 0,
            LocalExtraLength: 0,
            CentralNameLength: nameLength,
            CentralExtraLength: extraLength,
            CentralCommentLength: commentLength,
            Crc32: crc32,
            CompressedSize: compressedSize,
            UncompressedSize: uncompressedSize,
            Method: method,
            HasDataDescriptor: false,
            DataDescriptorOffset: 0,
            Name: Encoding.UTF8.GetString(nameBytes),
            NameHex: Convert.ToHexStringLower(nameBytes));
    }

    private static ArchiveFixtureEntryLayout AttachLocalHeader(ReadOnlySpan<byte> archive, ArchiveFixtureEntryLayout entry)
    {
        var offset = entry.LocalHeaderOffset;
        if (BinaryPrimitives.ReadUInt32LittleEndian(Slice(archive, offset, 4, $"entry {entry.Ordinal} local signature")) != LocalHeaderSignature)
        {
            throw new InvalidDataException($"Expected the local header signature 0x04034b50 at offset {offset} for entry {entry.Ordinal}.");
        }

        var flags = ReadUInt16(archive, offset + 6, $"entry {entry.Ordinal} general purpose flags");
        var localMethod = ReadUInt16(archive, offset + 8, $"entry {entry.Ordinal} local method");
        if (localMethod != entry.Method)
        {
            throw new InvalidDataException(
                $"Entry {entry.Ordinal} method mismatch: local header {localMethod}, central header {entry.Method}.");
        }

        var nameLength = ReadUInt16(archive, offset + 26, $"entry {entry.Ordinal} local name length");
        var extraLength = ReadUInt16(archive, offset + 28, $"entry {entry.Ordinal} local extra length");
        var localNameBytes = Slice(archive, offset + 30, nameLength, $"entry {entry.Ordinal} local name");
        if (!localNameBytes.SequenceEqual(Convert.FromHexString(entry.NameHex)))
        {
            throw new InvalidDataException($"Entry {entry.Ordinal} name mismatch between local and central headers.");
        }

        var dataOffset = checked(offset + 30 + nameLength + extraLength);
        var hasDescriptor = (flags & DataDescriptorFlag) != 0;
        var descriptorOffset = hasDescriptor ? checked(dataOffset + entry.CompressedSize) : 0;

        return entry with
        {
            LocalNameLength = nameLength,
            LocalExtraLength = extraLength,
            DataOffset = dataOffset,
            HasDataDescriptor = hasDescriptor,
            DataDescriptorOffset = descriptorOffset,
        };
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> archive, long offset, string field)
    {
        var slice = Slice(archive, offset, 2, field);
        return BinaryPrimitives.ReadUInt16LittleEndian(slice);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> archive, long offset, string field)
    {
        var slice = Slice(archive, offset, 4, field);
        return BinaryPrimitives.ReadUInt32LittleEndian(slice);
    }

    private static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> archive, long offset, int length, string field)
    {
        if (offset < 0 || length < 0 || checked(offset + length) > archive.Length)
        {
            throw new InvalidDataException($"Field '{field}' at offset {offset} with length {length} is out of range for a {archive.Length}-byte Archive.");
        }

        return archive.Slice((int)offset, length);
    }
}
