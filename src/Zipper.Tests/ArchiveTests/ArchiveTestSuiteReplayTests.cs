using System.Security.Cryptography;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Ticket #846: replay determinism and frozen byte goldens. Identical seed and
/// options into separate fresh directories must publish byte-identical pairs, and
/// stored/header-only controls carry cross-runtime byte goldens. Deflate controls
/// are replay-checked for same-runtime identity only — the contract (#834) makes no
/// cross-runtime deflate promise.
/// </summary>
public class ArchiveTestSuiteReplayTests : TempDirectoryTestBase
{
    /// <summary>The valid-stored pair at Seed 42: stored entries plus fixed timestamps
    /// are byte-stable across .NET runtimes, so both the Fixture ID and the final
    /// Archive SHA-256 are frozen. Proven on the Linux/Windows/macOS E2E matrix.
    /// Golden mirrored in tests/test-archive-test-suites.sh, .bat, and
    /// docs/archive-test-suites.md — change all four together.</summary>
    private const string FrozenValidStoredFixtureId = "atc-662d70277000fd379d41c3d096e0ef7d3075b668f7a33860bdf52117ab2e04ca";
    private const string FrozenValidStoredArchiveSha256 = "275c4c4da2722c54625b2175a78898eb27c20e28878ac3948e9854c7fa3dc9a5";

    private static ArchiveTestRequest SmokeRequest(string outputDirectory) =>
        ArchiveTestRequest.Create(
            [.. ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.SmokeSuite).Select(c => c.CaseKey)],
            42,
            outputDirectory);

    private async Task<ArchiveTestSuiteResult> PublishSmokeAsync(string directoryName) =>
        await ArchiveTestSuiteGenerator.GenerateAsync(
            SmokeRequest(Path.Combine(TempDir, directoryName)), CancellationToken.None);

    [Fact]
    public async Task GenerateAsync_SameSeedSeparateFreshDirectories_PublishesByteIdenticalPairs()
    {
        var first = await PublishSmokeAsync("replay-first");
        var second = await PublishSmokeAsync("replay-second");

        // Same Fixture IDs in the same ordinal order, and every pair is byte-identical:
        // Archive bytes and Expectation File bytes both replay exactly.
        Assert.Equal(first.FixtureIds, second.FixtureIds);

        foreach (var fixtureId in first.FixtureIds)
        {
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(first.PublishedDirectory, fixtureId + ".zip")),
                await File.ReadAllBytesAsync(Path.Combine(second.PublishedDirectory, fixtureId + ".zip")));
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(first.PublishedDirectory, fixtureId + ".json")),
                await File.ReadAllBytesAsync(Path.Combine(second.PublishedDirectory, fixtureId + ".json")));
        }
    }

    [Fact]
    public async Task GenerateAsync_StoredControlAtSeedFortyTwo_MatchesFrozenCrossPlatformGolden()
    {
        var result = await ArchiveTestSuiteGenerator.GenerateAsync(
            ArchiveTestRequest.Create(["valid-stored"], 42, Path.Combine(TempDir, "stored-golden")),
            CancellationToken.None);

        var fixtureId = Assert.Single(result.FixtureIds);
        Assert.Equal(FrozenValidStoredFixtureId, fixtureId);

        var archiveBytes = await File.ReadAllBytesAsync(
            Path.Combine(result.PublishedDirectory, fixtureId + ".zip"));
        Assert.Equal(
            FrozenValidStoredArchiveSha256,
            Convert.ToHexStringLower(SHA256.HashData(archiveBytes)));
    }
}
