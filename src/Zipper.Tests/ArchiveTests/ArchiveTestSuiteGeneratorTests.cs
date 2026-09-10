using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

public class ArchiveTestSuiteGeneratorTests : TempDirectoryTestBase
{
    private static async Task<ArchiveTestSuiteResult> GenerateAsync(
        ArchiveTestRequest request, CancellationToken cancellationToken = default, Action<string>? afterStaging = null) =>
        await ArchiveTestSuiteGenerator.GenerateAsync(request, cancellationToken, afterStaging);

    [Fact]
    public async Task GenerateAsync_SmokeCases_PublishFlatPairsWithMatchingIds()
    {
        var request = ArchiveTestRequest.Create(
            ["valid-empty", "valid-stored", "valid-deflate"], seed: 42, outputPath: Path.Combine(TempDir, "out"));

        var result = await GenerateAsync(request);

        var published = Directory.GetFiles(result.PublishedDirectory);
        Assert.Equal(6, published.Length);
        Assert.Empty(Directory.GetDirectories(result.PublishedDirectory));

        foreach (var fixtureId in result.FixtureIds)
        {
            var zipPath = Path.Combine(result.PublishedDirectory, fixtureId + ".zip");
            var jsonPath = Path.Combine(result.PublishedDirectory, fixtureId + ".json");
            Assert.True(File.Exists(zipPath), $"{zipPath} missing");
            Assert.True(File.Exists(jsonPath), $"{jsonPath} missing");

            // Pair ID and final SHA verified from actual disk bytes.
            var zipBytes = await File.ReadAllBytesAsync(zipPath);
            var zipSha256 = Convert.ToHexStringLower(SHA256.HashData(zipBytes));

            using var json = await JsonDocument.ParseAsync(File.OpenRead(jsonPath));
            Assert.Equal(fixtureId, json.RootElement.GetProperty("fixtureId").GetString());
            Assert.Equal(fixtureId + ".zip", json.RootElement.GetProperty("archive").GetProperty("fileName").GetString());
            Assert.Equal(zipSha256, json.RootElement.GetProperty("archive").GetProperty("sha256").GetString());
            Assert.Equal(zipBytes.Length, json.RootElement.GetProperty("archive").GetProperty("physicalSize").GetInt64());

            // Fixture ID must reproduce from the descriptor over the on-disk fields.
            Assert.Equal(
                fixtureId,
                ArchiveTestIdentity.ComputeFixtureId(
                    json.RootElement.GetProperty("generatorContractVersion").GetString()!,
                    json.RootElement.GetProperty("caseKey").GetString()!,
                    json.RootElement.GetProperty("caseRevision").GetInt32(),
                    json.RootElement.GetProperty("expectationRevision").GetInt32(),
                    json.RootElement.GetProperty("seed").GetInt32(),
                    zipSha256));
        }
    }

    [Fact]
    public async Task GenerateAsync_SameSeedInFreshDirectories_ReproducesIdsAndBytes()
    {
        var first = await GenerateAsync(ArchiveTestRequest.Create(
            ["valid-empty", "valid-directories"], 42, Path.Combine(TempDir, "first")));
        var second = await GenerateAsync(ArchiveTestRequest.Create(
            ["valid-empty", "valid-directories"], 42, Path.Combine(TempDir, "second")));

        Assert.Equal(first.FixtureIds, second.FixtureIds);
        for (var i = 0; i < first.FixtureIds.Count; i++)
        {
            Assert.Equal(
                await File.ReadAllBytesAsync(Path.Combine(first.PublishedDirectory, first.FixtureIds[i] + ".zip")),
                await File.ReadAllBytesAsync(Path.Combine(second.PublishedDirectory, second.FixtureIds[i] + ".zip")));
        }
    }

