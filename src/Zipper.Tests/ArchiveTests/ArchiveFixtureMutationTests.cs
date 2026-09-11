using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class ArchiveFixtureMutationTests : TempDirectoryTestBase
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
