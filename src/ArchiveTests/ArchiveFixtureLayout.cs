using System.Buffers.Binary;
using System.Text;

namespace Zipper.ArchiveTests;

/// <summary>
/// Exact structure offsets of one owned generated Archive, captured with checked
/// BinaryPrimitives reads after the standard writer finalized its central directory.
/// This is not a general untrusted-ZIP parser: it only reads archives produced by
/// <see cref="ArchiveFixtureBuilder"/> (EOCD last, empty archive comment, counts known).
/// Signature-like bytes inside payloads or names are data and are never searched for.
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
    internal const ushort DataDescriptorFlag = 0x0008;

    internal static ArchiveFixtureLayout Read(ReadOnlySpan<byte> archive)
    {
        const int EocdLength = 22;

        if (archive.Length < EocdLength)
        {
            throw new InvalidDataException($"Archive is {archive.Length} bytes, too small for an EOCD record ({EocdLength}).");
        }

        var eocdOffset = archive.Length - EocdLength;
        if (BinaryPrimitives.ReadUInt32LittleEndian(Slice(archive, eocdOffset, 4, "EOCD signature")) != EocdSignature)
        {
            throw new InvalidDataException($"Expected the EOCD signature 0x06054b50 at offset {eocdOffset} (owned archives have no comment); bytes differ.");
        }

        var diskNumber = ReadUInt16(archive, eocdOffset + 4, "EOCD disk number");
        var centralDirectoryDisk = ReadUInt16(archive, eocdOffset + 6, "EOCD central directory disk");
        if (diskNumber != 0 || centralDirectoryDisk != 0)
        {
            throw new InvalidDataException($"EOCD disk fields must be zero for a single-disk Archive; got disk {diskNumber}, central directory disk {centralDirectoryDisk}.");
        }

        var thisDiskEntries = ReadUInt16(archive, eocdOffset + 8, "EOCD entry count on this disk");
        var totalEntries = ReadUInt16(archive, eocdOffset + 10, "EOCD total entry count");
        if (thisDiskEntries != totalEntries)
        {
            throw new InvalidDataException($"EOCD entry counts disagree: disk {thisDiskEntries}, total {totalEntries}.");
        }

        var centralDirectorySize = ReadUInt32(archive, eocdOffset + 12, "EOCD central directory size");
        var centralDirectoryOffset = ReadUInt32(archive, eocdOffset + 16, "EOCD central directory offset");
        if (checked((long)centralDirectoryOffset + centralDirectorySize) != eocdOffset)
        {
            throw new InvalidDataException(
                $"Central directory span ({centralDirectoryOffset}..{centralDirectoryOffset + centralDirectorySize}) does not end at the EOCD offset {eocdOffset}.");
        }

        var entries = new ArchiveFixtureEntryLayout[totalEntries];
        var cursor = (long)centralDirectoryOffset;
        for (var ordinal = 0; ordinal < totalEntries; ordinal++)
        {
            entries[ordinal] = ReadCentralEntry(archive, ordinal, ref cursor);
        }

        if (cursor != eocdOffset)
        {
            throw new InvalidDataException($"Central directory walk ended at {cursor}, expected {eocdOffset}.");
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
