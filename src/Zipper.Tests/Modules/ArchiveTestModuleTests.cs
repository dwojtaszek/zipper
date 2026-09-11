using Xunit;
using Zipper.ArchiveTests;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

/// <summary>
/// Ticket #844: the Archive Test CLI workflow — module parsing, the closed flag set,
/// suite/case/seed/output validation, publication, and exit codes (REQ-208/215/216,
/// ADR-0008).
/// </summary>
[Collection("ConsoleTests")]
public class ArchiveTestModuleTests : TempDirectoryTestBase
{
    private static readonly System.Text.RegularExpressions.Regex FixturePairPattern =
        new("^atc-[0-9a-f]{64}\\.(zip|json)$", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

    private static CliModuleSet Parse(params string[] args)
    {
        var modules = CliModules.Create();
        Assert.True(modules.Parse(args), "expected the arguments to parse");
        return modules;
    }

    private static async Task<int> RunAsync(params string[] args) =>
        await ArchiveTestCliWorkflow.RunAsync(Parse(args), CancellationToken.None);

    private static List<string> SuiteKeys(string suite) =>
        [.. ArchiveTestCatalog.ListSuite(suite).Select(definition => definition.CaseKey)];

    // ---- Module parsing ----

    [Fact]
    public void TryApply_ArchiveTestFlags_ParsesRawValues()
    {
        var modules = Parse("--archive-test-suite", "smoke", "--archive-test-cases", "valid-empty,valid-stored");

        Assert.Equal("smoke", modules.ArchiveTest.RawSuite);
        Assert.Equal("valid-empty,valid-stored", modules.ArchiveTest.RawCases);
        Assert.True(modules.ArchiveTest.IsRequested);
    }

    [Theory]
    [InlineData("--archive-test-suite")]
    [InlineData("--archive-test-cases")]
    public void Parse_MissingValueForArchiveTestFlags_ReturnsFalse(string flag)
    {
        Assert.False(CliModules.Create().Parse([flag]));
    }

    [Fact]
    public void Parse_CaseListWithoutSuite_StillEntersWorkflow()
    {
        var modules = Parse("--archive-test-cases", "valid-empty");

        Assert.Null(modules.ArchiveTest.RawSuite);
        Assert.True(modules.ArchiveTest.IsRequested);
    }

    [Fact]
    public void Parse_TracksConsumedFlagsIncludingExplicitDefaults()
    {
        var modules = Parse("--archive-test-suite", "smoke", "--folders", "1", "--output-path", Path.Combine(TempDir, "out"));

        Assert.Contains("--archive-test-suite", modules.ConsumedFlags);
        Assert.Contains("--folders", modules.ConsumedFlags);
        Assert.Contains("--output-path", modules.ConsumedFlags);
    }

    // ---- Workflow: success paths ----

    [Fact]
    public async Task RunAsync_SmokeSuiteNoSelection_PublishesAllMembers()
    {
        var destination = Path.Combine(TempDir, "smoke-out");
        var exitCode = await RunAsync("--archive-test-suite", "smoke", "--output-path", destination);

        Assert.Equal(0, exitCode);
        var smokeKeys = SuiteKeys(ArchiveTestCatalog.SmokeSuite);
        Assert.Equal(smokeKeys.Count * 2, Directory.GetFiles(destination).Length);
        Assert.All(Directory.GetFiles(destination), file =>
            Assert.Matches(FixturePairPattern, Path.GetFileName(file)));
    }

    [Fact]
    public async Task RunAsync_CaseSelection_FiltersToSelectedKeys()
    {
        var destination = Path.Combine(TempDir, "two-cases");
        var exitCode = await RunAsync(
            "--archive-test-suite", "smoke",
            "--archive-test-cases", "valid-deflate,valid-empty",
            "--output-path", destination);

        Assert.Equal(0, exitCode);
        Assert.Equal(4, Directory.GetFiles(destination).Length);
    }

    [Fact]
    public async Task RunAsync_SuiteNamesAreCaseInsensitive()
    {
        var exitCode = await RunAsync("--archive-test-suite", "SMOKE", "--output-path", Path.Combine(TempDir, "upper"));

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task RunAsync_OmittedSeedDefaultsToFortyTwo()
    {
        var defaults = Path.Combine(TempDir, "seed-default");
        var explicitFortyTwo = Path.Combine(TempDir, "seed-42");
        await RunAsync("--archive-test-suite", "smoke", "--output-path", defaults);
        await RunAsync("--archive-test-suite", "smoke", "--seed", "42", "--output-path", explicitFortyTwo);

        Assert.Equal(
            Directory.GetFiles(defaults).Select(Path.GetFileName).Order(StringComparer.Ordinal),
            Directory.GetFiles(explicitFortyTwo).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task RunAsync_DifferentSeed_ProducesDifferentFixtureIds()
    {
        var seed42 = Path.Combine(TempDir, "seed-42");
        var seed7 = Path.Combine(TempDir, "seed-7");
        await RunAsync("--archive-test-suite", "smoke", "--output-path", seed42);
        await RunAsync("--archive-test-suite", "smoke", "--seed", "7", "--output-path", seed7);

        var ids42 = Directory.GetFiles(seed42).Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        var ids7 = Directory.GetFiles(seed7).Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal);
        Assert.NotEqual(ids42, ids7);
    }

    // ---- Workflow: validation failures (exit 1) ----

    [Fact]
    public async Task RunAsync_CaseListWithoutSuite_Fails()
    {
        Assert.Equal(1, await RunAsync("--archive-test-cases", "valid-empty", "--output-path", Path.Combine(TempDir, "x")));
    }

    [Fact]
    public async Task RunAsync_UnknownSuite_Fails()
    {
        Assert.Equal(1, await RunAsync("--archive-test-suite", "bogus", "--output-path", Path.Combine(TempDir, "x")));
    }

    [Fact]
    public async Task RunAsync_UnknownCaseKey_Fails()
    {
        Assert.Equal(1, await RunAsync(
            "--archive-test-suite", "smoke", "--archive-test-cases", "no-such-case", "--output-path", Path.Combine(TempDir, "x")));
    }

    [Fact]
    public async Task RunAsync_CaseKeyOutsideSelectedSuite_Fails()
    {
        Assert.Equal(1, await RunAsync(
            "--archive-test-suite", "smoke", "--archive-test-cases", "path-parent-traversal", "--output-path", Path.Combine(TempDir, "x")));
    }

    [Fact]
    public async Task RunAsync_DuplicateCaseKeys_Fail()
    {
        Assert.Equal(1, await RunAsync(
            "--archive-test-suite", "smoke", "--archive-test-cases", "valid-empty,valid-empty", "--output-path", Path.Combine(TempDir, "x")));
    }

    [Fact]
    public async Task RunAsync_DuplicateCaseKeysAfterTrim_Fail()
    {
        // Tokens are trimmed before validation, so whitespace does not disguise a duplicate.
        Assert.Equal(1, await RunAsync(
            "--archive-test-suite", "smoke", "--archive-test-cases", "valid-empty, valid-empty", "--output-path", Path.Combine(TempDir, "x")));
    }

    [Fact]
    public async Task RunAsync_OutputPathOutsideWorkingDirectory_Fails()
    {
        var outside = Path.Combine(Path.GetTempPath(), "zipper-atc-escape-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(1, await RunAsync("--archive-test-suite", "smoke", "--output-path", outside));
        Assert.False(Directory.Exists(outside));
    }

    [Fact]
    public async Task RunAsync_EmptyCaseTokenFails()
    {
        Assert.Equal(1, await RunAsync(
            "--archive-test-suite", "smoke", "--archive-test-cases", "valid-empty,,valid-stored", "--output-path", Path.Combine(TempDir, "x")));
    }

    [Fact]
    public async Task RunAsync_MissingOutputPath_Fails()
    {
        Assert.Equal(1, await RunAsync("--archive-test-suite", "smoke"));
    }

    [Theory]
    [InlineData("--with-text")]
    [InlineData("--chaos-mode")]
    public async Task RunAsync_DisallowedRegisteredFlag_Fails(string flag)
    {
        Assert.Equal(1, await RunAsync("--archive-test-suite", "smoke", flag, "--output-path", Path.Combine(TempDir, "x")));
    }

    [Fact]
    public async Task RunAsync_DisallowedFlagAtExplicitDefaultValue_Fails()
    {
        // Presence-based rejection: --folders 1 equals the default, but it is still a
        // generation flag outside the closed set.
        Assert.Equal(1, await RunAsync(
            "--archive-test-suite", "smoke", "--folders", "1", "--output-path", Path.Combine(TempDir, "x")));
    }

    [Fact]
    public async Task RunAsync_MixedComparisonWorkflow_Fails()
    {
        Assert.Equal(1, await RunAsync(
            "--archive-test-suite", "smoke", "--loadfile-only", "--output-path", Path.Combine(TempDir, "x")));
    }

    // ---- Publication safety ----

    [Fact]
    public async Task RunAsync_RepeatedRunIntoSameDirectory_FailsAndPreservesFirstRun()
    {
        var destination = Path.Combine(TempDir, "repeat");
        Assert.Equal(0, await RunAsync("--archive-test-suite", "smoke", "--output-path", destination));

        var before = Directory.GetFiles(destination)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllBytes)
            .ToList();

        Assert.Equal(1, await RunAsync("--archive-test-suite", "smoke", "--output-path", destination));

        var after = Directory.GetFiles(destination)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllBytes)
            .ToList();
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task RunAsync_NewDirectoryReplay_RepeatsFixtureIds()
    {
        var first = Path.Combine(TempDir, "replay-a");
        var second = Path.Combine(TempDir, "replay-b");
        await RunAsync("--archive-test-suite", "malformed", "--archive-test-cases", "crc-both-mismatch,missing-eocd", "--output-path", first);
        await RunAsync("--archive-test-suite", "malformed", "--archive-test-cases", "missing-eocd,crc-both-mismatch", "--output-path", second);

        Assert.Equal(
            Directory.GetFiles(first).Select(Path.GetFileName).Order(StringComparer.Ordinal),
            Directory.GetFiles(second).Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    // ---- Cancellation (exit 130) ----

    [Fact]
    public async Task RunAsync_PreCancelledToken_Returns130WithoutCreatingDestination()
    {
        var destination = Path.Combine(TempDir, "cancelled");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var exitCode = await ArchiveTestCliWorkflow.RunAsync(
            Parse("--archive-test-suite", "smoke", "--output-path", destination), cts.Token);

        Assert.Equal(130, exitCode);
        Assert.False(Directory.Exists(destination));
        Assert.Empty(Directory.GetFileSystemEntries(TempDir));
    }
}
