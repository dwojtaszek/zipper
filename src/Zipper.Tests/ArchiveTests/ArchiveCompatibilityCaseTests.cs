using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Ticket #841 compatibility cases: signed and unsigned data descriptors (APPNOTE
/// §4.3.9), UTF-8 and CP437 name encodings (bit 11 / Appendix D), signature-like comment
/// bytes, a tiny genuine Zip64 Archive, and the three paired malformed cases.
/// </summary>
public class ArchiveCompatibilityCaseTests : TempDirectoryTestBase
{
    private static ArchiveFixtureArtifact BuildControl(string caseKey) =>
        ArchiveFixtureBuilder.BuildControl(caseKey, 42, CancellationToken.None);

    private static byte[] ReadEntryContent(byte[] archiveBytes, string entryName)
    {
        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"entry '{entryName}' not found");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static byte[] ReadFirstEntryContent(byte[] archiveBytes)
    {
        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        using var stream = archive.Entries[0].Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static ushort ReadUInt16(byte[] bytes, long offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)offset, 2));

    private static uint ReadUInt32(byte[] bytes, long offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)offset, 4));

    private static void AssertOnlyRangesChanged(byte[] before, byte[] after, params (long Offset, int Width)[] ranges)
    {
        Assert.Equal(before.Length, after.Length);
        var changed = 0;
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] == after[i])
            {
                continue;
            }

            changed++;
            var offset = i;
            Assert.Contains(ranges, range => offset >= range.Offset && offset < checked(range.Offset + range.Width));
        }

        Assert.NotEqual(0, changed);
    }

    // ---- Data descriptors (APPNOTE §4.3.9) ----

    [Fact]
    public void Build_ValidDescriptorSignature_CarriesSignedDescriptor()
    {
        var control = BuildControl("valid-descriptor-signature");
        var entry = control.Layout.Entries[0];

        Assert.True(entry.HasDataDescriptor);
        Assert.Equal(0x08074b50u, ReadUInt32(control.ArchiveBytes, entry.DataDescriptorOffset));

        // The 32-bit descriptor fields after the signature are the entry's real values.
        Assert.Equal(entry.Crc32, ReadUInt32(control.ArchiveBytes, entry.DataDescriptorOffset + 4));
        Assert.Equal(entry.CompressedSize, ReadUInt32(control.ArchiveBytes, entry.DataDescriptorOffset + 8));
        Assert.Equal(entry.UncompressedSize, ReadUInt32(control.ArchiveBytes, entry.DataDescriptorOffset + 12));

        Assert.Equal(control.Entries[0].Content, ReadEntryContent(control.ArchiveBytes, "d.txt"));
    }

    [Fact]
    public void Build_ValidDescriptorNoSignature_KeepsIdenticalContentWithoutSignature()
    {
        var signed = BuildControl("valid-descriptor-signature");
        var unsigned = BuildControl("valid-descriptor-no-signature");
        var entry = unsigned.Layout.Entries[0];

        // Unsigned form: 4 bytes shorter, the descriptor starts with the CRC directly.
        Assert.Equal(signed.ArchiveBytes.Length - 4, unsigned.ArchiveBytes.Length);
        Assert.True(entry.HasDataDescriptor);
        Assert.Equal(entry.Crc32, ReadUInt32(unsigned.ArchiveBytes, entry.DataDescriptorOffset));
        Assert.Equal(entry.CompressedSize, ReadUInt32(unsigned.ArchiveBytes, entry.DataDescriptorOffset + 4));
        Assert.Equal(entry.UncompressedSize, ReadUInt32(unsigned.ArchiveBytes, entry.DataDescriptorOffset + 8));

        // Both descriptor forms yield identical content structure: same entry shape and
        // same length (the payload bytes differ only because the payload chain includes
        // the Case Key); each form reads back its own recipe content byte-for-byte.
        Assert.Equal(signed.Entries[0].Content, ReadEntryContent(signed.ArchiveBytes, "d.txt"));
        Assert.Equal(unsigned.Entries[0].Content, ReadEntryContent(unsigned.ArchiveBytes, "d.txt"));
        Assert.Equal(signed.Entries[0].Content.Length, unsigned.Entries[0].Content.Length);
        Assert.Equal(signed.Layout.Entries[0].CompressedSize, unsigned.Layout.Entries[0].CompressedSize);
        Assert.Equal(signed.Layout.Entries[0].UncompressedSize, unsigned.Layout.Entries[0].UncompressedSize);
    }

    // ---- Name encodings (bit 11 / Appendix D) ----

    [Fact]
    public void Build_ValidUtf8Name_SetsBit11WithUtf8RawBytes()
    {
        var control = BuildControl("valid-utf8-name");
        var entry = control.Layout.Entries[0];

        Assert.Equal(0x0800, ReadUInt16(control.ArchiveBytes, entry.LocalHeaderOffset + 6) & 0x0800);
        Assert.Equal(0x0800, ReadUInt16(control.ArchiveBytes, entry.CentralDirectoryOffset + 8) & 0x0800);
        Assert.Equal(Convert.ToHexStringLower("café.txt"u8), entry.NameHex);
        Assert.Equal("café.txt", entry.Name);
        Assert.Equal(control.Entries[0].Content, ReadEntryContent(control.ArchiveBytes, "café.txt"));
    }

    [Fact]
    public void Build_ValidCp437Name_UsesLegacyBytesWithBit11Clear()
    {
        var control = BuildControl("valid-cp437-name");
        var entry = control.Layout.Entries[0];

        // Bit 11 clear in both headers: the raw bytes are the legacy CP437 form.
        Assert.Equal(0, ReadUInt16(control.ArchiveBytes, entry.LocalHeaderOffset + 6) & 0x0800);
        Assert.Equal(0, ReadUInt16(control.ArchiveBytes, entry.CentralDirectoryOffset + 8) & 0x0800);
        Assert.Equal("636166822e747874", entry.NameHex);
        Assert.Equal("café.txt", entry.Name);
        Assert.Equal(control.Entries[0].Content, ReadFirstEntryContent(control.ArchiveBytes));
    }

    // ---- Signature-like comment bytes ----

    [Fact]
    public void Build_ValidSignaturesInComment_KeepsCommentBytesAsData()
    {
        var control = BuildControl("valid-signatures-in-comment");

        // The comment sits after the EOCD, carries three signature-like PK records (the
        // EOCD signature itself is excluded: a reference reader's backward EOCD scan
        // would misread it — a recorded runtime compatibility boundary), and is located
        // by the declared length; the archive stays fully readable.
        Assert.Equal(24, ReadUInt16(control.ArchiveBytes, control.Layout.EocdOffset + 20));
        var comment = control.ArchiveBytes.AsSpan((int)control.Layout.EocdOffset + 22, 24).ToArray();
        Assert.Equal([0x50, 0x4b, 0x03, 0x04], comment[..4]);
        Assert.Equal([0x50, 0x4b, 0x01, 0x02], comment[4..8]);
        Assert.Equal([0x50, 0x4b, 0x07, 0x08], comment[8..12]);

        // The entry payload itself also begins with signature-like bytes (pure data).
        Assert.Equal([0x50, 0x4b, 0x03, 0x04, 0x14, 0x00], control.Entries[0].Content[..6]);
        Assert.Equal(control.Entries[0].Content, ReadEntryContent(control.ArchiveBytes, "note.txt"));
    }

    // ---- Tiny genuine Zip64 (APPNOTE §4.5.3) ----

    [Fact]
    public void Build_ValidZip64Small_RoundTripsThroughTwoImplementations()
    {
        var control = BuildControl("valid-zip64-small");
        var entry = control.Layout.Entries[0];
        var bytes = control.ArchiveBytes;

        // Physical content stays tiny, far below every resource limit.
        Assert.True(bytes.Length < 300, $"expected a tiny Zip64 Archive, got {bytes.Length} bytes");

        // Sentinels in the fixed fields; required version 45 in the local header.
        Assert.Equal(45, ReadUInt16(bytes, 0 + 4));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(bytes, 0 + 18));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(bytes, 0 + 22));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(bytes, entry.CentralDirectoryOffset + 20));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(bytes, entry.CentralDirectoryOffset + 24));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(bytes, entry.CentralDirectoryOffset + 42));
        Assert.Equal(0xFFFF, ReadUInt16(bytes, control.Layout.EocdOffset + 8));

        // The layout reader (implementation one) resolves every sentinel through the
        // Zip64 extended fields in specification order.
        Assert.Equal(0, entry.LocalHeaderOffset);
        Assert.Equal((uint)control.Entries[0].Content.Length, entry.CompressedSize);
        Assert.Equal((uint)control.Entries[0].Content.Length, entry.UncompressedSize);
        Assert.Equal(1, control.Layout.EntryCount);

        // The standard ZipArchive reader (implementation two) round-trips the content;
        // it validates the hand-computed CRC-32 on read.
        Assert.Equal(control.Entries[0].Content, ReadEntryContent(bytes, "z64.txt"));
    }

    // ---- Paired malformed cases ----

    [Fact]
    public void Apply_InvalidUtf8Name_SetsFlagWithFixedInvalidRawBytes()
    {
        var control = BuildControl("valid-stored");
        var mutated = ArchiveFixtureMutator.Apply(
            ArchiveTestMutationKind.InvalidUtf8Name, control);
        var entry = control.Layout.Entries[0];

        Assert.Equal(0x0800, ReadUInt16(mutated.ArchiveBytes, entry.LocalHeaderOffset + 6) & 0x0800);
        Assert.Equal(0x0800, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 8) & 0x0800);

        var invalidHex = Convert.ToHexStringLower(new byte[] { 0xC3, 0x28, 0xC3, 0x28, 0x74 });
        Assert.Equal(invalidHex, Convert.ToHexStringLower(mutated.ArchiveBytes.AsSpan((int)(entry.LocalHeaderOffset + 30), 5)));
        Assert.Equal(invalidHex, Convert.ToHexStringLower(mutated.ArchiveBytes.AsSpan((int)(entry.CentralDirectoryOffset + 46), 5)));

        AssertOnlyRangesChanged(
            control.ArchiveBytes, mutated.ArchiveBytes,
            (entry.LocalHeaderOffset + 30, 5),
            (entry.CentralDirectoryOffset + 46, 5),
            (entry.LocalHeaderOffset + 6, 2),
            (entry.CentralDirectoryOffset + 8, 2));

        Assert.Equal(4, mutated.Mutations.Count);
        Assert.All(mutated.Mutations, mutation => Assert.NotNull(mutation.DeclaredValue));
        Assert.Contains(mutated.Mutations, mutation =>
            mutation.DeclaredValue!.Contains($"local-name-hex={invalidHex}", StringComparison.Ordinal));
    }

    [Fact]
    public void Apply_Zip64MissingExtra_HidesTheRequiredExtendedField()
    {
        var control = BuildControl("valid-zip64-small");
        var mutated = ArchiveFixtureMutator.Apply(
            ArchiveTestMutationKind.Zip64MissingExtra, control);
        var entry = control.Layout.Entries[0];

        // The central extra-length becomes 0 while every size stays a Zip64 sentinel:
        // the required extended field is missing, and the bytes never grow.
        Assert.Equal(0, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 30));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 20));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 24));
        Assert.Equal(0xFFFFFFFFu, ReadUInt32(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 42));
        Assert.Equal(control.ArchiveBytes.Length, mutated.ArchiveBytes.Length);

        AssertOnlyRangesChanged(
            control.ArchiveBytes, mutated.ArchiveBytes,
            (entry.CentralDirectoryOffset + 30, 2));

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Contains("central-extra-length=0", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains("sizes-remain=0xFFFFFFFF", mutation.DeclaredValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_Zip64TruncatedExtra_OverrunsSubfieldSizeOnly()
    {
        var control = BuildControl("valid-zip64-small");
        var mutated = ArchiveFixtureMutator.Apply(
            ArchiveTestMutationKind.Zip64TruncatedExtra, control);
        var entry = control.Layout.Entries[0];

        var subfieldSizeOffset = entry.CentralDirectoryOffset + 46 + entry.CentralNameLength + 2;

        // The subfield claims 30 data bytes, but the enclosing 28-byte extra area holds
        // a 4-byte header plus 24 data bytes; the enclosing lengths stay intact.
        Assert.Equal(30, ReadUInt16(mutated.ArchiveBytes, subfieldSizeOffset));
        Assert.Equal(28, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 30));
        Assert.Equal(control.ArchiveBytes.Length, mutated.ArchiveBytes.Length);

        AssertOnlyRangesChanged(
            control.ArchiveBytes, mutated.ArchiveBytes,
            (subfieldSizeOffset, 2));

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Contains("zip64-subfield-size=30", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains("enclosing-extra-area=28", mutation.DeclaredValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_Zip64Defects_ReferenceReaderFailsGracefullyWithoutGrowing()
    {
        // Both Zip64 defects keep the physical size unchanged (no allocation follows the
        // declared sizes), and the reference reader fails rather than misreading.
        var hidden = ArchiveFixtureMutator.Apply(
            ArchiveTestMutationKind.Zip64MissingExtra, BuildControl("valid-zip64-small"));
        var overrun = ArchiveFixtureMutator.Apply(
            ArchiveTestMutationKind.Zip64TruncatedExtra, BuildControl("valid-zip64-small"));

        foreach (var mutated in new[] { hidden, overrun })
        {
            using var archive = new ZipArchive(new MemoryStream(mutated.ArchiveBytes), ZipArchiveMode.Read);
            Assert.ThrowsAny<Exception>(() =>
            {
                using var entry = archive.Entries[0].Open();
            });
        }
    }

    // ---- Determinism and publication ----

    [Theory]
    [InlineData("valid-descriptor-signature")]
    [InlineData("valid-descriptor-no-signature")]
    [InlineData("valid-utf8-name")]
    [InlineData("valid-cp437-name")]
    [InlineData("valid-signatures-in-comment")]
    [InlineData("valid-zip64-small")]
    [InlineData("invalid-utf8-name")]
    [InlineData("zip64-missing-extra")]
    [InlineData("zip64-truncated-extra")]
    public void Build_CompatibilityCases_AreDeterministicPerCaseAndSeed(string caseKey)
    {
        var definition = ArchiveTestCatalog.GetCase(caseKey);

        var first = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var second = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);
        Assert.Equal(first.ArchiveSha256, second.ArchiveSha256);
        Assert.Equal(first.Mutations, second.Mutations);
    }

    [Fact]
    public async Task GenerateAsync_CompatibilityCases_PublishValidatedPairs()
    {
        var caseKeys = new[]
        {
            "valid-descriptor-signature", "valid-descriptor-no-signature", "valid-utf8-name",
            "valid-cp437-name", "valid-signatures-in-comment", "valid-zip64-small",
            "invalid-utf8-name", "zip64-missing-extra", "zip64-truncated-extra",
        };
        var tempDir = Path.Combine(TempDir, "compat");
        var result = await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create([.. caseKeys], 42, tempDir), CancellationToken.None);

        Assert.Equal(caseKeys.Length, result.FixtureIds.Count);
        foreach (var fixtureId in result.FixtureIds)
        {
            var testCase = ArchiveTestJson.Parse(
                await File.ReadAllBytesAsync(Path.Combine(result.PublishedDirectory, fixtureId + ".json")));
            Assert.Empty(ArchiveTestCaseSemantics.Validate(testCase));
            Assert.Equal(
                testCase.Archive.Sha256,
                Convert.ToHexStringLower(SHA256.HashData(
                    File.ReadAllBytes(Path.Combine(result.PublishedDirectory, fixtureId + ".zip")))));
            Assert.Contains(testCase.CaseKey, caseKeys);
        }
    }
}
