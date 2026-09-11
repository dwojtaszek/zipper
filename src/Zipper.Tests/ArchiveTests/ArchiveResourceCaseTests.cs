using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Ticket #843 bounded resource cases: honest high compression, a declared-size lie
/// over tiny physical bytes, genuine depth-two Archive nesting, and the exact
/// 1,000-entry cap — plus at-limit and one-over budget rejection. All reads are
/// read-only; nothing here extracts to the filesystem.
/// </summary>
public class ArchiveResourceCaseTests : TempDirectoryTestBase
{
    private const string Oversized = "33554433";

    private static ArchiveFixtureArtifact BuildControl(string caseKey) =>
        ArchiveFixtureBuilder.BuildControl(caseKey, 42, CancellationToken.None);

    private static uint ReadUInt32(byte[] bytes, long offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan((int)offset, 4));

    /// <summary>Builds a synthetic definition that never enters the catalog.</summary>
    private static ArchiveTestCaseDefinition Synthetic(string caseKey, ArchiveTestRecipe recipe) =>
        new(caseKey, 1, 1, "valid", [ArchiveTestCatalog.CompatibilitySuite], recipe);

    // ---- high-ratio-bounded ----

    [Fact]
    public void Build_HighRatioBounded_ExpandedBytesAreHonestAndRepeated()
    {
        var artifact = BuildControl("high-ratio-bounded");
        var entry = artifact.Layout.Entries[0];
        var content = artifact.Entries[0].Content;

        Assert.Equal(1024 * 1024, content.Length);
        Assert.Equal(1024 * 1024, (long)entry.UncompressedSize);
        Assert.All(content, byte_ => Assert.Equal((byte)0x41, byte_));

        // Honest sizes, high ratio: the physical Archive stays a tiny fraction of the
        // expanded content, and the declared sizes match the actual bytes.
        Assert.True(
            content.Length / artifact.ArchiveBytes.Length > 100,
            $"expected a high ratio, got {content.Length} expanded over {artifact.ArchiveBytes.Length} physical");
        Assert.True(artifact.ArchiveBytes.Length < 128 * 1024, "physical Archive must stay well under budget");
    }

