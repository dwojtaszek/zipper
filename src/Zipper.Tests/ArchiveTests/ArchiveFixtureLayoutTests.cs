using System.Buffers.Binary;
using System.Text;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class ArchiveFixtureLayoutTests
{
    private static byte[] BuildSingleStoredEntry(int length)
    {
        var definition = new ArchiveTestCaseDefinition(
            "layout-control", 1, 1, "valid", ["smoke"],
            new ArchiveTestRecipe([ArchiveTestRecipeEntry.File("a.txt", length, "stored")]));
        return ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None).ArchiveBytes;
    }

    [Fact]
    public void Read_SingleStoredControl_CapturesExactOffsets()
    {
        var archive = BuildSingleStoredEntry(40);
        var layout = ArchiveFixtureLayout.Read(archive);

        var entry = Assert.Single(layout.Entries);
        Assert.Equal(0, entry.LocalHeaderOffset);
        Assert.Equal("a.txt", entry.Name);
        Assert.Equal("612e747874", entry.NameHex);
        Assert.Equal((ushort)0, entry.Method);
        Assert.False(entry.HasDataDescriptor);

        // Independent raw-byte inspection, not the reader's own fields.
        Assert.Equal(0x04034b50u, BinaryPrimitives.ReadUInt32LittleEndian(archive));
        Assert.Equal(20u, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(4))); // version needed 2.0, flags 0
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(8))); // method
        Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(18))); // uncompressed
        Assert.Equal(40u, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(22))); // compressed
        Assert.Equal("a.txt", Encoding.UTF8.GetString(archive.AsSpan(30, 5))); // local name field

        Assert.Equal(30 + "a.txt".Length, entry.DataOffset);

        Assert.Equal(0x02014b50u, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan((int)entry.CentralDirectoryOffset)));
        Assert.Equal(0x06054b50u, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan((int)layout.EocdOffset)));
        Assert.Equal(layout.EocdOffset, layout.CentralDirectoryOffset + layout.CentralDirectorySize);
        Assert.Equal(archive.Length, layout.TotalBytes);
        Assert.Equal(archive.Length, layout.EocdOffset + 22);
    }

    [Fact]
    public void Read_RecordsCrcAndSizesFromCentralHeader()
    {
        var archive = BuildSingleStoredEntry(40);
        var layout = ArchiveFixtureLayout.Read(archive);
        var entry = Assert.Single(layout.Entries);

        // CRC-32 and sizes come straight from the central header the standard writer emitted.
        Assert.Equal(
            BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan((int)entry.CentralDirectoryOffset + 16)),
            entry.Crc32);
        Assert.Equal((uint)40, entry.UncompressedSize);
        Assert.Equal((uint)40, entry.CompressedSize);

        // Local header duplicates the sizes for a seekable stored write (no descriptor).
        Assert.Equal(entry.Crc32, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(14)));
        Assert.Equal((uint)40, BinaryPrimitives.ReadUInt32LittleEndian(archive.AsSpan(18)));
    }

    [Fact]
    public void Read_TruncatedArchive_ThrowsDescriptively()
    {
        var archive = BuildSingleStoredEntry(40);

        var error = Assert.Throws<InvalidDataException>(() => ArchiveFixtureLayout.Read(archive[..^1]));

        Assert.Contains("EOCD", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_ShortGarbageBytes_ThrowsDescriptively()
    {
        var garbage = new byte[10];

        var error = Assert.Throws<InvalidDataException>(() => ArchiveFixtureLayout.Read(garbage));

        Assert.Contains("too small", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_FlippedEocdSignature_ThrowsDescriptively()
    {
        var archive = BuildSingleStoredEntry(40);
        archive[^22] = 0x00; // break the EOCD signature

        var error = Assert.Throws<InvalidDataException>(() => ArchiveFixtureLayout.Read(archive));

        Assert.Contains("0x06054b50", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_FlippedCentralSignature_ThrowsDescriptively()
    {
        var archive = BuildSingleStoredEntry(40);
        var layout = ArchiveFixtureLayout.Read(archive);
        archive[(int)layout.CentralDirectoryOffset] = 0x00;

        var error = Assert.Throws<InvalidDataException>(() => ArchiveFixtureLayout.Read(archive));

        Assert.Contains("0x02014b50", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_FlippedLocalSignature_ThrowsDescriptively()
    {
        var archive = BuildSingleStoredEntry(40);
        archive[0] = 0x00;

        var error = Assert.Throws<InvalidDataException>(() => ArchiveFixtureLayout.Read(archive));

        Assert.Contains("0x04034b50", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_LocalCentralNameMismatch_ThrowsByDefault()
    {
        var archive = BuildSingleStoredEntry(40);
        archive[30] = (byte)'b';

        var error = Assert.Throws<InvalidDataException>(() => ArchiveFixtureLayout.Read(archive));

        Assert.Contains("name mismatch between local and central headers", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_DivergentNamesWithUnicodePath_ValidatesEachHeaderCrc()
    {
        var (archive, _, _) = BuildDivergentUnicodePathArchive();

        var layout = ArchiveFixtureLayout.Read(archive, allowLocalCentralNameDivergence: true);

        var entry = Assert.Single(layout.Entries);
        Assert.Equal("§ab.txt", entry.Name);
        Assert.Equal("819861622e747874", entry.LocalNameHex);
        Assert.Equal("c2a761622e747874", entry.NameHex);
        Assert.Equal("c2a761622e747874", entry.UnicodePathNameHex);
    }

    [Fact]
    public void Read_DivergentNamesWithInvalidLocalUnicodePathCrc_ThrowsDescriptively()
    {
        var (archive, localUnicodePathCrcOffset, _) = BuildDivergentUnicodePathArchive();
        archive[localUnicodePathCrcOffset] ^= 0x01;

        var error = Assert.Throws<InvalidDataException>(
            () => ArchiveFixtureLayout.Read(archive, allowLocalCentralNameDivergence: true));

        Assert.Contains("local", error.Message, StringComparison.Ordinal);
        Assert.Contains("CRC", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_DivergentNamesWithInvalidCentralUnicodePathCrc_ThrowsDescriptively()
    {
        var (archive, _, centralUnicodePathCrcOffset) = BuildDivergentUnicodePathArchive();
        archive[centralUnicodePathCrcOffset] ^= 0x01;

        var error = Assert.Throws<InvalidDataException>(
            () => ArchiveFixtureLayout.Read(archive, allowLocalCentralNameDivergence: true));

        Assert.Contains("central", error.Message, StringComparison.Ordinal);
        Assert.Contains("CRC", error.Message, StringComparison.Ordinal);
    }

    private static (byte[] Archive, int LocalUnicodePathCrcOffset, int CentralUnicodePathCrcOffset) BuildDivergentUnicodePathArchive()
    {
        const string LogicalName = "§ab.txt";
        var artifact = ArchiveFixtureBuilder.BuildControl("valid-unicode-path", 42, CancellationToken.None);
        var entry = Assert.Single(artifact.Layout.Entries);
        var archive = (byte[])artifact.ArchiveBytes.Clone();
        var localName = CodePagesEncodingProvider.Instance.GetEncoding(932)!.GetBytes(LogicalName);
        var centralName = Encoding.UTF8.GetBytes(LogicalName);
        var unicodeName = Encoding.UTF8.GetBytes(LogicalName);
        Assert.Equal(entry.LocalNameLength, localName.Length);
        Assert.Equal(entry.CentralNameLength, centralName.Length);

        var localNameOffset = checked((int)entry.LocalHeaderOffset + 30);
        var centralNameOffset = checked((int)entry.CentralDirectoryOffset + 46);
        var localExtraOffset = localNameOffset + entry.LocalNameLength;
        var centralExtraOffset = centralNameOffset + entry.CentralNameLength;
        Assert.Equal((ushort)0x7075, BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(localExtraOffset, 2)));
        Assert.Equal((ushort)0x7075, BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(centralExtraOffset, 2)));

        localName.CopyTo(archive.AsSpan(localNameOffset, entry.LocalNameLength));
        centralName.CopyTo(archive.AsSpan(centralNameOffset, entry.CentralNameLength));
        unicodeName.CopyTo(archive.AsSpan(localExtraOffset + 9, unicodeName.Length));
        unicodeName.CopyTo(archive.AsSpan(centralExtraOffset + 9, unicodeName.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            archive.AsSpan(localExtraOffset + 5, 4), ArchiveFixtureBuilder.Crc32(localName));
        BinaryPrimitives.WriteUInt32LittleEndian(
            archive.AsSpan(centralExtraOffset + 5, 4), ArchiveFixtureBuilder.Crc32(centralName));

        var localFlagsOffset = checked((int)entry.LocalHeaderOffset + 6);
        var localFlags = BinaryPrimitives.ReadUInt16LittleEndian(archive.AsSpan(localFlagsOffset, 2));
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(localFlagsOffset, 2), (ushort)(localFlags & ~0x0800));

        return (archive, localExtraOffset + 5, centralExtraOffset + 5);
    }

    [Fact]
    public void Read_EmptyArchive_EocdOnlyLayout()
    {
        var layout = ArchiveFixtureLayout.Read(Convert.FromHexString("504b0506000000000000000000000000000000000000"));

        Assert.Equal(0, layout.EocdOffset);
        Assert.Equal(22, layout.TotalBytes);
        Assert.Empty(layout.Entries);
        Assert.Equal(0, layout.CentralDirectoryOffset);
    }
}
