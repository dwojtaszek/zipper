using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ICSharpCode.SharpZipLib.BZip2;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class ArchiveFixtureMutationTests : WorkingDirectoryTestBase
{
    private static ArchiveFixtureArtifact BuildControl() =>
        ArchiveFixtureBuilder.BuildControl("valid-stored", 42, CancellationToken.None);

    private static byte[] ReadEntryContent(byte[] archiveBytes, string entryName)
    {
        using var archive = new ZipArchive(new MemoryStream(archiveBytes), ZipArchiveMode.Read);
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"entry '{entryName}' not found");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static int CountDifferingBytes(byte[] before, byte[] after)
    {
        Assert.Equal(before.Length, after.Length);
        var differences = 0;
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] != after[i])
            {
                differences++;
            }
        }

        return differences;
    }

    private static List<(long HeaderOffset, long Start, long End)> StoredBlockRanges(
        byte[] archiveBytes, ArchiveFixtureEntryLayout entry)
    {
        var compressed = archiveBytes.AsSpan((int)entry.DataOffset, (int)entry.CompressedSize);
        var ranges = new List<(long HeaderOffset, long Start, long End)>();
        var cursor = 0;
        var finalBlock = false;
        while (!finalBlock)
        {
            Assert.True(cursor < compressed.Length, "stored-block stream ended before its final block");
            var headerOffset = entry.DataOffset + cursor;
            var header = compressed[cursor++];
            finalBlock = (header & 1) != 0;
            Assert.Equal(0, (header >> 1) & 3);
            Assert.True(compressed.Length - cursor >= 4, "stored block is missing LEN/NLEN");

            var length = BinaryPrimitives.ReadUInt16LittleEndian(compressed[cursor..]);
            var complement = BinaryPrimitives.ReadUInt16LittleEndian(compressed[(cursor + 2)..]);
            cursor += 4;
            Assert.Equal((ushort)~length, complement);
            Assert.True(length <= compressed.Length - cursor, "stored-block literal data is truncated");

            ranges.Add((headerOffset, entry.DataOffset + cursor, entry.DataOffset + cursor + length));
            cursor += length;
        }

        Assert.Equal(compressed.Length, cursor);
        return ranges;
    }

    [Theory]
    [InlineData((int)ArchiveTestMutationKind.CrcLocalMismatch, "crc-local-mismatch", 1)]
    [InlineData((int)ArchiveTestMutationKind.CrcCentralMismatch, "crc-central-mismatch", 1)]
    // crc-both-mismatch is one fixture with two recorded field mutations; the first record is the local field.
    [InlineData((int)ArchiveTestMutationKind.CrcBothMismatch, "crc-local-mismatch", 2)]
    public void Apply_CrcMismatch_ChangesOnlyKnownCrcBytes(int kindValue, string expectedCode, int expectedDifferences)
    {
        var control = BuildControl();

        var mutated = ArchiveFixtureMutator.Apply((ArchiveTestMutationKind)kindValue, control);

        Assert.Equal(expectedCode, mutated.Mutations[0].Code);
        Assert.Equal(expectedDifferences, CountDifferingBytes(control.ArchiveBytes, mutated.ArchiveBytes));
        Assert.NotEqual(control.ArchiveSha256, mutated.ArchiveSha256);

        // Pin WHICH byte changed: with the difference count locked above, the flipped byte
        // must sit in the CRC field of the recorded structure (local +14 / central +16),
        // so a swapped FlipCrc branch cannot pass as a single-bit difference.
        var expectedOffset = kindValue == (int)ArchiveTestMutationKind.CrcCentralMismatch
            ? control.Layout.Entries[0].CentralDirectoryOffset + 16
            : control.Layout.Entries[0].LocalHeaderOffset + 14;
        var firstMutation = mutated.Mutations[0];
        Assert.Equal(expectedOffset, firstMutation.Offset);
        Assert.NotEqual(
            control.ArchiveBytes[(int)firstMutation.Offset],
            mutated.ArchiveBytes[(int)firstMutation.Offset]);

        // Only CRC bytes differ: the known baseline payload remains identical and still
        // reads back byte-for-byte through the reference reader (CRC is not re-verified on read).
        Assert.Equal(control.Entries[0].Content, ReadEntryContent(mutated.ArchiveBytes, "a.txt"));
        Assert.Equal(control.Entries[1].Content, ReadEntryContent(mutated.ArchiveBytes, "b.bin"));
    }

    [Fact]
    public void Apply_StoredPayloadBitflip_ChangesOnlyPayloadByteAtDataOffset()
    {
        var control = BuildControl();

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.StoredPayloadBitflip, control);

        Assert.Equal("stored-payload-bitflip", mutated.Mutations[0].Code);
        Assert.Equal("file-data", mutated.Mutations[0].Structure);
        Assert.Equal(1, CountDifferingBytes(control.ArchiveBytes, mutated.ArchiveBytes));
        Assert.NotEqual(control.ArchiveSha256, mutated.ArchiveSha256);

        var entry = control.Layout.Entries[0];
        var mutation = mutated.Mutations[0];
        Assert.Equal(entry.DataOffset, mutation.Offset);
        Assert.Equal(control.ArchiveBytes[(int)entry.DataOffset] ^ 0x01, mutated.ArchiveBytes[(int)entry.DataOffset]);
        Assert.Equal(
            $"payload-byte-flip=bit0 data-offset={entry.DataOffset} original-content-sha256={control.Entries[0].ContentSha256}",
            mutation.DeclaredValue);

        // Control payload != mutated payload:
        // .NET ZipArchive does not verify CRC when streaming stored content, so ReadEntryContent reads the corrupted payload.
        var mutatedPayload = ReadEntryContent(mutated.ArchiveBytes, "a.txt");
        Assert.NotEqual(control.Entries[0].Content, mutatedPayload);

        // Intact declarations: CRC, uncompressed size, compressed size in both headers unchanged
        var mutatedLayout = ArchiveFixtureLayout.Read(mutated.ArchiveBytes);
        Assert.Equal(entry.Crc32, mutatedLayout.Entries[0].Crc32);
        Assert.Equal(entry.CompressedSize, mutatedLayout.Entries[0].CompressedSize);
        Assert.Equal(entry.UncompressedSize, mutatedLayout.Entries[0].UncompressedSize);

        // Sidecar content hash is still the control's original content hash
        var definition = ArchiveTestCatalog.GetCase("stored-payload-bitflip");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var sidecar = ArchiveTestSuiteGenerator.BuildExpectationFile(artifact, definition, 42);
        Assert.Equal(control.Entries[0].ContentSha256, sidecar.Entries[0].ContentSha256);
    }

    [Fact]
    public void Apply_DeflatePayloadBitflip_ChangesLiteralByteAndPreservesDeclarations()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-deflate-stored-block", 42, CancellationToken.None);
        var definition = ArchiveTestCatalog.GetCase("deflate-payload-bitflip");

        var mutated = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        var entry = Assert.Single(control.Layout.Entries);
        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("deflate-payload-bitflip", mutation.Code);
        Assert.Equal("file-data", mutation.Structure);
        Assert.Equal(entry.Ordinal, mutation.Ordinal);
        Assert.Equal(1, CountDifferingBytes(control.ArchiveBytes, mutated.ArchiveBytes));
        Assert.NotEqual(control.ArchiveSha256, mutated.ArchiveSha256);

        var literalRanges = StoredBlockRanges(control.ArchiveBytes, entry);
        Assert.Contains(literalRanges, range => mutation.Offset >= range.Start && mutation.Offset < range.End);
        Assert.Equal(
            control.ArchiveBytes[(int)mutation.Offset] ^ 0x01,
            mutated.ArchiveBytes[(int)mutation.Offset]);
        Assert.Equal(
            $"payload-byte-flip=bit0 literal-data-offset={mutation.Offset} original-content-sha256={control.Entries[0].ContentSha256}",
            mutation.DeclaredValue);

        var mutatedLayout = ArchiveFixtureLayout.Read(mutated.ArchiveBytes);
        Assert.Equal(entry.Method, mutatedLayout.Entries[0].Method);
        Assert.Equal(entry.Crc32, mutatedLayout.Entries[0].Crc32);
        Assert.Equal(entry.CompressedSize, mutatedLayout.Entries[0].CompressedSize);
        Assert.Equal(entry.UncompressedSize, mutatedLayout.Entries[0].UncompressedSize);
        Assert.Equal(
            BinaryPrimitives.ReadUInt32LittleEndian(control.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset + 14, 4)),
            BinaryPrimitives.ReadUInt32LittleEndian(mutated.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset + 14, 4)));

        var decoded = ReadEntryContent(mutated.ArchiveBytes, entry.Name);
        Assert.NotEqual(control.Entries[0].Content, decoded);
        Assert.Equal(control.Entries[0].Content.Length, decoded.Length);
        Assert.NotEqual(entry.Crc32, ArchiveFixtureBuilder.Crc32(decoded));

        var sidecar = ArchiveTestSuiteGenerator.BuildExpectationFile(mutated, definition, 42);
        Assert.Equal(control.Entries[0].ContentSha256, sidecar.Entries[0].ContentSha256);
    }

    [Fact]
    public void Apply_VersionLocalCentralMismatch_PinsLocalTwoPointZeroAndCentralFourPointFive()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-deflate", 42, CancellationToken.None);
        var entry = Assert.Single(control.Layout.Entries);
        var localVersionOffset = entry.LocalHeaderOffset + 4;
        var centralVersionOffset = entry.CentralDirectoryOffset + 6;

        var mutated = ArchiveFixtureMutator.Apply(
            ArchiveTestMutationKind.VersionLocalCentralMismatch, control);

        Assert.Equal(20, BinaryPrimitives.ReadUInt16LittleEndian(control.ArchiveBytes.AsSpan((int)localVersionOffset, 2)));
        Assert.Equal(20, BinaryPrimitives.ReadUInt16LittleEndian(control.ArchiveBytes.AsSpan((int)centralVersionOffset, 2)));
        Assert.Equal(20, BinaryPrimitives.ReadUInt16LittleEndian(mutated.ArchiveBytes.AsSpan((int)localVersionOffset, 2)));
        Assert.Equal(45, BinaryPrimitives.ReadUInt16LittleEndian(mutated.ArchiveBytes.AsSpan((int)centralVersionOffset, 2)));
        Assert.Equal(control.ArchiveBytes.Length, mutated.ArchiveBytes.Length);
        Assert.Equal(1, CountDifferingBytes(control.ArchiveBytes, mutated.ArchiveBytes));

        Assert.Equal(2, mutated.Mutations.Count);
        var localMutation = mutated.Mutations[0];
        var centralMutation = mutated.Mutations[1];
        Assert.Equal("version-local-central-mismatch", localMutation.Code);
        Assert.Equal("local-header", localMutation.Structure);
        Assert.Equal(localVersionOffset, localMutation.Offset);
        Assert.Equal(2, localMutation.DeletedLength);
        Assert.Equal(2, localMutation.InsertedLength);
        Assert.Equal("version-local-central-mismatch", centralMutation.Code);
        Assert.Equal("central-header", centralMutation.Structure);
        Assert.Equal(centralVersionOffset, centralMutation.Offset);
        Assert.Equal(2, centralMutation.DeletedLength);
        Assert.Equal(2, centralMutation.InsertedLength);
        Assert.Equal(localMutation.BeforeHex, localMutation.AfterHex);
        Assert.NotEqual(centralMutation.BeforeHex, centralMutation.AfterHex);
        Assert.All(mutated.Mutations, mutation =>
            Assert.Equal("local-version-needed=20 central-version-needed=45", mutation.DeclaredValue));
        Assert.Equal(control.ArchiveSha256, localMutation.BeforeSha256);
        Assert.Equal(mutated.ArchiveSha256, localMutation.AfterSha256);
        Assert.Equal(control.ArchiveSha256, centralMutation.BeforeSha256);
        Assert.Equal(mutated.ArchiveSha256, centralMutation.AfterSha256);
    }

    [Theory]
    [InlineData("btype", "non-stored block")]
    [InlineData("short-len-nlen", "a stored block is missing LEN/NLEN")]
    [InlineData("len-nlen", "inconsistent LEN/NLEN")]
    [InlineData("missing-final", "stream ends before a final block")]
    [InlineData("literal-overrun", "literal data exceeds the compressed payload")]
    [InlineData("trailing-byte", "bytes follow the final stored block")]
    public void Apply_DeflatePayloadBitflip_InvalidStoredBlockFraming_ThrowsInvalidOperationException(
        string defect, string expectedMessage)
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-deflate-stored-block", 42, CancellationToken.None);
        var entry = control.Layout.Entries[0];
        var malformedBytes = (byte[])control.ArchiveBytes.Clone();
        var compressedOffset = checked((int)entry.DataOffset);
        var malformedControl = control with { ArchiveBytes = malformedBytes };

        switch (defect)
        {
            case "btype":
                malformedBytes[compressedOffset] |= 0x02;
                break;
            case "short-len-nlen":
                malformedBytes[compressedOffset] |= 0x01;
                malformedControl = control with
                {
                    ArchiveBytes = malformedBytes,
                    Layout = control.Layout with
                    {
                        Entries = [entry with { CompressedSize = 3 }],
                    },
                };
                break;
            case "len-nlen":
                malformedBytes[compressedOffset + 3] ^= 0x01;
                break;
            case "missing-final":
                {
                    var lastBlockHeaderOffset = StoredBlockRanges(control.ArchiveBytes, entry)[^1].HeaderOffset;
                    malformedBytes[(int)lastBlockHeaderOffset] &= 0xFE;
                    break;
                }
            case "literal-overrun":
                {
                    var lastBlockHeaderOffset = StoredBlockRanges(control.ArchiveBytes, entry)[^1].HeaderOffset;
                    BinaryPrimitives.WriteUInt16LittleEndian(malformedBytes.AsSpan((int)lastBlockHeaderOffset + 1), 2);
                    BinaryPrimitives.WriteUInt16LittleEndian(malformedBytes.AsSpan((int)lastBlockHeaderOffset + 3), unchecked((ushort)~2));
                    break;
                }
            case "trailing-byte":
                malformedControl = control with
                {
                    Layout = control.Layout with
                    {
                        Entries = [entry with { CompressedSize = checked(entry.CompressedSize + 1) }],
                    },
                };
                break;
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            ArchiveFixtureMutator.Apply(
                ArchiveTestMutationKind.DeflatePayloadBitflip,
                malformedControl));

        Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_DeflatePayloadBitflip_ZeroLengthStoredControl_ThrowsInvalidOperationException()
    {
        var definition = new ArchiveTestCaseDefinition(
            "empty-deflate-stored-block", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [ArchiveTestCatalog.CompatibilitySuite],
            Recipe: new ArchiveTestRecipe([ArchiveTestRecipeEntry.File("empty.txt", 0, "deflate")]),
            Construction: ArchiveControlConstruction.DeflateStoredBlock);
        var control = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        var error = Assert.Throws<InvalidOperationException>(
            () => ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.DeflatePayloadBitflip, control));

        Assert.Contains("no literal data to flip", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_StoredPayloadBitflip_NonStoredOrEmptyControl_ThrowsInvalidOperationException()
    {
        var emptyControl = ArchiveFixtureBuilder.BuildControl("valid-empty", 42, CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() =>
            ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.StoredPayloadBitflip, emptyControl));

        var deflateControl = ArchiveFixtureBuilder.BuildControl("valid-deflate", 42, CancellationToken.None);
        Assert.Throws<InvalidOperationException>(() =>
            ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.StoredPayloadBitflip, deflateControl));
    }

    [Fact]
    public void Apply_StoredPayloadBitflip_ZeroLengthStoredControl_ThrowsInvalidOperationException()
    {
        var definition = new ArchiveTestCaseDefinition(
            "empty-stored-control", CaseRevision: 1, ExpectationRevision: 1, Classification: "valid",
            Suites: [ArchiveTestCatalog.CompatibilitySuite],
            Recipe: new ArchiveTestRecipe([ArchiveTestRecipeEntry.File("empty.txt", 0, "stored")]));
        var control = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        var error = Assert.Throws<InvalidOperationException>(
            () => ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.StoredPayloadBitflip, control));

        Assert.Contains("non-zero payload size", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_CrcBothMismatch_RecordsBothChangedRangesWithSameWrongValue()
    {
        var control = BuildControl();

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.CrcBothMismatch, control);

        Assert.Equal(2, mutated.Mutations.Count);
        Assert.All(mutated.Mutations, mutation => Assert.Equal("before-mutation", mutation.OffsetBasis));

        var local = mutated.Mutations[0];
        var central = mutated.Mutations[1];
        Assert.Equal("local-header", local.Structure);
        Assert.Equal("central-header", central.Structure);
        Assert.Equal(control.Layout.Entries[0].LocalHeaderOffset + 14, local.Offset);
        Assert.Equal(control.Layout.Entries[0].CentralDirectoryOffset + 16, central.Offset);

        // Both recorded CRCs flip the same bit: the same wrong value appears in both headers.
        Assert.Equal(control.ArchiveBytes[(int)local.Offset] ^ 0x01, mutated.ArchiveBytes[(int)central.Offset]);

        Assert.All(mutated.Mutations, mutation =>
        {
            Assert.NotNull(mutation.BeforeSize);
            Assert.NotNull(mutation.AfterSize);
            Assert.Equal(control.ArchiveBytes.Length, mutation.BeforeSize);
            Assert.Equal(mutated.ArchiveBytes.Length, mutation.AfterSize);
        });
    }

    [Theory]
    [InlineData((int)ArchiveTestMutationKind.TruncatePayloadTail, "truncate-payload-tail", "file-data")]
    [InlineData((int)ArchiveTestMutationKind.TruncateCentralTail, "truncate-central-tail", "central-header")]
    [InlineData((int)ArchiveTestMutationKind.TruncateEocd, "truncate-eocd", "eocd")]
    [InlineData((int)ArchiveTestMutationKind.MissingEocd, "missing-eocd", "eocd")]
    public void Apply_Truncation_RemovesTailBytesAndRecordsThem(int kindValue, string expectedCode, string expectedStructure)
    {
        var control = BuildControl();

        var mutated = ArchiveFixtureMutator.Apply((ArchiveTestMutationKind)kindValue, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal(expectedCode, mutation.Code);
        Assert.Equal(expectedStructure, mutation.Structure);
        Assert.Equal("before-mutation", mutation.OffsetBasis);
        Assert.Equal(control.ArchiveBytes.Length, mutation.DeletedLength + mutated.ArchiveBytes.Length);
        Assert.Equal(mutated.ArchiveBytes.Length, mutation.AfterSize);
        Assert.True(mutated.ArchiveBytes.AsSpan().SequenceEqual(control.ArchiveBytes.AsSpan(0, mutated.ArchiveBytes.Length)),
            "the truncated Archive must be a byte prefix of the control");

        // Reconstruct the truncation from the logged operation and verify before/after hashes independently.
        var reconstructed = control.ArchiveBytes[..(int)mutation.Offset];
        Assert.Equal(reconstructed, mutated.ArchiveBytes);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(control.ArchiveBytes)),
            mutation.BeforeSha256);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(mutated.ArchiveBytes)),
            mutation.AfterSha256);
    }

    [Fact]
    public void Apply_TruncatePayloadTail_ExplanationNamesLaterStructures()
    {
        var control = BuildControl();

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.TruncatePayloadTail, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Contains("also removes", mutation.Explanation, StringComparison.Ordinal);
        Assert.Contains("central directory", mutation.Explanation, StringComparison.Ordinal);
        Assert.Contains("EOCD", mutation.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_MissingEocdArchive_IsUnopenableByReferenceReader()
    {
        var control = BuildControl();

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.MissingEocd, control);

        using var stream = new MemoryStream(mutated.ArchiveBytes);
        Assert.ThrowsAny<Exception>(() => new ZipArchive(stream, ZipArchiveMode.Read));
    }

    [Fact]
    public void Apply_MultidiskEocdDeclared_RecordsBothDiskFields()
    {
        var control = BuildControl();

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.MultidiskEocdDeclared, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("multidisk-eocd-declared", mutation.Code);
        Assert.Equal("eocd", mutation.Structure);
        Assert.Equal(control.Layout.EocdOffset + 4, mutation.Offset);
        Assert.Equal((ushort)1, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)mutation.Offset));
        Assert.Equal((ushort)1, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)mutation.Offset + 2));
        Assert.Equal((ushort)0, BitConverter.ToUInt16(control.ArchiveBytes, (int)mutation.Offset));
        Assert.Contains("disk-number=1", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains("central-directory-disk=1", mutation.DeclaredValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_MultidiskCentralEntryDeclared_RecordsEntryDiskField()
    {
        var control = BuildControl();

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.MultidiskCentralEntryDeclared, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("multidisk-central-entry-declared", mutation.Code);
        Assert.Equal("central-header", mutation.Structure);
        Assert.Equal(control.Layout.Entries[0].CentralDirectoryOffset + 34, mutation.Offset);
        Assert.Equal((ushort)1, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)mutation.Offset));
    }

    [Fact]
    public void Apply_EocdEntryCountMismatch_RecordsThisDiskCount()
    {
        var control = BuildControl();

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.EocdEntryCountMismatch, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("eocd-entry-count-mismatch", mutation.Code);
        Assert.Equal("eocd", mutation.Structure);
        Assert.Equal(control.Layout.EocdOffset + 8, mutation.Offset);
        Assert.Equal((ushort)1, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)mutation.Offset));
        Assert.Equal((ushort)2, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)mutation.Offset + 2));
    }

    [Fact]
    public void Apply_Zip64EocdEntryCountMismatch_RecordsTotalEntries()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-zip64-small", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.Zip64EocdEntryCountMismatch, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("zip64-eocd-entry-count-mismatch", mutation.Code);
        Assert.Equal("zip64-eocd", mutation.Structure);
        var locatorOffset = control.Layout.EocdOffset - 20;
        var zip64Offset = BitConverter.ToUInt32(control.ArchiveBytes, (int)locatorOffset + 8);
        Assert.Equal(zip64Offset + 32, (uint)mutation.Offset);
        Assert.Equal(2UL, BitConverter.ToUInt64(mutated.ArchiveBytes, (int)mutation.Offset));
        Assert.Equal(1UL, BitConverter.ToUInt64(mutated.ArchiveBytes, (int)mutation.Offset - 8));
    }

    [Fact]
    public void Apply_Zip64EocdOffsetDisagreement_RewritesOnlyZip64CentralDirectoryOffset()
    {
        var definition = ArchiveTestCatalog.GetCase("zip64-eocd-offset-disagreement");
        var controlConstruction = definition.ControlConstruction
            ?? throw new InvalidOperationException("The Zip64 EOCD disagreement case must select its real-value control.");
        var controlDefinition = ArchiveTestCatalog.GetCase(definition.ControlCaseKey!) with
        {
            Construction = controlConstruction,
        };
        var control = ArchiveFixtureBuilder.Build(controlDefinition, 42, CancellationToken.None);
        var eocdOffset = control.Layout.EocdOffset;
        var locatorOffset = eocdOffset - 20;
        var zip64EocdOffset = checked((long)BinaryPrimitives.ReadUInt64LittleEndian(
            control.ArchiveBytes.AsSpan((int)locatorOffset + 8, 8)));
        var regularCentralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(
            control.ArchiveBytes.AsSpan((int)eocdOffset + 16, 4));
        Assert.Equal(
            (ulong)regularCentralDirectoryOffset,
            BinaryPrimitives.ReadUInt64LittleEndian(
                control.ArchiveBytes.AsSpan((int)zip64EocdOffset + 48, 8)));

        var mutated = ArchiveFixtureMutator.Apply(
            ArchiveTestMutationKind.Zip64EocdOffsetDisagreement, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("zip64-eocd-offset-disagreement", mutation.Code);
        Assert.Equal("zip64-eocd", mutation.Structure);
        Assert.Equal(zip64EocdOffset + 48, mutation.Offset);
        Assert.Equal(8, mutation.DeletedLength);
        Assert.Equal(8, mutation.InsertedLength);
        Assert.Equal("zip64-cd-offset=+16", mutation.DeclaredValue);
        Assert.Equal(
            checked((ulong)regularCentralDirectoryOffset + 16),
            BinaryPrimitives.ReadUInt64LittleEndian(mutated.ArchiveBytes.AsSpan((int)mutation.Offset, 8)));
        Assert.Equal(
            regularCentralDirectoryOffset,
            BinaryPrimitives.ReadUInt32LittleEndian(mutated.ArchiveBytes.AsSpan((int)eocdOffset + 16, 4)));
        Assert.Equal(
            control.ArchiveBytes.AsSpan((int)regularCentralDirectoryOffset, (int)control.Layout.CentralDirectorySize).ToArray(),
            mutated.ArchiveBytes.AsSpan((int)regularCentralDirectoryOffset, (int)control.Layout.CentralDirectorySize).ToArray());
        Assert.Equal(control.ArchiveBytes.Length, mutated.ArchiveBytes.Length);

        var reconstructedControl = mutated.ArchiveBytes.ToArray();
        Convert.FromHexString(mutation.BeforeHex!).CopyTo(reconstructedControl, (int)mutation.Offset);
        Assert.Equal(control.ArchiveBytes, reconstructedControl);
        Assert.Equal(control.ArchiveSha256, mutation.BeforeSha256);
        Assert.Equal(mutated.ArchiveSha256, mutation.AfterSha256);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Apply_Zip64EocdOffsetDisagreement_RejectsControlWhenEitherOffsetIsWrong(
        bool regularEocdOffsetWrong,
        bool zip64EocdOffsetWrong)
    {
        var definition = ArchiveTestCatalog.GetCase("zip64-eocd-offset-disagreement");
        var controlConstruction = definition.ControlConstruction
            ?? throw new InvalidOperationException("The Zip64 EOCD disagreement case must select its real-value control.");
        var controlDefinition = ArchiveTestCatalog.GetCase(definition.ControlCaseKey!) with
        {
            Construction = controlConstruction,
        };
        var control = ArchiveFixtureBuilder.Build(controlDefinition, 42, CancellationToken.None);
        var zip64EocdOffset = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(
            control.ArchiveBytes.AsSpan((int)control.Layout.EocdOffset - 20 + 8, 8)));
        var malformedControlBytes = control.ArchiveBytes.ToArray();
        if (regularEocdOffsetWrong)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(
                malformedControlBytes.AsSpan((int)control.Layout.EocdOffset + 16, 4),
                checked((uint)control.Layout.CentralDirectoryOffset + 1));
        }

        if (zip64EocdOffsetWrong)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(
                malformedControlBytes.AsSpan(zip64EocdOffset + 48, 8),
                checked((ulong)control.Layout.CentralDirectoryOffset + 1));
        }

        var malformedControl = control with { ArchiveBytes = malformedControlBytes };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.Zip64EocdOffsetDisagreement, malformedControl));

        Assert.Contains("requires matching", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_Zip64ExtraWithoutSentinels_PreservesExtraStructureAndDisagreesWithHeaderSizes()
    {
        const string CaseKey = "zip64-extra-without-sentinels";
        var control = ArchiveFixtureBuilder.BuildControl("valid-zip64-small", 42, CancellationToken.None);
        var definition = ArchiveTestCatalog.GetCase(CaseKey);

        var mutated = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        var entry = Assert.Single(control.Layout.Entries);
        var localExtraOffset = entry.LocalHeaderOffset + 30 + entry.LocalNameLength;
        var centralExtraOffset = entry.CentralDirectoryOffset + 46 + entry.CentralNameLength;
        var expectedHeaderSize = checked((uint)control.Entries[0].Content.Length);
        var conflictingExtraSize = checked((ulong)expectedHeaderSize + 64);
        var actualLayout = ArchiveFixtureLayout.Read(mutated.ArchiveBytes);

        Assert.Equal(control.ArchiveBytes.Length, mutated.ArchiveBytes.Length);
        Assert.Equal(control.Layout.EocdOffset, actualLayout.EocdOffset);
        Assert.Equal(expectedHeaderSize, BinaryPrimitives.ReadUInt32LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset + 18, 4)));
        Assert.Equal(expectedHeaderSize, BinaryPrimitives.ReadUInt32LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset + 22, 4)));
        Assert.Equal(expectedHeaderSize, BinaryPrimitives.ReadUInt32LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)entry.CentralDirectoryOffset + 20, 4)));
        Assert.Equal(expectedHeaderSize, BinaryPrimitives.ReadUInt32LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)entry.CentralDirectoryOffset + 24, 4)));
        Assert.Equal(0U, BinaryPrimitives.ReadUInt32LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)entry.CentralDirectoryOffset + 42, 4)));
        Assert.Equal(expectedHeaderSize, actualLayout.Entries[0].CompressedSize);
        Assert.Equal(expectedHeaderSize, actualLayout.Entries[0].UncompressedSize);

        // Keep both 0x0001 subfields intact, but make their size values disagree with
        // the real 32-bit header fields. The extra ID/length bytes and local-header
        // offset value stay unchanged; the now-unneeded 32-bit offset sentinel is
        // cleared so readers do not interpret the extra's first size as an offset.
        Assert.Equal((ushort)0x0001, BinaryPrimitives.ReadUInt16LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)localExtraOffset, 2)));
        Assert.Equal((ushort)16, BinaryPrimitives.ReadUInt16LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)localExtraOffset + 2, 2)));
        Assert.Equal((ushort)0x0001, BinaryPrimitives.ReadUInt16LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)centralExtraOffset, 2)));
        Assert.Equal((ushort)24, BinaryPrimitives.ReadUInt16LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)centralExtraOffset + 2, 2)));
        Assert.Equal(conflictingExtraSize, BinaryPrimitives.ReadUInt64LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)localExtraOffset + 4, 8)));
        Assert.Equal(conflictingExtraSize, BinaryPrimitives.ReadUInt64LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)localExtraOffset + 12, 8)));
        Assert.Equal(conflictingExtraSize, BinaryPrimitives.ReadUInt64LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)centralExtraOffset + 4, 8)));
        Assert.Equal(conflictingExtraSize, BinaryPrimitives.ReadUInt64LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)centralExtraOffset + 12, 8)));
        Assert.Equal((ulong)entry.LocalHeaderOffset, BinaryPrimitives.ReadUInt64LittleEndian(
            mutated.ArchiveBytes.AsSpan((int)centralExtraOffset + 20, 8)));

        Assert.Equal(
            [
                entry.LocalHeaderOffset + 18,
                localExtraOffset + 4,
                entry.CentralDirectoryOffset + 20,
                centralExtraOffset + 4,
                entry.CentralDirectoryOffset + 42,
            ],
            mutated.Mutations.Select(mutation => mutation.Offset));
        Assert.Equal([8, 16, 8, 16, 4], mutated.Mutations.Select(mutation => mutation.DeletedLength));
        Assert.Equal(24, CountDifferingBytes(control.ArchiveBytes, mutated.ArchiveBytes));
        Assert.All(mutated.Mutations, mutation => Assert.Equal(CaseKey, mutation.Code));
    }

    [Fact]
    public void Apply_Zip64ExtraWithoutSentinels_WhenControlEntryCountsAreNotOne_Throws()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-zip64-small", 42, CancellationToken.None);

        Assert.Throws<InvalidOperationException>(() =>
            ArchiveFixtureMutator.Apply(
                ArchiveTestMutationKind.Zip64SentinelCleared,
                control with { Entries = [] }));
        Assert.Throws<InvalidOperationException>(() =>
            ArchiveFixtureMutator.Apply(
                ArchiveTestMutationKind.Zip64SentinelCleared,
                control with { Layout = control.Layout with { Entries = [] } }));
    }

    [Fact]
    public void Apply_Zip64LocatorDiskMismatch_RecordsTotalDisks()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-zip64-small", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.Zip64LocatorDiskMismatch, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("zip64-locator-disk-mismatch", mutation.Code);
        Assert.Equal("zip64-locator", mutation.Structure);
        var locatorOffset = control.Layout.EocdOffset - 20;
        Assert.Equal(locatorOffset + 16, mutation.Offset);
        Assert.Equal(2U, BitConverter.ToUInt32(mutated.ArchiveBytes, (int)mutation.Offset));
    }

    [Fact]
    public void Apply_Zip64LocatorAmbiguity_PlacesFakeRecordsInsideCentralDirectoryComment()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-signatures-in-comment", 42, CancellationToken.None);
        var entry = Assert.Single(control.Layout.Entries);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.Zip64LocatorAmbiguity, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.All(mutated.Mutations, record =>
            Assert.Equal(
                record.AfterSize!.Value,
                record.BeforeSize!.Value - record.DeletedLength!.Value + record.InsertedLength!.Value));
        var commentLength = ushort.MaxValue - (46 + entry.CentralNameLength + entry.CentralExtraLength);
        var eocdOffset = control.Layout.EocdOffset + commentLength;
        const int FakeCentralNameLength = 13;
        const int FakeCentralLength = 46 + FakeCentralNameLength;
        const int Zip64EocdLength = 56;
        const int Zip64LocatorLength = 20;
        var fileCommentOffset = entry.CentralDirectoryOffset + 46 + entry.CentralNameLength + entry.CentralExtraLength;
        var paddingLength = commentLength - FakeCentralLength - Zip64EocdLength - Zip64LocatorLength;
        var fakeCentralOffset = fileCommentOffset + paddingLength;
        var zip64EocdOffset = fakeCentralOffset + FakeCentralLength;
        var locatorOffset = zip64EocdOffset + Zip64EocdLength;

        uint ReadUInt32(long offset) =>
            BinaryPrimitives.ReadUInt32LittleEndian(mutated.ArchiveBytes.AsSpan((int)offset, 4));
        ushort ReadUInt16(long offset) =>
            BinaryPrimitives.ReadUInt16LittleEndian(mutated.ArchiveBytes.AsSpan((int)offset, 2));
        ulong ReadUInt64(long offset) =>
            BinaryPrimitives.ReadUInt64LittleEndian(mutated.ArchiveBytes.AsSpan((int)offset, 8));

        Assert.Equal("zip64-locator-ambiguity", mutation.Code);
        Assert.Equal("whole-archive", mutation.Structure);
        Assert.Equal(control.Layout.CentralDirectoryOffset, mutation.Offset);
        Assert.Equal(control.ArchiveBytes.Length - mutation.Offset, mutation.DeletedLength);
        Assert.Equal(mutated.ArchiveBytes.Length - mutation.Offset, mutation.InsertedLength);
        Assert.Contains($"file-comment-offset={fileCommentOffset}", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains($"file-comment-length={commentLength}", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains($"fake-central-header-offset={fakeCentralOffset}", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains($"fake-zip64-eocd-offset={zip64EocdOffset}", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains($"fake-zip64-locator-offset={locatorOffset}", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains($"central-header-comment-length={commentLength}", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains($"central-directory-size={ushort.MaxValue}", mutation.DeclaredValue, StringComparison.Ordinal);

        Assert.Equal(ArchiveFixtureLayout.CentralHeaderSignature, ReadUInt32(fakeCentralOffset));
        Assert.Equal("fake_file.txt", Encoding.ASCII.GetString(
            mutated.ArchiveBytes.AsSpan((int)fakeCentralOffset + 46, FakeCentralNameLength)));
        Assert.Equal(ArchiveFixtureLayout.Zip64EocdSignature, ReadUInt32(zip64EocdOffset));
        Assert.Equal(44UL, ReadUInt64(zip64EocdOffset + 4));
        Assert.Equal((ulong)FakeCentralLength, ReadUInt64(zip64EocdOffset + 40));
        Assert.Equal((ulong)fakeCentralOffset, ReadUInt64(zip64EocdOffset + 48));
        Assert.Equal(ArchiveFixtureLayout.Zip64LocatorSignature, ReadUInt32(locatorOffset));
        Assert.Equal((ulong)zip64EocdOffset, ReadUInt64(locatorOffset + 8));
        Assert.Equal(1U, ReadUInt32(locatorOffset + 16));
        Assert.Equal(eocdOffset - 20, locatorOffset);

        Assert.Equal(commentLength, ReadUInt16(entry.CentralDirectoryOffset + 32));
        Assert.Equal(ArchiveFixtureLayout.CentralHeaderSignature, ReadUInt32(entry.CentralDirectoryOffset));
        Assert.Equal((uint)ushort.MaxValue, ReadUInt32(eocdOffset + 12));
        Assert.Equal((uint)control.Layout.CentralDirectoryOffset, ReadUInt32(eocdOffset + 16));
        Assert.Equal(1, ReadUInt16(eocdOffset + 8));
        Assert.Equal(1, ReadUInt16(eocdOffset + 10));
        Assert.Equal(ArchiveFixtureLayout.EocdSignature, ReadUInt32(eocdOffset));
        Assert.NotEqual(
            ArchiveFixtureLayout.Zip64LocatorSignature,
            BinaryPrimitives.ReadUInt32LittleEndian(
                control.ArchiveBytes.AsSpan((int)control.Layout.EocdOffset - 20, 4)));

        var originalEocd = control.ArchiveBytes.AsSpan((int)control.Layout.EocdOffset, 22).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(originalEocd.AsSpan(12), ushort.MaxValue);
        Assert.Equal(originalEocd, mutated.ArchiveBytes.AsSpan((int)eocdOffset, 22).ToArray());
        Assert.Equal(
            control.ArchiveBytes.AsSpan((int)control.Layout.EocdOffset + 22).ToArray(),
            mutated.ArchiveBytes.AsSpan((int)eocdOffset + 22, ArchiveTestCatalog.SignatureCommentLength).ToArray());

        var lengthAnchored = ArchiveFixtureLayout.Read(mutated.ArchiveBytes, ArchiveTestCatalog.SignatureCommentLength);
        Assert.Equal(eocdOffset, lengthAnchored.EocdOffset);
        Assert.Equal((uint)ushort.MaxValue, lengthAnchored.CentralDirectorySize);
        var parsedEntry = Assert.Single(lengthAnchored.Entries);
        Assert.Equal("note.txt", parsedEntry.Name);
        Assert.Equal(commentLength, parsedEntry.CentralCommentLength);
    }

    [Fact]
    public void Apply_UndeclaredTrailingData_EmptyControl_ThrowsInvalidOperationException()
    {
        var emptyControl = ArchiveFixtureBuilder.BuildControl("valid-empty", 42, CancellationToken.None);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.UndeclaredTrailingData, emptyControl));

        Assert.Contains("non-empty Stored control", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_UndeclaredTrailingData_NonStoredControl_ThrowsInvalidOperationException()
    {
        var nonStoredControl = ArchiveFixtureBuilder.BuildControl("valid-deflate", 42, CancellationToken.None);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.UndeclaredTrailingData, nonStoredControl));

        Assert.Contains("non-empty Stored control", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_UndeclaredTrailingData_ControlWithTrailingBytes_ThrowsInvalidOperationException()
    {
        var control = BuildControl();
        var archiveBytes = new byte[control.ArchiveBytes.Length + 1];
        control.ArchiveBytes.CopyTo(archiveBytes, 0);
        archiveBytes[^1] = 0x41;
        var controlWithTrailingBytes = control with { ArchiveBytes = archiveBytes };

        var error = Assert.Throws<InvalidOperationException>(() =>
            ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.UndeclaredTrailingData, controlWithTrailingBytes));

        Assert.Contains("non-empty Stored control", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_UndeclaredTrailingData_NonzeroCommentLength_ThrowsInvalidOperationException()
    {
        var control = BuildControl();
        var archiveBytes = control.ArchiveBytes.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(
            archiveBytes.AsSpan((int)control.Layout.EocdOffset + 20, 2), 1);
        var controlWithCommentLength = control with { ArchiveBytes = archiveBytes };

        var error = Assert.Throws<InvalidOperationException>(() =>
            ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.UndeclaredTrailingData, controlWithCommentLength));

        Assert.Contains("non-empty Stored control", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_UndeclaredTrailingData_AppendsBytesWithoutDeclaringThem()
    {
        const string TrailingData = "zipper-tail-data";
        var control = BuildControl();
        var eocdOffset = control.Layout.EocdOffset;

        var mutated = ArchiveFixtureMutator.Apply(
            ArchiveTestMutationKind.UndeclaredTrailingData, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("trailing-data-undeclared", mutation.Code);
        Assert.Equal("whole-archive", mutation.Structure);
        Assert.Equal(control.ArchiveBytes.Length, mutation.Offset);
        Assert.Equal(0, mutation.DeletedLength);
        Assert.Equal(TrailingData.Length, mutation.InsertedLength);
        Assert.Equal($"trailing-bytes={TrailingData.Length} undeclared=true", mutation.DeclaredValue);
        Assert.Equal(control.ArchiveBytes.Length + TrailingData.Length, mutated.ArchiveBytes.Length);
        Assert.Equal(
            control.ArchiveBytes,
            mutated.ArchiveBytes.AsSpan(0, control.ArchiveBytes.Length).ToArray());
        Assert.Equal(
            Encoding.ASCII.GetBytes(TrailingData),
            mutated.ArchiveBytes.AsSpan(control.ArchiveBytes.Length).ToArray());
        Assert.Equal(
            control.ArchiveBytes.AsSpan((int)eocdOffset, 22).ToArray(),
            mutated.ArchiveBytes.AsSpan((int)eocdOffset, 22).ToArray());
        Assert.Equal(
            (ushort)0,
            BinaryPrimitives.ReadUInt16LittleEndian(
                mutated.ArchiveBytes.AsSpan((int)eocdOffset + 20, 2)));
        Assert.Equal(control.ArchiveSha256, mutation.BeforeSha256);
        Assert.Equal(mutated.ArchiveSha256, mutation.AfterSha256);
    }

    [Fact]
    public void Apply_DescriptorCrcDisagreement_FlipsDescriptorCrcOnly()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-descriptor-signature", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.DescriptorCrcDisagreement, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("descriptor-crc-disagreement", mutation.Code);
        Assert.Equal("data-descriptor", mutation.Structure);
        var descriptorOffset = control.Layout.Entries[0].DataDescriptorOffset;
        Assert.True(descriptorOffset > 0, "control must carry a data descriptor");
        Assert.Equal(descriptorOffset + 4, mutation.Offset);
        Assert.NotEqual(
            control.ArchiveBytes[(int)mutation.Offset],
            mutated.ArchiveBytes[(int)mutation.Offset]);
        Assert.Equal(1, CountDifferingBytes(control.ArchiveBytes, mutated.ArchiveBytes));
    }

    [Fact]
    public void Apply_DescriptorSizeDisagreement_RewritesDescriptorCompressedSize()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-descriptor-signature", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.DescriptorSizeDisagreement, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("descriptor-size-disagreement", mutation.Code);
        Assert.Equal("data-descriptor", mutation.Structure);
        var descriptorOffset = control.Layout.Entries[0].DataDescriptorOffset;
        Assert.Equal(descriptorOffset + 8, mutation.Offset);
        var declared = BitConverter.ToUInt32(mutated.ArchiveBytes, (int)mutation.Offset);
        Assert.NotEqual((uint)control.Entries[0].Content.Length, declared);
        Assert.Contains("descriptor-compressed-size=", mutation.DeclaredValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_DuplicateZip64Extra_AppendsConflictingSubfieldAndRelinks()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-zip64-small", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.DuplicateZip64Extra, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("duplicate-zip64-extra", mutation.Code);
        Assert.Equal("central-header", mutation.Structure);
        // The central extra grows by one 12-byte subfield; the EOCD span and
        // the Zip64 records move with it.
        Assert.Equal(control.ArchiveBytes.Length + 12, mutated.ArchiveBytes.Length);
        var entry = control.Layout.Entries[0];
        var extraEnd = entry.CentralDirectoryOffset + 46 + entry.CentralNameLength + entry.CentralExtraLength;
        Assert.Equal(extraEnd, mutation.Offset);
        Assert.Equal(0x0001, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)mutation.Offset));
        var first = BitConverter.ToUInt64(mutated.ArchiveBytes, (int)entry.CentralDirectoryOffset + 46 + entry.CentralNameLength + 4);
        var second = BitConverter.ToUInt64(mutated.ArchiveBytes, (int)mutation.Offset + 4);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Apply_DuplicateUnicodePath_AppendsConflictingNameInBothHeaders()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-unicode-path", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.DuplicateUnicodePath, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("duplicate-unicode-path", mutation.Code);
        // The clean control's Unicode name equals "safe.txt": each duplicate
        // subfield is 4 + (1 + 4 + 8) = 17 bytes, one per header.
        Assert.Equal(control.ArchiveBytes.Length + 34, mutated.ArchiveBytes.Length);
        Assert.Equal(0, mutation.DeletedLength);
        Assert.Equal(34, mutation.InsertedLength);
        Assert.Equal(0x7075, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)mutation.Offset));

        // Both extra areas walk as two well-formed 0x7075 subfields with equal
        // sizes and CRCs but a divergent first name byte: a corrupt splice
        // (shifted neighbors instead of the duplicate) cannot pass this walk.
        var layoutEntry = control.Layout.Entries[0];
        AssertDuplicateUnicodePathExtra(mutated.ArchiveBytes, (int)layoutEntry.LocalHeaderOffset + 30 + layoutEntry.LocalNameLength);
        var centralShift = mutated.ArchiveBytes.Length - control.ArchiveBytes.Length - 17;
        AssertDuplicateUnicodePathExtra(mutated.ArchiveBytes, (int)layoutEntry.CentralDirectoryOffset + centralShift + 46 + layoutEntry.CentralNameLength);
    }

    private static void AssertDuplicateUnicodePathExtra(byte[] archive, int extraStart)
    {
        Assert.Equal(0x7075, BitConverter.ToUInt16(archive, extraStart));
        var firstSize = BitConverter.ToUInt16(archive, extraStart + 2);
        Assert.Equal(0x7075, BitConverter.ToUInt16(archive, extraStart + 4 + firstSize));
        var secondSize = BitConverter.ToUInt16(archive, extraStart + 4 + firstSize + 2);
        Assert.Equal(firstSize, secondSize);
        var first = archive.AsSpan(extraStart + 4, firstSize);
        var second = archive.AsSpan(extraStart + 4 + firstSize + 4, secondSize);
        Assert.True(first[..5].SequenceEqual(second[..5]), "version and CRC must agree; only the name diverges");
        Assert.Equal(8, firstSize - 5);
        Assert.NotEqual(first[5], second[5]);
        Assert.True(first[6..].SequenceEqual(second[6..]), "only the first name byte diverges");
    }

    [Fact]
    public void Apply_Utf8FlagCp437Name_SetsBit11InBothHeaders()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-cp437-name", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.Utf8FlagCp437Name, control);

        Assert.Equal(2, mutated.Mutations.Count);
        var local = mutated.Mutations[0];
        var central = mutated.Mutations[1];
        Assert.Equal(control.Layout.Entries[0].LocalHeaderOffset + 6, local.Offset);
        Assert.Equal(control.Layout.Entries[0].CentralDirectoryOffset + 8, central.Offset);
        Assert.Equal(0x0800, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)local.Offset) & 0x0800);
        Assert.Equal(0x0800, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)central.Offset) & 0x0800);
        Assert.Equal(0x82, mutated.ArchiveBytes[(int)control.Layout.Entries[0].LocalHeaderOffset + 30 + 3]);
    }

    [Fact]
    public void Apply_UnicodePathCrcMismatch_FlipsSubfieldCrcInBothHeaders()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-unicode-path", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.UnicodePathCrcMismatch, control);

        Assert.Equal(2, mutated.Mutations.Count);
        Assert.All(mutated.Mutations, mutation => Assert.Equal("unicode-path-crc-mismatch", mutation.Code));
    }

    [Fact]
    public void Apply_UnicodePathNameDivergence_ChangesUnicodeNameOnly()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-unicode-path", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.UnicodePathNameDivergence, control);

        Assert.Equal(2, mutated.Mutations.Count);
        Assert.All(mutated.Mutations, mutation => Assert.Equal("unicode-path-name-divergence", mutation.Code));
        Assert.Equal(control.ArchiveBytes.Length, mutated.ArchiveBytes.Length);
    }

    [Fact]
    public void Apply_MixedMethodsOneCorruptMember_CorruptsOnlyBzip2MemberByOrdinal()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-mixed-methods", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.MixedMethodsOneCorruptMember, control);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("mixed-methods-one-corrupt-member", mutation.Code);
        Assert.Equal("file-data", mutation.Structure);
        Assert.Equal(2, mutation.Ordinal);
        var bzip2 = control.Layout.Entries[2];
        Assert.Equal((ushort)12, bzip2.Method);
        Assert.Equal(bzip2.DataOffset + 4, mutation.Offset);

        // Healthy members stay independently readable by ordinal with matching content.
        Assert.Equal(control.Entries[0].Content, ReadEntryContent(mutated.ArchiveBytes, "a.txt"));
        Assert.Equal(control.Entries[1].Content, ReadEntryContent(mutated.ArchiveBytes, "b.bin"));

        // The corrupt member is genuinely undecodable, not just relabeled: a
        // real BZip2 decoder (.NET has none) rejects the broken block magic.
        var member = control.Layout.Entries[2];
        Assert.ThrowsAny<Exception>(() =>
        {
            using var raw = new MemoryStream(mutated.ArchiveBytes, (int)member.DataOffset, (int)member.CompressedSize, writable: false);
            using var bz2 = new BZip2InputStream(raw);
            using var copy = new MemoryStream();
            bz2.CopyTo(copy);
        });
    }

    [Fact]
    public void Apply_MixedMethodsOneUnsupportedMember_Declares98OnBzip2MemberOnly()
    {
        var control = ArchiveFixtureBuilder.BuildControl("valid-mixed-methods", 42, CancellationToken.None);

        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.MixedMethodsOneUnsupportedMember, control);

        Assert.Equal(2, mutated.Mutations.Count);
        Assert.All(mutated.Mutations, mutation => Assert.Equal("mixed-methods-one-unsupported-member", mutation.Code));
        var bzip2 = control.Layout.Entries[2];
        Assert.Equal(bzip2.LocalHeaderOffset + 8, mutated.Mutations[0].Offset);
        Assert.Equal(bzip2.CentralDirectoryOffset + 10, mutated.Mutations[1].Offset);
        Assert.Equal((ushort)98, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)mutated.Mutations[0].Offset));
        Assert.Equal((ushort)98, BitConverter.ToUInt16(mutated.ArchiveBytes, (int)mutated.Mutations[1].Offset));

        // Healthy members stay independently readable; the 98 member is cleanly rejected.
        Assert.Equal(control.Entries[0].Content, ReadEntryContent(mutated.ArchiveBytes, "a.txt"));
        Assert.Equal(control.Entries[1].Content, ReadEntryContent(mutated.ArchiveBytes, "b.bin"));
        using var archive = new ZipArchive(new MemoryStream(mutated.ArchiveBytes), ZipArchiveMode.Read);
        Assert.Throws<System.IO.InvalidDataException>(() => archive.Entries[2].Open());
    }

    [Fact]
    public void Build_MutatedCases_AreDeterministicPerCaseAndSeed()
    {
        var first = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("truncate-eocd"), 42, CancellationToken.None);
        var second = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("truncate-eocd"), 42, CancellationToken.None);

        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);
        Assert.Equal(first.ArchiveSha256, second.ArchiveSha256);
    }

    [Fact]
    public void Build_MalformedCase_KeepsControlLayoutAndRecordsMutations()
    {
        var definition = ArchiveTestCatalog.GetCase("crc-both-mismatch");
        var artifact = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        Assert.Equal(2, artifact.Mutations.Count);
        // The artifact keeps the control's layout: before-mutation coordinates.
        Assert.Equal(2, artifact.Layout.EntryCount);
        Assert.Equal(2, artifact.Entries.Count);
        Assert.Equal("a.txt", artifact.Layout.Entries[0].Name);
    }

    [Fact]
    public async Task GenerateAsync_MalformedSuite_PublishesStandalonePairsWithMutationRecords()
    {
        var malformedCases = ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.MalformedSuite);

        var result = await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create(malformedCases.Select(c => c.CaseKey).ToList(), 42, Path.Combine(TempDir, "malformed")),
            CancellationToken.None);

        Assert.Equal(malformedCases.Count, result.FixtureIds.Count);
        Assert.Equal(malformedCases.Count * 2, Directory.GetFiles(result.PublishedDirectory).Length);

        // The missing-EOCD Archive is unopenable, but its adjacent JSON still parses,
        // matches the filename, and verifies the final hash from disk bytes.
        var zipShaById = result.FixtureIds
            .Select(id => (
                Id: id,
                Sha: Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(Path.Combine(result.PublishedDirectory, id + ".zip"))))))
            .ToDictionary(pair => pair.Id, pair => pair.Sha);

        var classificationByKey = malformedCases.ToDictionary(c => c.CaseKey, c => c.Classification);
        foreach (var fixtureId in result.FixtureIds)
        {
            var testCase = ArchiveTestJson.Parse(
                await File.ReadAllBytesAsync(Path.Combine(result.PublishedDirectory, fixtureId + ".json")));

            Assert.Equal(fixtureId, testCase.FixtureId);
            Assert.Equal(classificationByKey[testCase.CaseKey], testCase.Classification);
            Assert.NotEmpty(testCase.Mutations);
            Assert.All(testCase.Mutations, mutation => Assert.Equal("before-mutation", mutation.OffsetBasis));
            Assert.Equal(zipShaById[fixtureId], testCase.Archive.Sha256);
            Assert.Empty(ArchiveTestCaseSemantics.Validate(testCase));
        }
    }

    [Fact]
    public void Validate_TruncationPointingBeyondFinalLength_IsAcceptedOnBeforeBasis()
    {
        var control = BuildControl();
        var mutated = ArchiveFixtureMutator.Apply(ArchiveTestMutationKind.TruncateEocd, control);

        // A truncation record describes deleted bytes of the larger before-Archive: the
        // offset can equal (or exceed) the final smaller size, which physicalSize-based
        // bounds would wrongly reject.
        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal(mutated.ArchiveBytes.Length, (int)mutation.Offset);
        Assert.Equal(control.ArchiveBytes.Length, mutation.DeletedLength!.Value + mutated.ArchiveBytes.Length);

        var testCase = ArchiveTestJsonTests.ValidEmptyCase() with { Mutations = [mutation] };
        Assert.Empty(ArchiveTestCaseSemantics.Validate(testCase));
    }
}
