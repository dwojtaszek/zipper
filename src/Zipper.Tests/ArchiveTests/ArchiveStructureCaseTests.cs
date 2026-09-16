using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Byte-level assertions for the ticket #840 structure-mismatch cases: every targeted
/// field's original value, final value, and unchanged neighboring bytes; the two new
/// valid controls (extra field, signature-like payload); and the capability distinctions
/// between unsupported codec, corrupt local fields, and truncated structure.
/// </summary>
public class ArchiveStructureCaseTests : TempDirectoryTestBase
{
    private const ushort UnsupportedMethodCode = 98;
    private const ushort UnsupportedMethodDeflate64Code = 9;

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

    /// <summary>
    /// Every differing byte must lie inside one of the mutated field ranges (unchanged
    /// neighboring bytes), and at least one byte must actually change. Little-endian
    /// rewrites of small values change fewer bytes than the field width, so the count
    /// itself is not asserted.
    /// </summary>
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

    private static ushort ReadUInt16(byte[] bytes, long offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan((int)offset, 2));

    private static uint ReadUInt32(byte[] bytes, long offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)offset, 4));

    private static MutatedArchiveFixture Apply(string caseKey)
    {
        var definition = ArchiveTestCatalog.GetCase(caseKey);
        Assert.True(definition.IsMutation, $"case '{caseKey}' must be a mutation case");
        return ArchiveFixtureMutator.Apply(definition.Mutations![0], BuildControl(definition.ControlCaseKey!));
    }

    // ---- New valid controls (ticket #840 step 1) ----

    [Fact]
    public void Build_ValidExtraFieldControl_IsReadableWithMatchingHashes()
    {
        var control = BuildControl("valid-extra-field");

        Assert.Equal(8, control.Layout.Entries[0].LocalExtraLength);
        Assert.Equal(control.Entries.Count, control.Layout.Entries.Count);
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(control.Entries[0].Content)),
            Convert.ToHexStringLower(SHA256.HashData(ReadEntryContent(control.ArchiveBytes, "a.txt"))));
        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(control.Entries[1].Content)),
            Convert.ToHexStringLower(SHA256.HashData(ReadEntryContent(control.ArchiveBytes, "b.bin"))));
    }

    [Fact]
    public void Build_ValidExtraFieldControl_ShiftsLinkedOffsetsByInsertedLength()
    {
        var stored = BuildControl("valid-stored");
        var extra = BuildControl("valid-extra-field");

        // The enrichment's linked mechanical changes: exactly 8 bytes inserted, every later
        // physical offset and the central directory shift by 8.
        Assert.Equal(stored.ArchiveBytes.Length + 8, extra.ArchiveBytes.Length);
        Assert.Equal(stored.Layout.Entries[0].DataOffset + 8, extra.Layout.Entries[0].DataOffset);
        Assert.Equal(stored.Layout.Entries[1].LocalHeaderOffset + 8, extra.Layout.Entries[1].LocalHeaderOffset);
        Assert.Equal(stored.Layout.CentralDirectoryOffset + 8, extra.Layout.CentralDirectoryOffset);
        Assert.Equal(stored.Layout.CentralDirectorySize, extra.Layout.CentralDirectorySize);
    }

    [Fact]
    public void Build_ValidSignaturePayloadControl_KeepsSignatureBytesAsData()
    {
        var control = BuildControl("valid-signature-payload");

        // The payload starts with local-header-signature-like bytes that are pure data.
        Assert.Equal([0x50, 0x4b, 0x03, 0x04, 0x14, 0x00], control.Entries[0].Content[..6]);
        Assert.Single(control.Layout.Entries);
        Assert.Equal(
            control.Entries[0].Content,
            ReadEntryContent(control.ArchiveBytes, "sig.txt"));
    }

    // ---- Byte-level mutation assertions ----

    [Fact]
    public void Apply_NameMismatch_RewritesLocalNameOnly()
    {
        var control = BuildControl("valid-stored");
        var mutated = Apply("name-local-central-mismatch");

        var entry = control.Layout.Entries[0];
        var nameOffset = entry.LocalHeaderOffset + 30;
        Assert.Equal(
            "z.txt",
            Encoding.UTF8.GetString(mutated.ArchiveBytes.AsSpan((int)nameOffset, 5)));
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (nameOffset, 5));

        // The central header keeps the control's name bytes; the artifact entries keep
        // the physical (central) name. Both raw names are recorded in the mutation.
        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("612e747874", mutation.BeforeHex);
        Assert.Equal("7a2e747874", mutation.AfterHex);
        Assert.Contains("local-name=z.txt", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains("central-name=a.txt", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Equal(nameOffset, mutation.Offset);
        Assert.Equal(5, mutation.DeletedLength);
        Assert.Equal(5, mutation.InsertedLength);
    }

    [Fact]
    public void Apply_MethodMismatch_ChangesLocalMethodOnly()
    {
        var control = BuildControl("valid-stored");
        var mutated = Apply("method-local-central-mismatch");

        var entry = control.Layout.Entries[0];
        Assert.Equal(8, ReadUInt16(mutated.ArchiveBytes, entry.LocalHeaderOffset + 8));
        Assert.Equal(0, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 10));
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (entry.LocalHeaderOffset + 8, 2));

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal(entry.LocalHeaderOffset + 8, mutation.Offset);
        Assert.Equal("local-method=8 central-method=0", mutation.DeclaredValue);
    }

    [Fact]
    public void Apply_SizeMismatch_ChangesLocalCompressedSizeOnly()
    {
        var control = BuildControl("valid-stored");
        var mutated = Apply("size-local-central-mismatch");

        var entry = control.Layout.Entries[0];
        Assert.Equal(entry.CompressedSize + 1, ReadUInt32(mutated.ArchiveBytes, entry.LocalHeaderOffset + 18));
        Assert.Equal(entry.CompressedSize, ReadUInt32(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 20));
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (entry.LocalHeaderOffset + 18, 4));

        // The content and the central size are unchanged: the defect is the local lie only.
        Assert.Equal(
            control.Entries[0].Content,
            ReadEntryContent(mutated.ArchiveBytes, "a.txt"));
    }

    [Fact]
    public void Apply_OffsetOutsideArchive_WritesNearBoundaryDeclaredOffset()
    {
        var control = BuildControl("valid-stored");
        var mutated = Apply("offset-outside-archive");

        var entry = control.Layout.Entries[0];
        Assert.Equal(0x7FFF_0000u, ReadUInt32(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 42));
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (entry.CentralDirectoryOffset + 42, 4));

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Contains("declared-local-header-offset=2147418112", mutation.DeclaredValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_OffsetIntoPayload_PointsAtSignatureLikeData()
    {
        var control = BuildControl("valid-signature-payload");
        var mutated = Apply("offset-into-payload");

        var entry = control.Layout.Entries[0];
        Assert.Equal((uint)entry.DataOffset, ReadUInt32(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 42));
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (entry.CentralDirectoryOffset + 42, 4));

        // The declared offset lands on the payload's leading signature-like bytes: data,
        // not a genuine local header.
        Assert.Equal(0x50, mutated.ArchiveBytes[(int)entry.DataOffset]);
        Assert.Equal(0x4b, mutated.ArchiveBytes[(int)entry.DataOffset + 1]);
        Assert.Equal(0x03, mutated.ArchiveBytes[(int)entry.DataOffset + 2]);
        Assert.Equal(0x04, mutated.ArchiveBytes[(int)entry.DataOffset + 3]);
    }

    [Fact]
    public void Apply_ExtraFieldOverrun_InflatesSubfieldSizeOnly()
    {
        var control = BuildControl("valid-extra-field");
        var mutated = Apply("extra-field-length-overrun");

        var entry = control.Layout.Entries[0];
        var subfieldSizeOffset = entry.LocalHeaderOffset + 30 + entry.LocalNameLength + 2;

        // The subfield claims 16 bytes, but the enclosing extra area stays 8 bytes wide.
        Assert.Equal(16, ReadUInt16(mutated.ArchiveBytes, subfieldSizeOffset));
        Assert.Equal(8, ReadUInt16(mutated.ArchiveBytes, entry.LocalHeaderOffset + 28));
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (subfieldSizeOffset, 2));

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal(subfieldSizeOffset, mutation.Offset);
        Assert.Contains("enclosing-extra-area=8", mutation.DeclaredValue, StringComparison.Ordinal);

        // Readers that skip extra fields by the enclosing length still read the entry.
        Assert.Equal(
            control.Entries[0].Content,
            ReadEntryContent(mutated.ArchiveBytes, "a.txt"));
    }

    [Fact]
    public void Apply_UnsupportedMethod_SetsConsistentReservedCodeInBothHeaders()
    {
        var control = BuildControl("valid-stored");
        var mutated = Apply("unsupported-method");

        var entry = control.Layout.Entries[0];
        Assert.Equal(UnsupportedMethodCode, ReadUInt16(mutated.ArchiveBytes, entry.LocalHeaderOffset + 8));
        Assert.Equal(UnsupportedMethodCode, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 10));
        AssertOnlyRangesChanged(
            control.ArchiveBytes, mutated.ArchiveBytes,
            (entry.LocalHeaderOffset + 8, 2), (entry.CentralDirectoryOffset + 10, 2));

        // Two parallel field records (local + central), same transition.
        Assert.Equal(2, mutated.Mutations.Count);
        Assert.Equal("local-header", mutated.Mutations[0].Structure);
        Assert.Equal("central-header", mutated.Mutations[1].Structure);
        Assert.All(mutated.Mutations, mutation =>
            Assert.Equal("method=98", mutation.DeclaredValue, StringComparer.Ordinal));

        // The payload stays the stored control bytes: an unsupported codec, not corrupt data.
        Assert.Equal(control.Entries[0].Content[..16], mutated.ArchiveBytes.AsSpan((int)entry.DataOffset, 16).ToArray());
    }

    [Fact]
    public void Apply_EncryptionFlag_SetsBitOnBothHeadersWithoutTouchingPayload()
    {
        var control = BuildControl("valid-stored");
        var mutated = Apply("encryption-flag-with-plaintext");

        var entry = control.Layout.Entries[0];
        Assert.Equal(0x0001, ReadUInt16(mutated.ArchiveBytes, entry.LocalHeaderOffset + 6) & 0x0001);
        Assert.Equal(0x0001, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 8) & 0x0001);
        AssertOnlyRangesChanged(
            control.ArchiveBytes, mutated.ArchiveBytes,
            (entry.LocalHeaderOffset + 6, 2), (entry.CentralDirectoryOffset + 8, 2));

        Assert.Equal(2, mutated.Mutations.Count);
        Assert.Equal("local-header", mutated.Mutations[0].Structure);
        Assert.Equal("central-header", mutated.Mutations[1].Structure);
        Assert.All(mutated.Mutations, mutation =>
            Assert.Equal("general-purpose-bits+=0x0001", mutation.DeclaredValue, StringComparer.Ordinal));
    }

    [Fact]
    public void Apply_OverlappingRanges_RetargetsSecondEntryOffsetIntoFirstPayload()
    {
        var control = BuildControl("valid-stored");
        var mutated = Apply("overlapping-entry-ranges");

        var first = control.Layout.Entries[0];
        var second = control.Layout.Entries[1];
        Assert.Equal((uint)first.DataOffset, ReadUInt32(mutated.ArchiveBytes, second.CentralDirectoryOffset + 42));
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (second.CentralDirectoryOffset + 42, 4));

        // Interval evidence: both claimed ranges recorded; ordinals retained.
        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal(second.Ordinal, mutation.Ordinal);
        Assert.Contains($"entry0-range={first.DataOffset}..", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains($"entry1-claimed={first.DataOffset}..", mutation.DeclaredValue, StringComparison.Ordinal);
    }

    // ---- Capability distinctions (ticket #840 "Tests / done") ----

    [Fact]
    public void Apply_StructureCases_DistinguishFailureModesAcrossCaseKinds()
    {
        // Structural truncation: the Archive cannot even be opened (the EOCD is gone).
        var truncated = Apply("truncate-eocd");
        using (var stream = new MemoryStream(truncated.ArchiveBytes))
        {
            Assert.ThrowsAny<Exception>(() => new ZipArchive(stream, ZipArchiveMode.Read));
        }

        // Unsupported codec: the Archive opens and lists, but reading the entry cannot
        // decode the declared method (98) — a policy failure, not structural damage.
        var unsupported = Apply("unsupported-method");
        using (var archive = new ZipArchive(new MemoryStream(unsupported.ArchiveBytes), ZipArchiveMode.Read))
        {
            Assert.Equal(2, archive.Entries.Count);
            Assert.ThrowsAny<Exception>(() =>
            {
                using var entry = archive.Entries[0].Open();
            });
        }

        // Deflate64 lie (ticket #877): same consistent-code shape, but the .NET
        // reader fails the entry read with a data error, not the
        // unsupported-method rejection method 98 gets — the verified split that
        // justifies the dedicated expectation arm.
        var deflate64 = Apply("unsupported-method-deflate64");
        using (var archive = new ZipArchive(new MemoryStream(deflate64.ArchiveBytes), ZipArchiveMode.Read))
        {
            Assert.Equal(2, archive.Entries.Count);
            var ex = Assert.ThrowsAny<Exception>(() =>
            {
                using var entry = archive.Entries[0].Open();
                using var copy = new MemoryStream();
                entry.CopyTo(copy);
            });
            Assert.DoesNotContain("unsupported compression method", ex.Message, StringComparison.Ordinal);
        }

        // Corrupt local field with intact central directory: the lenient reference reader
        // (central-method based) still reads matching content; only local-trust readers fail.
        var methodMismatch = Apply("method-local-central-mismatch");
        using (var archive = new ZipArchive(new MemoryStream(methodMismatch.ArchiveBytes), ZipArchiveMode.Read))
        {
            Assert.Equal(2, archive.Entries.Count);
            using var entry = archive.GetEntry("a.txt")!.Open();
            using var copy = new MemoryStream();
            entry.CopyTo(copy);
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData(BuildControl("valid-stored").Entries[0].Content)),
                Convert.ToHexStringLower(SHA256.HashData(copy.ToArray())));
        }
    }

    // ---- Catalog wiring and publication ----

    [Fact]
    public void ListSuite_Malformed_IncludesAllStructureCases()
    {
        var malformed = ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.MalformedSuite);

        Assert.Equal(36, malformed.Count);
        Assert.Contains(malformed, c => c.CaseKey == "unsupported-method");
        Assert.Contains(malformed, c => c.CaseKey == "unsupported-method-deflate64");
        Assert.Contains(malformed, c => c.CaseKey == "orphan-local-header");
        Assert.Contains(malformed, c => c.CaseKey == "prefix-unrebased");
        Assert.Contains(malformed, c => c.CaseKey == "deflate-invalid-btype");
        Assert.Contains(malformed, c => c.CaseKey == "deflate-corrupt-huffman");
        Assert.Equal("policy-sensitive", ArchiveTestCatalog.GetCase("unsupported-method").Classification);
        Assert.Equal("policy-sensitive", ArchiveTestCatalog.GetCase("unsupported-method-deflate64").Classification);
        Assert.Equal("malformed", ArchiveTestCatalog.GetCase("orphan-local-header").Classification);
        Assert.Equal("malformed", ArchiveTestCatalog.GetCase("prefix-unrebased").Classification);

        var keys = malformed.Select(c => c.CaseKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("name-local-central-mismatch")]
    [InlineData("method-local-central-mismatch")]
    [InlineData("size-local-central-mismatch")]
    [InlineData("offset-outside-archive")]
    [InlineData("offset-into-payload")]
    [InlineData("extra-field-length-overrun")]
    [InlineData("unsupported-method")]
    [InlineData("unsupported-method-deflate64")]
    [InlineData("encryption-flag-with-plaintext")]
    [InlineData("overlapping-entry-ranges")]
    [InlineData("orphan-local-header")]
    [InlineData("prefix-unrebased")]
    [InlineData("deflate-invalid-btype")]
    [InlineData("deflate-corrupt-huffman")]
    [InlineData("method-cross-deflate64-deflate")]
    [InlineData("method-cross-bzip2-stored")]
    [InlineData("method-data-deflate-as-bzip2")]
    [InlineData("method-data-bzip2-as-stored")]
    [InlineData("bzip2-corrupt-block-magic")]
    [InlineData("bzip2-truncated-stream")]
    [InlineData("bzip2-wrong-crc")]
    [InlineData("deflate64-corrupt-stream")]
    [InlineData("deflate64-truncated-stream")]
    public void Build_StructureCases_AreDeterministicPerCaseAndSeed(string caseKey)
    {
        var definition = ArchiveTestCatalog.GetCase(caseKey);

        var first = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);
        var second = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);
        Assert.Equal(first.ArchiveSha256, second.ArchiveSha256);
        Assert.Equal(first.Mutations, second.Mutations);
        Assert.NotEmpty(first.Mutations);
    }

    [Fact]
    public void Apply_OrphanLocalHeader_InsertsHiddenEntryBeforeVisible()
    {
        var control = BuildControl("valid-deflate");
        var definition = ArchiveTestCatalog.GetCase("orphan-local-header");
        Assert.True(definition.IsMutation, "case 'orphan-local-header' must be a mutation case");
        var mutated = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        // Hidden LFH at offset 0: signature, stored method, name hidden.sh.
        // Lengths come from the header itself, never a duplicated constant.
        Assert.Equal(0x04034b50u, ReadUInt32(mutated.ArchiveBytes, 0));
        Assert.Equal(0, ReadUInt16(mutated.ArchiveBytes, 8));
        var hiddenNameLength = ReadUInt16(mutated.ArchiveBytes, 26);
        Assert.Equal("hidden.sh", Encoding.UTF8.GetString(mutated.ArchiveBytes.AsSpan(30, hiddenNameLength)));
        var hiddenPayloadLength = ReadUInt32(mutated.ArchiveBytes, 18);
        Assert.Equal(ReadUInt32(mutated.ArchiveBytes, 22), hiddenPayloadLength);
        var hiddenLength = 30 + hiddenNameLength + (int)hiddenPayloadLength;
        Assert.Equal(control.ArchiveBytes.Length + hiddenLength, mutated.ArchiveBytes.Length);

        // Central directory still indexes only the visible entry; .NET lists one.
        using var archive = new ZipArchive(new MemoryStream(mutated.ArchiveBytes), ZipArchiveMode.Read);
        var sole = Assert.Single(archive.Entries);
        Assert.Equal("doc.txt", sole.FullName);

        // Linked shifts: visible LFH and central directory move by the hidden length.
        var visibleLocalOffset = ReadUInt32(mutated.ArchiveBytes, control.Layout.Entries[0].CentralDirectoryOffset + hiddenLength + 42);
        Assert.Equal((uint)hiddenLength, visibleLocalOffset);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("orphan-local-header", mutation.Code);
        Assert.Equal(0, mutation.Offset);
        Assert.Equal(0, mutation.DeletedLength);
        Assert.Equal(hiddenLength, mutation.InsertedLength);
        Assert.Contains("hidden-lfh-offset=0", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains("hidden-name-hex=68696464656e2e7368", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains(
            "hidden-payload-sha256=" + Convert.ToHexStringLower(SHA256.HashData("atc-hidden-payload"u8.ToArray())),
            mutation.DeclaredValue,
            StringComparison.Ordinal);
        Assert.Contains(
            $"eocd-central-directory-offset={control.Layout.CentralDirectoryOffset + hiddenLength}",
            mutation.DeclaredValue,
            StringComparison.Ordinal);
        Assert.Contains("eocd-entry-counts=1", mutation.DeclaredValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PrefixedArchive_RebasesOffsetsAndReads()
    {
        var plain = BuildControl("valid-deflate");
        var artifact = ArchiveFixtureBuilder.BuildControl("prefix-rebased", 42, CancellationToken.None);

        Assert.Equal("valid", ArchiveTestCatalog.GetCase("prefix-rebased").Classification);
        Assert.Contains(ArchiveTestCatalog.CompatibilitySuite, ArchiveTestCatalog.GetCase("prefix-rebased").Suites);
        Assert.Equal(plain.ArchiveBytes.Length + 64, artifact.ArchiveBytes.Length);
        Assert.Equal(64, artifact.Layout.Entries[0].LocalHeaderOffset);
        Assert.Equal(plain.Layout.CentralDirectoryOffset + 64, artifact.Layout.CentralDirectoryOffset);
        Assert.Equal(plain.Layout.CentralDirectorySize, artifact.Layout.CentralDirectorySize);

        // The stub is inert data: none of the structural signatures appear in it.
        var stub = artifact.ArchiveBytes.AsSpan(0, 64);
        Assert.Equal(-1, stub.IndexOf((ReadOnlySpan<byte>)[0x50, 0x4b, 0x03, 0x04]));
        Assert.Equal(-1, stub.IndexOf((ReadOnlySpan<byte>)[0x50, 0x4b, 0x01, 0x02]));
        Assert.Equal(-1, stub.IndexOf((ReadOnlySpan<byte>)[0x50, 0x4b, 0x05, 0x06]));
        Assert.Equal(-1, stub.IndexOf((ReadOnlySpan<byte>)[0x50, 0x4b, 0x07, 0x08]));

        using var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read);
        var sole = Assert.Single(archive.Entries);
        Assert.Equal("doc.txt", sole.FullName);
    }

    [Fact]
    public void Apply_PrefixUnrebased_LeavesStaleOffsets()
    {
        var control = BuildControl("valid-deflate");
        var definition = ArchiveTestCatalog.GetCase("prefix-unrebased");
        Assert.True(definition.IsMutation, "case 'prefix-unrebased' must be a mutation case");
        Assert.Equal("malformed", definition.Classification);
        var mutated = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("prefix-unrebased", mutation.Code);
        Assert.Equal(0, mutation.Offset);
        Assert.Equal(64, mutation.InsertedLength);
        Assert.Equal(control.ArchiveBytes.Length, mutation.BeforeSize);
        Assert.Equal(control.ArchiveBytes.Length + 64, mutation.AfterSize);
        Assert.Contains("prefix-length=64 rebased=false", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains($"eocd-central-directory-offset={control.Layout.CentralDirectoryOffset}", mutation.DeclaredValue, StringComparison.Ordinal);

        // Stale coordinates: the EOCD field still holds the pre-prefix central
        // offset and the central entry still declares local header 0 (now the
        // stub); the true central directory sits 64 bytes later.
        var eocdField = ReadUInt32(mutated.ArchiveBytes, mutated.ArchiveBytes.Length - 22 + 16);
        Assert.Equal((uint)control.Layout.CentralDirectoryOffset, eocdField);
        Assert.NotEqual(
            [0x50, 0x4b, 0x01, 0x02],
            mutated.ArchiveBytes.AsSpan((int)eocdField, 4).ToArray());
        Assert.Equal(
            [0x50, 0x4b, 0x01, 0x02],
            mutated.ArchiveBytes.AsSpan((int)eocdField + 64, 4).ToArray());
        Assert.Equal(0u, ReadUInt32(mutated.ArchiveBytes, (int)eocdField + 64 + 42));

        // Desynchronized: the central directory still starts at its pre-prefix file
        // position, which now holds shifted payload bytes instead of central
        // headers, so even listing fails on strict readers.
        using var archive = new ZipArchive(new MemoryStream(mutated.ArchiveBytes), ZipArchiveMode.Read);
        Assert.ThrowsAny<Exception>(() => archive.Entries.Count);
    }

    [Fact]
    public void Build_OverlappingBomb_AllCentralHeadersShareOneLocalHeader()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("zip-bomb-overlapping-deflate", 42, CancellationToken.None);

        Assert.Equal(64, artifact.Layout.EntryCount);
        Assert.All(artifact.Layout.Entries, entry => Assert.Equal(0, entry.LocalHeaderOffset));
        Assert.All(artifact.Layout.Entries, entry => Assert.Equal((ushort)8, entry.Method));

        // Amplification: tiny physical archive, 64 x 64 KiB declared expansion.
        Assert.Equal(64 * 65536L, artifact.Entries.Sum(e => (long)e.Content.Length));
        Assert.True(artifact.ArchiveBytes.Length < 16 * 1024 * 1024, "physical Archive exceeds the 16 MiB budget.");
        Assert.Equal(
            artifact.Layout.CentralDirectoryOffset + artifact.Layout.CentralDirectorySize,
            artifact.Layout.EocdOffset);

        using var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read);
        Assert.Equal(64, archive.Entries.Count);
    }

    [Fact]
    public void Build_ValidDeflateDynamic_UsesDynamicHuffmanFirstBlock()
    {
        var control = BuildControl("valid-deflate-dynamic");

        var entry = Assert.Single(control.Layout.Entries);
        Assert.Equal((ushort)8, entry.Method);
        Assert.Equal(0b10, (control.ArchiveBytes[(int)entry.DataOffset] >> 1) & 0x03);
        Assert.Equal(control.Entries[0].Content, ReadEntryContent(control.ArchiveBytes, "dyn.txt"));
    }

    [Fact]
    public void Apply_DeflateInvalidBtype_SetsReservedBlockType()
    {
        var control = BuildControl("valid-deflate");
        var definition = ArchiveTestCatalog.GetCase("deflate-invalid-btype");
        var mutated = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        var entry = control.Layout.Entries[0];
        Assert.NotEqual(0b11, (control.ArchiveBytes[(int)entry.DataOffset] >> 1) & 0x03);
        Assert.Equal(0b11, (mutated.ArchiveBytes[(int)entry.DataOffset] >> 1) & 0x03);
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (entry.DataOffset, 1));

        using var archive = new ZipArchive(new MemoryStream(mutated.ArchiveBytes), ZipArchiveMode.Read);
        Assert.Single(archive.Entries);
        Assert.ThrowsAny<Exception>(() =>
        {
            using var stream = archive.Entries[0].Open();
            stream.CopyTo(Stream.Null);
        });

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("deflate-invalid-btype", mutation.Code);
        Assert.Equal(entry.DataOffset, mutation.Offset);
        Assert.Contains("btype=3", mutation.DeclaredValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_DeflateCorruptHuffman_FlipsCodeLengthBitAndFailsDecode()
    {
        var control = BuildControl("valid-deflate-dynamic");
        var definition = ArchiveTestCatalog.GetCase("deflate-corrupt-huffman");
        var mutated = ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None);

        // The documented dynamic header survives except the flipped order[0] bit.
        var entry = control.Layout.Entries[0];
        Assert.Equal(0b10, (mutated.ArchiveBytes[(int)entry.DataOffset] >> 1) & 0x03);
        Assert.Equal(
            0x02,
            control.ArchiveBytes[(int)entry.DataOffset + 2] ^ mutated.ArchiveBytes[(int)entry.DataOffset + 2]);
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (entry.DataOffset + 2, 1));

        using var archive = new ZipArchive(new MemoryStream(mutated.ArchiveBytes), ZipArchiveMode.Read);
        Assert.Single(archive.Entries);
        Assert.ThrowsAny<Exception>(() =>
        {
            using var stream = archive.Entries[0].Open();
            stream.CopyTo(Stream.Null);
        });

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal(entry.DataOffset + 2, mutation.Offset);
        Assert.Contains("dynamic-hlit=", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains("dynamic-hdist=", mutation.DeclaredValue, StringComparison.Ordinal);
        Assert.Contains("dynamic-hclen=", mutation.DeclaredValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_UnsupportedMethodDeflate64_SetsConsistentCodeNineInBothHeaders()
    {
        var control = BuildControl("valid-stored");
        var mutated = Apply("unsupported-method-deflate64");

        var entry = control.Layout.Entries[0];
        Assert.Equal(UnsupportedMethodDeflate64Code, ReadUInt16(mutated.ArchiveBytes, entry.LocalHeaderOffset + 8));
        Assert.Equal(UnsupportedMethodDeflate64Code, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 10));
        AssertOnlyRangesChanged(
            control.ArchiveBytes, mutated.ArchiveBytes,
            (entry.LocalHeaderOffset + 8, 2), (entry.CentralDirectoryOffset + 10, 2));

        // Two parallel field records (local + central), same transition.
        Assert.Equal(2, mutated.Mutations.Count);
        Assert.Equal("local-header", mutated.Mutations[0].Structure);
        Assert.Equal("central-header", mutated.Mutations[1].Structure);
        Assert.All(mutated.Mutations, mutation =>
            Assert.Equal("method=9", mutation.DeclaredValue, StringComparer.Ordinal));

        // The payload stays the stored control bytes: an unsupported codec, not corrupt data.
        Assert.Equal(control.Entries[0].Content[..16], mutated.ArchiveBytes.AsSpan((int)entry.DataOffset, 16).ToArray());
    }

    [Fact]
    public void Apply_Bzip2CorruptBlockMagic_RejectsAtDecode()
    {
        var control = BuildControl("valid-bzip2");
        var mutated = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("bzip2-corrupt-block-magic"), 42, CancellationToken.None);

        var entry = control.Layout.Entries[0];
        Assert.NotEqual(
            control.ArchiveBytes[(int)entry.DataOffset + 4],
            mutated.ArchiveBytes[(int)entry.DataOffset + 4]);
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (entry.DataOffset + 4, 1));

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("bzip2-corrupt-block-magic", mutation.Code);
        Assert.Contains("block-magic", mutation.DeclaredValue, StringComparison.Ordinal);

        // A bzip2 decoder rejects the stream at block start.
        Assert.ThrowsAny<Exception>(() => DecodeBzip2(mutated.ArchiveBytes, mutated.Layout.Entries[0]));
    }

    [Fact]
    public void Apply_Bzip2WrongCrc_FlipsStoredCrcByte()
    {
        var control = BuildControl("valid-bzip2");
        var mutated = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("bzip2-wrong-crc"), 42, CancellationToken.None);

        var entry = control.Layout.Entries[0];
        var crcOffset = entry.DataOffset + 10;
        Assert.NotEqual(control.ArchiveBytes[(int)crcOffset], mutated.ArchiveBytes[(int)crcOffset]);
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (crcOffset, 1));

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("bzip2-wrong-crc", mutation.Code);

        // An integrity-checking decoder rejects the finished stream.
        Assert.ThrowsAny<Exception>(() => DecodeBzip2(mutated.ArchiveBytes, mutated.Layout.Entries[0]));
    }

    private static byte[] DecodeBzip2(byte[] archiveBytes, ArchiveFixtureEntryLayout entry)
    {
        using var source = new MemoryStream(archiveBytes, (int)entry.DataOffset, (int)entry.CompressedSize, writable: false);
        using var bz2 = new ICSharpCode.SharpZipLib.BZip2.BZip2InputStream(source);
        using var inflated = new MemoryStream();
        bz2.CopyTo(inflated);
        return inflated.ToArray();
    }

    [Fact]
    public void Apply_Bzip2TruncatedStream_DeletesTailAndRelinksDirectory()
    {
        var control = BuildControl("valid-bzip2");
        var mutated = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("bzip2-truncated-stream"), 42, CancellationToken.None);

        Assert.Equal(control.ArchiveBytes.Length - 16, mutated.ArchiveBytes.Length);

        // The artifact keeps before-mutation coordinates, so the relink is pinned
        // from the final bytes: the EOCD sits 16 bytes earlier, its directory
        // offset field is rebased by -16 onto a parseable central header, and the
        // central size is unchanged while the entry sizes keep pre-truncation
        // values (the lies under test).
        var controlEntry = control.Layout.Entries[0];
        var eocdAt = mutated.ArchiveBytes.Length - 22;
        Assert.Equal(0x06054b50u, ReadUInt32(mutated.ArchiveBytes, eocdAt));
        Assert.Equal((uint)(control.Layout.CentralDirectoryOffset - 16), ReadUInt32(mutated.ArchiveBytes, eocdAt + 16));
        Assert.Equal(control.Layout.CentralDirectorySize, ReadUInt32(mutated.ArchiveBytes, eocdAt + 12));
        var cdAt = control.Layout.CentralDirectoryOffset - 16;
        Assert.Equal(0x02014b50u, ReadUInt32(mutated.ArchiveBytes, cdAt));
        Assert.Equal(controlEntry.CompressedSize, ReadUInt32(mutated.ArchiveBytes, controlEntry.LocalHeaderOffset + 18));

        // The truncated stream no longer decodes.
        var truncated = controlEntry with { CompressedSize = controlEntry.CompressedSize - 16 };
        Assert.ThrowsAny<Exception>(() => DecodeBzip2(mutated.ArchiveBytes, truncated));

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal(16, mutation.DeletedLength);
        Assert.Equal(0, mutation.InsertedLength);
    }

    [Fact]
    public void Apply_Deflate64CorruptStream_SetsReservedBlockType()
    {
        var control = BuildControl("valid-deflate64");
        var mutated = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("deflate64-corrupt-stream"), 42, CancellationToken.None);

        var entry = control.Layout.Entries[0];
        Assert.Equal(0b11, (mutated.ArchiveBytes[(int)entry.DataOffset] >> 1) & 0x03);
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (entry.DataOffset, 1));

        // No decoder dispatches the reserved block type.
        using var archive = new ZipArchive(new MemoryStream(mutated.ArchiveBytes), ZipArchiveMode.Read);
        Assert.ThrowsAny<Exception>(() =>
        {
            using var stream = archive.Entries[0].Open();
            stream.CopyTo(Stream.Null);
        });

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("deflate64-corrupt-stream", mutation.Code);
    }

    [Fact]
    public void Apply_MethodCrossDeflate64Deflate_SplitsLocalFromCentral()
    {
        var control = BuildControl("valid-deflate");
        var mutated = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("method-cross-deflate64-deflate"), 42, CancellationToken.None);

        var entry = control.Layout.Entries[0];
        Assert.Equal(9, ReadUInt16(mutated.ArchiveBytes, entry.LocalHeaderOffset + 8));
        Assert.Equal(8, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 10));
        AssertOnlyRangesChanged(control.ArchiveBytes, mutated.ArchiveBytes, (entry.LocalHeaderOffset + 8, 2));

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("local-method=9 central-method=8", mutation.DeclaredValue);
    }

    [Fact]
    public void Apply_MethodCrossBzip2Stored_SplitsLocalFromCentral()
    {
        var control = BuildControl("valid-stored");
        var mutated = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("method-cross-bzip2-stored"), 42, CancellationToken.None);

        var entry = control.Layout.Entries[0];
        Assert.Equal(12, ReadUInt16(mutated.ArchiveBytes, entry.LocalHeaderOffset + 8));
        Assert.Equal(0, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 10));

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("local-method=12 central-method=0", mutation.DeclaredValue);
    }

    [Fact]
    public void Apply_MethodDataDeflateAsBzip2_RelabelsBothHeadersToDeflate()
    {
        var control = BuildControl("valid-bzip2");
        var mutated = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("method-data-deflate-as-bzip2"), 42, CancellationToken.None);

        var entry = control.Layout.Entries[0];
        Assert.Equal(8, ReadUInt16(mutated.ArchiveBytes, entry.LocalHeaderOffset + 8));
        Assert.Equal(8, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 10));

        using var archive = new ZipArchive(new MemoryStream(mutated.ArchiveBytes), ZipArchiveMode.Read);
        Assert.ThrowsAny<Exception>(() =>
        {
            using var stream = archive.Entries[0].Open();
            stream.CopyTo(Stream.Null);
        });

        Assert.Equal(2, mutated.Mutations.Count);
        var dataMutation = Assert.Single(mutated.Mutations, m => m.Structure == "local-header");
        Assert.Equal("method-data-deflate-as-bzip2", dataMutation.Code);
    }

    [Fact]
    public void Apply_Deflate64TruncatedStream_DeletesTailAndRelinksDirectory()
    {
        var control = BuildControl("valid-deflate64");
        var mutated = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("deflate64-truncated-stream"), 42, CancellationToken.None);

        Assert.Equal(control.ArchiveBytes.Length - 16, mutated.ArchiveBytes.Length);

        var controlEntry = control.Layout.Entries[0];
        var eocdAt = mutated.ArchiveBytes.Length - 22;
        Assert.Equal(0x06054b50u, ReadUInt32(mutated.ArchiveBytes, eocdAt));
        Assert.Equal((uint)(control.Layout.CentralDirectoryOffset - 16), ReadUInt32(mutated.ArchiveBytes, eocdAt + 16));
        Assert.Equal(controlEntry.CompressedSize, ReadUInt32(mutated.ArchiveBytes, controlEntry.LocalHeaderOffset + 18));

        // The truncated stream never yields the true content: strict readers throw
        // while lenient ones (.NET) stream short without error — either way the
        // payload contract is broken.
        using var archive = new ZipArchive(new MemoryStream(mutated.ArchiveBytes), ZipArchiveMode.Read);
        try
        {
            using var stream = archive.Entries[0].Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            Assert.NotEqual(control.Entries[0].Content, copy.ToArray());
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
        }

        var mutation = Assert.Single(mutated.Mutations);
        Assert.Equal("deflate64-truncated-stream", mutation.Code);
    }

    [Fact]
    public void Build_Bzip2HighRatioBounded_DeclaresOneMebibyteHonestly()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("bzip2-high-ratio-bounded", 42, CancellationToken.None);

        var entry = Assert.Single(artifact.Layout.Entries);
        Assert.Equal((ushort)12, entry.Method);
        Assert.Equal(1024u * 1024u, entry.UncompressedSize);
        Assert.Equal(artifact.Entries[0].Content, ReadEntryContentRaw(artifact.ArchiveBytes, entry));
    }

    private static byte[] ReadEntryContentRaw(byte[] archiveBytes, ArchiveFixtureEntryLayout entry)
    {
        using var source = new MemoryStream(archiveBytes, (int)entry.DataOffset, (int)entry.CompressedSize, writable: false);
        using var bz2 = new ICSharpCode.SharpZipLib.BZip2.BZip2InputStream(source);
        using var inflated = new MemoryStream();
        bz2.CopyTo(inflated);
        return inflated.ToArray();
    }

    [Fact]
    public void Apply_MethodDataBzip2AsStored_RelabelsBothHeadersToBzip2()
    {
        var control = BuildControl("valid-stored");
        var mutated = ArchiveFixtureBuilder.Build(ArchiveTestCatalog.GetCase("method-data-bzip2-as-stored"), 42, CancellationToken.None);

        var entry = control.Layout.Entries[0];
        Assert.Equal(12, ReadUInt16(mutated.ArchiveBytes, entry.LocalHeaderOffset + 8));
        Assert.Equal(12, ReadUInt16(mutated.ArchiveBytes, entry.CentralDirectoryOffset + 10));

        Assert.Equal(2, mutated.Mutations.Count);
        var mutation = Assert.Single(mutated.Mutations, m => m.Structure == "local-header");
        Assert.Equal("method-data-bzip2-as-stored", mutation.Code);
        Assert.Contains("method=12", mutation.DeclaredValue, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ValidMixedMethods_KeepsPerEntryCodecs()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("valid-mixed-methods", 42, CancellationToken.None);

        Assert.Equal(3, artifact.Layout.EntryCount);
        Assert.Equal([(ushort)0, (ushort)8, (ushort)12], artifact.Layout.Entries.Select(e => e.Method).ToList());
    }

    [Fact]
    public void Build_DirectorySlashWithPayload_KeepsSlashNameAndContent()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("directory-slash-with-payload", 42, CancellationToken.None);

        var entry = Assert.Single(artifact.Layout.Entries);
        Assert.Equal("testdir/", entry.Name);
        Assert.Equal((uint)"atc-dir-slash-payload".Length, entry.UncompressedSize);
        Assert.False(artifact.Entries[0].IsDirectory);

        // Central external attributes are deliberately not pinned: the standard
        // writer synthesizes host-OS directory mode bits for trailing-slash names
        // (Unix 0x41ED0000 on Linux), so the exact value varies by build host.
        Assert.Equal(
            "atc-dir-slash-payload",
            Encoding.ASCII.GetString(ReadEntryContent(artifact.ArchiveBytes, "testdir/")));
    }

    [Fact]
    public void Build_DirectoryAttributeWithPayload_SetsDosDirectoryAttribute()
    {
        var artifact = ArchiveFixtureBuilder.BuildControl("directory-attribute-with-payload", 42, CancellationToken.None);

        var entry = Assert.Single(artifact.Layout.Entries);
        Assert.Equal("testfile", entry.Name);
        Assert.Equal((uint)"atc-dir-attr-payload".Length, entry.UncompressedSize);

        // Raw value 0x10 in the central external attributes (central + 38); the local
        // header carries no such field. The version-made-by host byte is deliberately
        // not pinned: it is the writer's OS default (Unix on Linux), so whether an
        // extractor reads 0x10 as a DOS directory attribute varies by build host.
        Assert.Equal(0x10u, ReadUInt32(artifact.ArchiveBytes, entry.CentralDirectoryOffset + 38));
        Assert.Equal(
            "atc-dir-attr-payload",
            Encoding.ASCII.GetString(ReadEntryContent(artifact.ArchiveBytes, "testfile")));
    }

    [Fact]
    public async Task GenerateAsync_AllSuites_PublishesUniqueValidatedPairsForEachCase()
    {
        var all = ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.AllSuites);
        Assert.Equal(79, all.Count);

        var result = await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create(all.Select(c => c.CaseKey).ToList(), 42, Path.Combine(TempDir, "all")),
            CancellationToken.None);

        Assert.Equal(all.Count, result.FixtureIds.Count);
        Assert.Equal(all.Count * 2, Directory.GetFiles(result.PublishedDirectory).Length);
        Assert.Equal(result.FixtureIds.Count, result.FixtureIds.Distinct(StringComparer.Ordinal).Count());

        var definitionsByKey = all.ToDictionary(c => c.CaseKey, c => c);
        var seenCaseKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var jsonPath in Directory.GetFiles(result.PublishedDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            var testCase = ArchiveTestJson.Parse(await File.ReadAllBytesAsync(jsonPath));
            var definition = definitionsByKey[testCase.CaseKey];

            // Exactly one pair per case: no case accidentally selected twice.
            Assert.True(seenCaseKeys.Add(testCase.CaseKey));
            Assert.Equal(Path.GetFileNameWithoutExtension(jsonPath), testCase.FixtureId);

            // Verifiable final hash: the on-disk Archive bytes match the sidecar, and the
            // Fixture ID reproduces from the descriptor including that final hash.
            var zipBytes = await File.ReadAllBytesAsync(Path.Combine(result.PublishedDirectory, testCase.FixtureId + ".zip"));
            Assert.Equal(testCase.Archive.Sha256, Convert.ToHexStringLower(SHA256.HashData(zipBytes)));
            Assert.Equal(
                ArchiveTestIdentity.ComputeFixtureId(
                    "1", definition.CaseKey, definition.CaseRevision, definition.ExpectationRevision,
                    42, testCase.Archive.Sha256),
                testCase.FixtureId);

            Assert.Equal(definition.Classification, testCase.Classification);
            Assert.Empty(ArchiveTestCaseSemantics.Validate(testCase));

            // Sidecars retain entry ordinals even with conflicting names or offsets.
            Assert.Equal(testCase.Entries.Count, testCase.Entries.Select(e => e.Ordinal).Distinct().Count());
            if (definition.IsMutation)
            {
                Assert.NotEmpty(testCase.Mutations);
                Assert.All(testCase.Mutations, mutation => Assert.Equal("before-mutation", mutation.OffsetBasis));

                // The ticket #840 structure cases store their malformed declared values
                // separately from the physical facts; the #839 truncations have no declared
                // value (the defect is the deletion itself).
                if (definition.Mutations is [var single]
                    && single is ArchiveTestMutationKind.NameLocalCentralMismatch
                        or ArchiveTestMutationKind.MethodLocalCentralMismatch
                        or ArchiveTestMutationKind.SizeLocalCentralMismatch
                        or ArchiveTestMutationKind.OffsetOutsideArchive
                        or ArchiveTestMutationKind.OffsetIntoPayload
                        or ArchiveTestMutationKind.ExtraFieldLengthOverrun
                        or ArchiveTestMutationKind.UnsupportedMethod
                        or ArchiveTestMutationKind.UnsupportedMethodDeflate64
                        or ArchiveTestMutationKind.EncryptionFlagWithPlaintext
                        or ArchiveTestMutationKind.OverlappingEntryRanges
                        or ArchiveTestMutationKind.OrphanLocalHeader
                        or ArchiveTestMutationKind.PrefixUnrebased
                        or ArchiveTestMutationKind.DeflateInvalidBtype
                        or ArchiveTestMutationKind.DeflateCorruptHuffman
                        or ArchiveTestMutationKind.Bzip2CorruptBlockMagic
                        or ArchiveTestMutationKind.Bzip2TruncatedStream
                        or ArchiveTestMutationKind.Bzip2WrongCrc
                        or ArchiveTestMutationKind.Deflate64CorruptStream
                        or ArchiveTestMutationKind.Deflate64TruncatedStream)
                {
                    Assert.All(testCase.Mutations, mutation => Assert.NotNull(mutation.DeclaredValue));
                }
            }
        }

        Assert.Equal(all.Count, seenCaseKeys.Count);
    }
}
