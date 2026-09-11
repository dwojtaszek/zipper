using System.Buffers.Binary;
using System.Text;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Ticket #843 combined-mutation cases: the audited field-level mutations composed in
/// a finite, explicit chain. Each mutation consumes the previous result; the mutation
/// log is ordered with every offset on its own before-mutation basis, and the
/// before/after hash chain proves the composition.
/// </summary>
public class ArchiveCombinedCaseTests : TempDirectoryTestBase
{
    private static ArchiveFixtureArtifact BuildControl(string caseKey) =>
        ArchiveFixtureBuilder.BuildControl(caseKey, 42, CancellationToken.None);

    private static void AssertHashChainConsumesEachResult(ArchiveFixtureArtifact artifact)
    {
        var records = artifact.Mutations;
        Assert.True(records.Count >= 2);
        Assert.Equal(artifact.ArchiveSha256, records[^1].AfterSha256);
        for (var index = 1; index < records.Count; index++)
        {
            // Each mutation's before state is the previous mutation's after state.
            Assert.Equal(records[index - 1].AfterSha256, records[index].BeforeSha256);
            Assert.Equal(records[index - 1].AfterSize, records[index].BeforeSize);
        }
    }

    [Fact]
    public void Build_CombinedCrcAndName_AppliesBothFieldMutationsInOrder()
    {
        var control = BuildControl("valid-stored");
        var artifact = BuildControl("combined-crc-and-name");

        // Ordered log: CRC lie first, name lie second; both effects present in the
        // final bytes, and no length changed.
        Assert.Equal(["crc-local-mismatch", "name-local-central-mismatch"], artifact.Mutations.Select(m => m.Code));
        Assert.Equal(control.ArchiveBytes.Length, artifact.ArchiveBytes.Length);

        var entry = artifact.Layout.Entries[0];
        var controlEntry = control.Layout.Entries[0];
        var finalCrc = BinaryPrimitives.ReadUInt32LittleEndian(
            artifact.ArchiveBytes.AsSpan((int)entry.LocalHeaderOffset + 14, 4));
        Assert.NotEqual(controlEntry.Crc32, finalCrc);

        var localName = Encoding.UTF8.GetString(artifact.ArchiveBytes.AsSpan(
            (int)entry.LocalHeaderOffset + 30, entry.LocalNameLength));
        var centralName = Encoding.UTF8.GetString(artifact.ArchiveBytes.AsSpan(
            (int)entry.CentralDirectoryOffset + 46, entry.CentralNameLength));
        Assert.Equal("a.txt", centralName);
        Assert.NotEqual(centralName, localName);
    }

    [Fact]
    public void Build_CombinedCrcAndName_HashChainConsumesEachResult()
    {
        AssertHashChainConsumesEachResult(BuildControl("combined-crc-and-name"));
    }

    [Fact]
    public void Build_CombinedCrcAndTruncate_LogIsOrderedWithPerStepCoordinates()
    {
        var control = BuildControl("valid-stored");
        var artifact = BuildControl("combined-crc-and-truncate");
        var records = artifact.Mutations;

        Assert.Equal(["crc-local-mismatch", "truncate-payload-tail"], records.Select(m => m.Code));

        // Step 1 (CRC flip) changes no length: before == after == control size.
        Assert.Equal(control.ArchiveBytes.Length, records[0].BeforeSize);
        Assert.Equal(records[0].BeforeSize, records[0].AfterSize);

        // Step 2 (truncation) consumes step 1's result: its offset and before size are
        // on the post-CRC basis, and its after state is the final truncated Archive.
        Assert.Equal(records[0].AfterSize, records[1].BeforeSize);
        Assert.Equal(
            control.Layout.Entries[^1].DataOffset + 10,
            records[1].Offset);
        Assert.True(records[1].AfterSize < records[1].BeforeSize);
        Assert.Equal(records[1].AfterSize, (long)artifact.ArchiveBytes.Length);
        Assert.Equal(records[1].DeletedLength, records[1].BeforeSize - records[1].Offset);
    }

    [Fact]
    public void Build_CombinedCrcAndTruncate_HashChainConsumesEachResult()
    {
        AssertHashChainConsumesEachResult(BuildControl("combined-crc-and-truncate"));
    }

