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
    public void Read_EmptyArchive_EocdOnlyLayout()
    {
        var layout = ArchiveFixtureLayout.Read(Convert.FromHexString("504b0506000000000000000000000000000000000000"));

        Assert.Equal(0, layout.EocdOffset);
        Assert.Equal(22, layout.TotalBytes);
        Assert.Empty(layout.Entries);
        Assert.Equal(0, layout.CentralDirectoryOffset);
    }
}
