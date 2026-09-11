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

        Assert.Equal(22, malformed.Count);
        Assert.Contains(malformed, c => c.CaseKey == "unsupported-method");
        Assert.Equal("policy-sensitive", ArchiveTestCatalog.GetCase("unsupported-method").Classification);

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
    [InlineData("encryption-flag-with-plaintext")]
    [InlineData("overlapping-entry-ranges")]
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
    public async Task GenerateAsync_AllSuites_PublishesUniqueValidatedPairsForEachCase()
    {
        var all = ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.AllSuites);
        Assert.Equal(49, all.Count);

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
                        or ArchiveTestMutationKind.EncryptionFlagWithPlaintext
                        or ArchiveTestMutationKind.OverlappingEntryRanges)
                {
                    Assert.All(testCase.Mutations, mutation => Assert.NotNull(mutation.DeclaredValue));
                }
            }
        }

        Assert.Equal(all.Count, seenCaseKeys.Count);
    }
}