    [Theory]
    [InlineData("combined-crc-and-name")]
    [InlineData("combined-crc-and-truncate")]
    public void Build_CombinedCases_AreDeterministic(string caseKey)
    {
        var first = BuildControl(caseKey);
        var second = BuildControl(caseKey);

        Assert.Equal(first.ArchiveBytes, second.ArchiveBytes);
        Assert.Equal(first.ArchiveSha256, second.ArchiveSha256);
        Assert.Equal(first.Mutations, second.Mutations);
    }

    [Theory]
    [InlineData("combined-crc-and-name")]
    [InlineData("combined-crc-and-truncate")]
    public void Replay_MutationChain_ReproducesExactlyTheFinalBytes(string caseKey)
    {
        var definition = ArchiveTestCatalog.GetCase(caseKey);
        var control = BuildControl(definition.ControlCaseKey!);
        var artifact = BuildControl(caseKey);

        // Replay mirrors the builder's chain path exactly: apply each recorded kind to
        // the previous result, recomputing the layout between mutations so no stale
        // offsets are reused.
        var current = control;
        for (var index = 0; index < definition.Mutations!.Count; index++)
        {
            var mutated = ArchiveFixtureMutator.Apply(definition.Mutations[index], current);
            current = current with
            {
                ArchiveBytes = mutated.ArchiveBytes,
                ArchiveSha256 = mutated.ArchiveSha256,
                Mutations = [],
            };

            if (index + 1 < definition.Mutations.Count)
            {
                current = current with { Layout = ArchiveFixtureLayout.Read(current.ArchiveBytes) };
            }
        }

        Assert.Equal(artifact.ArchiveBytes, current.ArchiveBytes);
        Assert.Equal(artifact.ArchiveSha256, current.ArchiveSha256);
    }

    [Fact]
    public void Build_ChainAfterTruncation_FailsDescriptivelyOnUnreadableIntermediate()
    {
        // An invalid recipe sequence: the truncation removes the EOCD, so a later
        // mutation cannot be given fresh field coordinates.
        var recipe = new ArchiveTestRecipe(
        [
            ArchiveTestRecipeEntry.File("a.txt", 100, "stored"),
            ArchiveTestRecipeEntry.File("b.bin", 40, "stored"),
        ]);
        var definition = new ArchiveTestCaseDefinition(
            "synthetic-invalid-chain", 1, 1, "malformed", [ArchiveTestCatalog.MalformedSuite], recipe,
            ControlCaseKey: "valid-stored",
            Mutations: [ArchiveTestMutationKind.TruncatePayloadTail, ArchiveTestMutationKind.CrcLocalMismatch]);

        var ex = Assert.Throws<InvalidOperationException>(
            () => ArchiveFixtureBuilder.Build(definition, 42, CancellationToken.None));
        Assert.Contains("invalid mutation chain", ex.Message, StringComparison.Ordinal);
        Assert.Contains("truncate-payload-tail", ex.Message, StringComparison.Ordinal);
        Assert.Contains("crc-local-mismatch", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_CombinedCases_PublishOrderedMutationLogs()
    {
        var caseKeys = new[] { "combined-crc-and-name", "combined-crc-and-truncate" };
        var result = await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create(caseKeys, 42, Path.Combine(TempDir, "combined")),
            CancellationToken.None);

        Assert.Equal(caseKeys.Length, result.FixtureIds.Count);
        foreach (var jsonPath in Directory.GetFiles(result.PublishedDirectory, "*.json"))
        {
            var testCase = ArchiveTestJson.Parse(await File.ReadAllBytesAsync(jsonPath));
            var definition = ArchiveTestCatalog.GetCase(testCase.CaseKey);

            // The published log is the ordered chain, validated end to end.
            Assert.Equal(definition.Mutations!.Select(kind => kind.ToCaseKey()), testCase.Mutations.Select(m => m.Code));
            Assert.Empty(ArchiveTestCaseSemantics.Validate(testCase));

            // Chained expectations: the CRC capability is only named for the chain that
            // keeps a readable entry; the truncation chain fails every operation.
            var readEntry = testCase.Expectations.Single(e => e.Operation == "read-entry");
            if (testCase.CaseKey == "combined-crc-and-name")
            {
                Assert.Contains("read-entry-returns-unverified-bytes", readEntry.AllowedOutcomes);
                Assert.Equal("crc32", testCase.Expectations.Single(e => e.Operation == "integrity-check").Capability);
            }
            else
            {
                Assert.Equal(["operation-fails"], readEntry.AllowedOutcomes);
            }
        }
    }
}