    [Fact]
    public void Read_HighRatioBounded_ReferenceReaderReturnsExactContent()
    {
        var artifact = BuildControl("high-ratio-bounded");

        using var archive = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read);
        using var stream = archive.GetEntry("hi-ratio.bin")!.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);

        Assert.Equal(artifact.Entries[0].ContentSha256, Convert.ToHexStringLower(SHA256.HashData(copy.ToArray())));
    }

    // ---- declared-size-oversized ----

    [Fact]
    public void Build_DeclaredSizeOversized_DeclaresBeyondBudgetWithoutGrowingBytes()
    {
        var control = BuildControl("valid-deflate");
        var artifact = BuildControl("declared-size-oversized");
        var entry = artifact.Layout.Entries[0];

        // Both headers declare 32 MiB + 1 — beyond the expanded budget...
        Assert.Equal(
            32 * 1024 * 1024 + 1u,
            ReadUInt32(artifact.ArchiveBytes, entry.LocalHeaderOffset + 22));
        Assert.Equal(
            32 * 1024 * 1024 + 1u,
            ReadUInt32(artifact.ArchiveBytes, entry.CentralDirectoryOffset + 24));

        // ...while the physical bytes stay the control's tiny size: a pure declaration
        // lie that must never cause a proportional allocation.
        Assert.Equal(control.ArchiveBytes.Length, artifact.ArchiveBytes.Length);
        Assert.Equal(400, artifact.Entries[0].Content.Length);

        // The declared value is recorded separately from the actual content length.
        Assert.Equal(2, artifact.Mutations.Count);
        Assert.All(artifact.Mutations, mutation =>
        {
            Assert.Equal("declared-size-oversized", mutation.Code);
            Assert.Contains(Oversized, mutation.DeclaredValue, StringComparison.Ordinal);
            Assert.Contains("actual-content-length=400", mutation.DeclaredValue, StringComparison.Ordinal);
        });
    }

    // ---- nested-archives-depth-two ----

    [Fact]
    public void Build_NestedArchivesDepthTwo_InnerArchiveIsRealAndFinite()
    {
        var artifact = BuildControl("nested-archives-depth-two");
        var innerBytes = artifact.Entries[0].Content;

        using (var inner = new ZipArchive(new MemoryStream(innerBytes), ZipArchiveMode.Read))
        {
            Assert.Single(inner.Entries);
            using var stream = inner.GetEntry("inner.txt")!.Open();
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            Assert.Equal("atc-nested-inner"u8.ToArray(), copy.ToArray());
        }

        // The outer Archive holds the inner Archive plus one plain sibling file.
        using var outer = new ZipArchive(new MemoryStream(artifact.ArchiveBytes), ZipArchiveMode.Read);
        Assert.Equal(2, outer.Entries.Count);
        Assert.NotNull(outer.GetEntry("inner.zip"));
        Assert.NotNull(outer.GetEntry("outer.txt"));
    }

    [Fact]
    public void Build_NestedArchivesDepthTwo_HasNoRecursiveReferenceOrThirdLevel()
    {
        var artifact = BuildControl("nested-archives-depth-two");

        // The inner Archive's single member is a regular file: depth two exactly, no
        // self-reference, no nested-nested Archive.
        using var inner = new ZipArchive(new MemoryStream(artifact.Entries[0].Content), ZipArchiveMode.Read);
        Assert.All(inner.Entries, entry => Assert.Equal("inner.txt", entry.Name));
    }

    // ---- many-small-entries ----

    [Fact]
    public void Build_ManySmallEntries_IsExactlyAtTheEntryCap()
    {
        var artifact = BuildControl("many-small-entries");

        Assert.Equal(ArchiveTestCaseSemantics.MaxEntries, artifact.Layout.EntryCount);
        Assert.Equal(
            Enumerable.Range(0, ArchiveTestCaseSemantics.MaxEntries),
            artifact.Layout.Entries.Select(entry => entry.Ordinal));
        Assert.Equal(
            artifact.Layout.EntryCount,
            artifact.Layout.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count());

        // The small members carry distinct deterministic content (SHA chain over the
        // distinct names): spot-check first vs last.
        Assert.NotEqual(artifact.Entries[0].ContentSha256, artifact.Entries[^1].ContentSha256);
    }

    [Fact]
    public async Task GenerateAsync_ManySmallEntries_SidecarStaysWithinBudget()
    {
        var result = await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create(["many-small-entries"], 42, Path.Combine(TempDir, "many")),
            CancellationToken.None);

        var jsonPath = Directory.GetFiles(result.PublishedDirectory, "*.json").Single();
        var jsonLength = new FileInfo(jsonPath).Length;
        Assert.True(jsonLength <= ArchiveTestRequest.MaxSidecarBytes, $"sidecar is {jsonLength} bytes");

        var testCase = ArchiveTestJson.Parse(await File.ReadAllBytesAsync(jsonPath));
        Assert.Equal(ArchiveTestCaseSemantics.MaxEntries, testCase.Entries.Count);
        Assert.Empty(ArchiveTestCaseSemantics.Validate(testCase));
    }

    // ---- at-limit and one-over budget rejection ----

    [Fact]
    public void Build_EntriesOneOverCap_ThrowsDescriptively()
    {
        var recipe = new ArchiveTestRecipe(
        [
            .. Enumerable.Range(0, ArchiveTestCaseSemantics.MaxEntries + 1)
                .Select(index => ArchiveTestRecipeEntry.File($"over-{index:0000}.txt", 4, "stored")),
        ]);

        var ex = Assert.Throws<InvalidDataException>(
            () => ArchiveFixtureBuilder.Build(Synthetic("synthetic-over-entries", recipe), 42, CancellationToken.None));
        Assert.Contains($"{ArchiveTestCaseSemantics.MaxEntries}-entry budget", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_PhysicalBytesOneOver_ThrowsDescriptively()
    {
        var recipe = new ArchiveTestRecipe(
        [
            ArchiveTestRecipeEntry.File(
                "big.bin", (int)ArchiveTestCaseSemantics.MaxArchivePhysicalBytes + 1, "stored"),
        ]);

        var ex = Assert.Throws<InvalidDataException>(
            () => ArchiveFixtureBuilder.Build(Synthetic("synthetic-over-physical", recipe), 42, CancellationToken.None));
        Assert.Contains("physical Archive budget", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ExpandedBytesOneOver_ThrowsDescriptively()
    {
        var recipe = new ArchiveTestRecipe(
        [
            ArchiveTestRecipeEntry.File(
                "wide.bin", (int)ArchiveTestCaseSemantics.MaxExpandedBytesBudget + 1, "stored"),
        ]);

        var ex = Assert.Throws<InvalidDataException>(
            () => ArchiveFixtureBuilder.Build(Synthetic("synthetic-over-expanded", recipe), 42, CancellationToken.None));
        Assert.Contains("expanded budget", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_ExpandedBytesSumOneOver_ThrowsDescriptively()
    {
        // Individually legal members whose cumulative expanded content exceeds the
        // budget: the checked sum must reject before any payload is generated.
        const int meg = 1024 * 1024;
        var recipe = new ArchiveTestRecipe(
        [
            .. Enumerable.Range(0, 33)
                .Select(index => ArchiveTestRecipeEntry.File($"sum-{index:00}.bin", meg, "stored")),
        ]);

        var ex = Assert.Throws<InvalidDataException>(
            () => ArchiveFixtureBuilder.Build(Synthetic("synthetic-over-expanded-sum", recipe), 42, CancellationToken.None));
        Assert.Contains("expanded budget", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_SameCasesDifferentOrder_ProduceIdenticalFixtureIds()
    {
        var forward = await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create(["high-ratio-bounded", "nested-archives-depth-two"], 42, Path.Combine(TempDir, "fwd")),
            CancellationToken.None);
        var backward = await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create(["nested-archives-depth-two", "high-ratio-bounded"], 42, Path.Combine(TempDir, "bwd")),
            CancellationToken.None);

        // Selection order never changes a case's bytes or Fixture ID (REQ-210).
        Assert.Equal(
            forward.FixtureIds.Order(StringComparer.Ordinal),
            backward.FixtureIds.Order(StringComparer.Ordinal));
        foreach (var fixtureId in forward.FixtureIds)
        {
            Assert.True(File.Exists(Path.Combine(backward.PublishedDirectory, fixtureId + ".zip")));
        }
    }
}