    [Fact]
    public async Task GenerateAsync_DifferentSeed_ChangesIdsEvenForEmptyArchive()
    {
        var first = await GenerateAsync(ArchiveTestRequest.Create(["valid-empty"], 42, Path.Combine(TempDir, "a")));
        var second = await GenerateAsync(ArchiveTestRequest.Create(["valid-empty"], 43, Path.Combine(TempDir, "b")));

        Assert.NotEqual(first.FixtureIds, second.FixtureIds);
    }

    [Fact]
    public void Create_ExistingTargetDirectory_LeavesSentinelIntact()
    {
        var target = Path.Combine(TempDir, "existing");
        Directory.CreateDirectory(target);
        var sentinel = Path.Combine(target, "sentinel.txt");
        var sentinelBytes = "do not touch"u8.ToArray();
        File.WriteAllBytes(sentinel, sentinelBytes);

        var error = Assert.Throws<InvalidOperationException>(() => ArchiveTestRequest.Create(["valid-empty"], 42, target));

        Assert.Contains("already exists", error.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(target));
        Assert.Equal(sentinelBytes, File.ReadAllBytes(sentinel));
        Assert.DoesNotContain(
            Directory.GetFileSystemEntries(target),
            entry => Path.GetFileName(entry) != "sentinel.txt");
    }

    [Fact]
    public void Create_ExistingTargetFile_ThrowsWithoutCreatingDirectory()
    {
        var target = Path.Combine(TempDir, "existing-file");
        File.WriteAllText(target, "not a directory");

        Assert.Throws<InvalidOperationException>(() => ArchiveTestRequest.Create(["valid-empty"], 42, target));

        Assert.Equal("not a directory", File.ReadAllText(target));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public async Task GenerateAsync_CancellationAfterStaging_PublishesNothingAndCleansStaging()
    {
        using var cts = new CancellationTokenSource();
        ArchiveTestRequest request = ArchiveTestRequest.Create(["valid-stored", "valid-deflate"], 42, Path.Combine(TempDir, "out"));

        var error = await Assert.ThrowsAsync<OperationCanceledException>(
            () => GenerateAsync(request, cts.Token, afterStaging: _ => cts.Cancel()));

        Assert.NotNull(error);
        Assert.False(Directory.Exists(Path.Combine(TempDir, "out")));
        Assert.DoesNotContain(
            Directory.GetDirectories(TempDir),
            dir => Path.GetFileName(dir).StartsWith("atc-staging-", StringComparison.Ordinal));
        Assert.DoesNotContain(Directory.GetFiles(TempDir), file => file.Contains(".zip", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_TargetCollisionDuringPublication_FailsSafely()
    {
        ArchiveTestRequest request = ArchiveTestRequest.Create(["valid-empty"], 42, Path.Combine(TempDir, "raced"));
        var sentinel = Path.Combine(TempDir, "raced", "user-file.txt");
        var sentinelBytes = "survives rename attempts"u8.ToArray();

        // The destination appears after staging with user content in it: publication must
        // fail without merging, the sentinel must survive byte-for-byte, and the staged
        // pairs must be discarded with the staging directory.
        await Assert.ThrowsAsync<IOException>(
            () => GenerateAsync(request, afterStaging: _ =>
            {
                Directory.CreateDirectory(Path.Combine(TempDir, "raced"));
                File.WriteAllBytes(sentinel, sentinelBytes);
            }));

        Assert.True(Directory.Exists(Path.Combine(TempDir, "raced")));
        Assert.Equal(sentinelBytes, await File.ReadAllBytesAsync(sentinel));
        Assert.DoesNotContain(
            Directory.GetFiles(Path.Combine(TempDir, "raced")),
            file => file.EndsWith(".zip", StringComparison.Ordinal) || file.EndsWith(".json", StringComparison.Ordinal));
        Assert.DoesNotContain(
            Directory.GetDirectories(TempDir),
            dir => Path.GetFileName(dir).StartsWith("atc-staging-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GenerateAsync_EmptyTargetAppearingDuringPublication_FailsSafely()
    {
        ArchiveTestRequest request = ArchiveTestRequest.Create(["valid-empty"], 42, Path.Combine(TempDir, "empty-raced"));

        // POSIX rename(2) silently replaces an existing empty destination directory, so the
        // explicit pre-rename guard must fail the run instead of merging into the empty dir.
        await Assert.ThrowsAsync<IOException>(
            () => GenerateAsync(request, afterStaging: _ => Directory.CreateDirectory(Path.Combine(TempDir, "empty-raced"))));

        Assert.True(Directory.Exists(Path.Combine(TempDir, "empty-raced")));
        Assert.Empty(Directory.GetFiles(Path.Combine(TempDir, "empty-raced")));
        Assert.DoesNotContain(
            Directory.GetDirectories(TempDir),
            dir => Path.GetFileName(dir).StartsWith("atc-staging-", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_UnknownCaseKey_ThrowsNamingKey()
    {
        var error = Assert.Throws<KeyNotFoundException>(
            () => ArchiveTestRequest.Create(["valid-empty", "valid-nope"], 42, Path.Combine(TempDir, "out")));

        Assert.Contains("valid-nope", error.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(TempDir, "out")));
    }

    [Fact]
    public void Create_DuplicateCaseKeys_Throws()
    {
        var error = Assert.Throws<ArgumentException>(
            () => ArchiveTestRequest.Create(["valid-empty", "valid-empty"], 42, Path.Combine(TempDir, "out")));

        Assert.Contains("valid-empty", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_EmptySelection_Throws()
    {
        Assert.Throws<ArgumentException>(() => ArchiveTestRequest.Create([], 42, Path.Combine(TempDir, "out")));
    }

    [Fact]
    public void Create_EscapeOutsideWorkingDirectory_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => ArchiveTestRequest.Create(["valid-empty"], 42, Path.Combine(TempDir, "..", "..", "..", "evil")));
    }

    [Fact]
    public async Task GenerateAsync_AllCases_StayUnderSuiteBudget()
    {
        // The whole valid catalog must publish successfully and stay far under the
        // 256 MiB suite budget; the checked accumulation enforces it during writes.
        var allCases = ArchiveTestCatalog.ListSuite("all").Select(c => c.CaseKey).ToList();
        var result = await GenerateAsync(ArchiveTestRequest.Create(allCases, 42, Path.Combine(TempDir, "all")));

        var suiteBytes = Directory.GetFiles(result.PublishedDirectory).Sum(path => new FileInfo(path).Length);
        Assert.True(suiteBytes < ArchiveTestRequest.MaxSuiteBytes);
        Assert.Equal(allCases.Count * 2, Directory.GetFiles(result.PublishedDirectory).Length);
    }

    [Fact]
    public async Task GenerateAsync_PublishedPairs_PassSemanticValidationFromDisk()
    {
        var result = await GenerateAsync(ArchiveTestRequest.Create(["valid-directories"], 42, Path.Combine(TempDir, "out")));

        var jsonPath = Path.Combine(result.PublishedDirectory, result.FixtureIds[0] + ".json");
        var testCase = ArchiveTestJson.Parse(await File.ReadAllBytesAsync(jsonPath));

        Assert.Empty(ArchiveTestCaseSemantics.Validate(testCase));
        Assert.Equal(3, testCase.Entries.Count);
        Assert.Equal("dir/", testCase.Entries[0].ReadableName);
        Assert.Equal("directory", testCase.Entries[0].Kind);
        Assert.Equal("dir/file.txt", testCase.Entries[1].ReadableName);
        Assert.Equal("file", testCase.Entries[1].Kind);
        Assert.Null(testCase.Entries[0].ContentSha256);
        Assert.NotNull(testCase.Entries[1].ContentSha256);
        Assert.Empty(testCase.Mutations);
    }
}
