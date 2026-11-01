using Xunit;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class BatesModuleTests
{
    private const int MaxBatesPrefixBytes = 200;
    private static readonly string PrefixTooLongError = $"Error: --bates-prefix must not exceed {MaxBatesPrefixBytes} UTF-8 bytes.";

    private static string CaptureError(Action action) =>
        ConsoleOutputCapture.RunWithCapturedOutput(action).stderr;

    private static bool TryBuild(bool productionSet, int rollingCount, string? rollingBatesMode, long? count, string[] apply, out BatesNumberConfig? config)
    {
        var module = new BatesModule();
        for (int i = 0; i < apply.Length; i += 2)
        {
            Assert.True(module.TryApply(apply[i], apply[i + 1]));
        }
        return module.TryBuild(productionSet, rollingCount, rollingBatesMode, count, out config);
    }

    [Fact]
    public void TryBuild_NoBatesArgs_BuildsNullConfig()
    {
        Assert.True(TryBuild(false, 1, "continuous", null, Array.Empty<string>(), out var config));
        Assert.Null(config);
    }

    [Fact]
    public void TryBuild_BatesPrefixStartDigits_BuildsConfig()
    {
        Assert.True(TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", "CL001", "--bates-start", "100", "--bates-digits", "6" }, out var config));

        Assert.NotNull(config);
        Assert.Equal("CL001", config!.Prefix);
        Assert.Equal(100, config.Start);
        Assert.Equal(6, config.Digits);
    }

    [Fact]
    public void TryBuild_StartOnly_WithoutPrefix_BuildsNullConfig()
    {
        Assert.True(TryBuild(false, 1, "continuous", null, new[] { "--bates-start", "100" }, out var config));
        Assert.Null(config);
    }

    [Fact]
    public void TryBuild_PrefixOnly_AppliesDefaults()
    {
        Assert.True(TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", "CL001" }, out var config));

        Assert.NotNull(config);
        Assert.Equal("CL001", config!.Prefix);
        Assert.Equal(1, config.Start);
        Assert.Equal(8, config.Digits);
    }

    [Fact]
    public void TryApply_CommaSeparatedPrefix_ParsesList()
    {
        var module = new BatesModule();
        Assert.True(module.TryApply("--bates-prefix", "PROD, PROD2"));

        Assert.Equal(new[] { "PROD", "PROD2" }, module.BatesPrefixes);
        Assert.Equal("PROD, PROD2", module.BatesPrefix);
    }

    [Fact]
    public void TryApply_PrefixExceedingMaximumLength_ShouldReturnFalseAndEmitError()
    {
        var error = CaptureError(() =>
        {
            var module = new BatesModule();
            Assert.False(module.TryApply("--bates-prefix", new string('A', MaxBatesPrefixBytes + 1)));
        });

        Assert.Equal(PrefixTooLongError, error.TrimEnd(Environment.NewLine.ToCharArray()));
    }

    [Fact]
    public void TryApply_PrefixAtMaximumLength_ShouldReturnTrue()
    {
        var module = new BatesModule();

        Assert.True(module.TryApply("--bates-prefix", new string('A', MaxBatesPrefixBytes)));
    }

    [Fact]
    public void TryApply_MultibytePrefixExceedingByteLimit_ShouldReturnFalseAndEmitError()
    {
        // 101 'é' is 101 characters (inside a 200-character reading of the limit)
        // but 202 UTF-8 bytes: only the byte cap can reject it.
        var multibytePrefix = new string('é', MaxBatesPrefixBytes / 2 + 1);
        var error = CaptureError(() =>
        {
            var module = new BatesModule();
            Assert.False(module.TryApply("--bates-prefix", multibytePrefix));
        });

        Assert.Equal(PrefixTooLongError, error.TrimEnd(Environment.NewLine.ToCharArray()));
    }

    [Fact]
    public void TryApply_CommaSeparatedPrefixesEachWithinLimit_ShouldReturnTrue()
    {
        // A rolling list may exceed the limit in total while every Bates Number prefix stays
        // within it: the limit guards each emitted prefix, not the comma-joined flag value
        // (REQ-225 Production Metadata budget).
        var module = new BatesModule();

        Assert.True(module.TryApply(
            "--bates-prefix",
            $"{new string('A', MaxBatesPrefixBytes)},{new string('B', MaxBatesPrefixBytes)}"));
    }

    [Fact]
    public void TryApply_CommaSeparatedPrefixesWithWhitespaceAtLimit_ShouldTrimAndReturnTrue()
    {
        var firstPrefix = new string('A', MaxBatesPrefixBytes);
        var secondPrefix = new string('B', MaxBatesPrefixBytes);
        var module = new BatesModule();

        Assert.True(module.TryApply("--bates-prefix", $" {firstPrefix} , {secondPrefix} "));

        Assert.Equal(new[] { firstPrefix, secondPrefix }, module.BatesPrefixes);
    }

    [Fact]
    public void TryBuild_MultibytePrefixAtByteLimit_ShouldPreservePrefix()
    {
        var prefix = new string('é', MaxBatesPrefixBytes / 2);

        Assert.True(TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", prefix }, out var config));
        Assert.NotNull(config);
        Assert.Equal(prefix, config!.Prefix);
    }

    [Fact]
    public void TryApply_CommaSeparatedPrefixExceedingMaximumLength_ShouldReturnFalseAndEmitError()
    {
        var error = CaptureError(() =>
        {
            var module = new BatesModule();
            Assert.False(module.TryApply(
                "--bates-prefix",
                $"A,{new string('B', MaxBatesPrefixBytes + 1)}"));
        });

        Assert.Equal(PrefixTooLongError, error.TrimEnd(Environment.NewLine.ToCharArray()));
    }

    [Fact]
    public void TryApply_CommaSeparatedStart_ParsesList()
    {
        var module = new BatesModule();
        Assert.True(module.TryApply("--bates-start", "1, 5"));

        Assert.Equal(new[] { 1L, 5L }, module.BatesStarts);
        Assert.Equal(1, module.BatesStart);
    }

    [Fact]
    public void TryBuild_CommaSeparatedPrefix_BuildsConfigWithPrimaryPrefixAndList()
    {
        Assert.True(TryBuild(true, 2, "continuous", 5, new[] { "--bates-prefix", "PROD, PROD2" }, out var config));
        Assert.NotNull(config);
        Assert.Equal("PROD", config!.Prefix);
        Assert.Equal(new[] { "PROD", "PROD2" }, config.Prefixes);
    }

    [Fact]
    public void TryBuild_MultiPrefixWithDirectoryTraversal_ReturnsFalse()
    {
        Assert.False(TryBuild(true, 2, "continuous", 5, new[] { "--bates-prefix", "PROD, .." }, out _));
    }

    [Fact]
    public void TryBuild_PrefixWithPathSeparator_ReturnsFalse()
    {
        Assert.False(TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", "foo/bar" }, out _));
        Assert.False(TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", "foo\\bar" }, out _));
    }

    [Fact]
    public void TryBuild_PrefixWithDotDot_ReturnsFalse()
    {
        Assert.False(TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", ".." }, out _));
    }

    [Fact]
    public void TryBuild_PrefixWithSpecialChars_ReturnsFalse()
    {
        Assert.False(TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", "hello!@#" }, out _));
    }

    [Fact]
    public void TryBuild_InvalidPrefix_EmitsError()
    {
        var error = CaptureError(() =>
        {
            TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", "foo/bar" }, out _);
        });

        Assert.Contains("Error: --bates-prefix must not contain path separators.", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TakesValue_BatesPageSuffix_IsParameterless()
    {
        var module = new BatesModule();

        Assert.True(module.Owns("--bates-page-suffix"));
        Assert.False(module.TakesValue("--bates-page-suffix"));
    }

    [Fact]
    public void TryBuild_PageSuffixWithPrefix_BuildsConfigWithPageSuffix()
    {
        var module = new BatesModule();
        Assert.True(module.TryApply("--bates-prefix", "CL001"));
        Assert.True(module.TryApply("--bates-page-suffix", null));

        Assert.True(module.TryBuild(false, 1, "continuous", null, out var config));

        Assert.NotNull(config);
        Assert.True(config!.PageSuffix);
    }

    [Fact]
    public void TryBuild_DefaultArgs_LeavesPageSuffixOff()
    {
        Assert.True(TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", "CL001" }, out var config));

        Assert.NotNull(config);
        Assert.False(config!.PageSuffix);
    }

    [Fact]
    public void TryBuild_PageSuffixWithoutPrefix_ReturnsFalseWithError()
    {
        var module = new BatesModule();
        Assert.True(module.TryApply("--bates-page-suffix", null));
        BatesNumberConfig? config = null;
        bool built = true;

        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => built = module.TryBuild(false, 1, "continuous", null, out config));

        Assert.False(built);
        Assert.Null(config);
        Assert.Contains("Error: --bates-page-suffix requires --bates-prefix.", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_PrefixOverPlatformLimit_WarnsAndStillBuilds()
    {
        var prefix = new string('A', 37);
        bool built = false;
        BatesNumberConfig? config = null;

        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => built = TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", prefix }, out config));

        Assert.True(built);
        Assert.NotNull(config);
        Assert.Contains($"Warning: Bates prefix '{prefix}' is 37 characters", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_PrefixAtPlatformLimit_DoesNotWarn()
    {
        var prefix = new string('A', 36);
        bool built = false;

        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => built = TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", prefix }, out _));

        Assert.True(built);
        Assert.DoesNotContain("Warning:", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_DigitsOverPlatformLimit_WarnsAndStillBuilds()
    {
        bool built = false;

        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => built = TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", "CL001", "--bates-digits", "17" }, out _));

        Assert.True(built);
        Assert.Contains("Warning: Bates numbers are 17 characters", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_PageSuffixPushesNumberOverPlatformLimit_Warns()
    {
        var module = new BatesModule();
        Assert.True(module.TryApply("--bates-prefix", "CL001"));
        Assert.True(module.TryApply("--bates-digits", "13"));
        Assert.True(module.TryApply("--bates-page-suffix", null));
        bool built = false;

        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => built = module.TryBuild(false, 1, "continuous", null, out _));

        Assert.True(built);
        Assert.Contains("Warning: Bates numbers are 17 characters including the page suffix", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_RangeOverDiscoLimit_Warns()
    {
        bool built = false;

        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => built = TryBuild(false, 1, "continuous", 30001L, new[] { "--bates-prefix", "CL001" }, out _));

        Assert.True(built);
        Assert.Contains("Warning: Bates range 1 through 30001 spans 30,001 Bates numbers", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_RangeAtDiscoLimit_DoesNotWarn()
    {
        bool built = false;

        var (_, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => built = TryBuild(false, 1, "continuous", 30000L, new[] { "--bates-prefix", "CL001" }, out _));

        Assert.True(built);
        Assert.DoesNotContain("Warning:", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void TryApply_InvalidBatesStart_ReturnsFalse()
    {
        Assert.False(new BatesModule().TryApply("--bates-start", "notanumber"));
    }

    [Fact]
    public void TryApply_InvalidBatesStartInList_ReturnsFalse()
    {
        Assert.False(new BatesModule().TryApply("--bates-start", "1,notanumber"));
    }

    [Fact]
    public void TryApply_InvalidBatesDigits_ReturnsFalse()
    {
        Assert.False(new BatesModule().TryApply("--bates-digits", "notanumber"));
    }

    [Fact]
    public void TryApply_MissingBatesStartValue_ReturnsFalse()
    {
        Assert.False(new BatesModule().TryApply("--bates-start", null));
    }

    [Fact]
    public void TryApply_UnknownFlag_ReturnsFalse()
    {
        Assert.False(new BatesModule().TryApply("--unknown-flag", "x"));
    }

    [Fact]
    public void TryBuild_ProductionSet_MismatchedPrefixCount_ReturnsFalse()
    {
        Assert.False(TryBuild(true, 2, "continuous", 10, new[] { "--bates-prefix", "PROD,PROD,PROD" }, out _));
    }

    [Fact]
    public void TryBuild_ProductionSet_EmptyPrefixInList_ReturnsFalse()
    {
        Assert.False(TryBuild(true, 2, "continuous", 10, new[] { "--bates-prefix", "PROD," }, out _));
    }

    [Fact]
    public void TryBuild_ProductionSet_MismatchedStartCount_ReturnsFalse()
    {
        Assert.False(TryBuild(true, 2, "continuous", 10, new[] { "--bates-prefix", "PROD,PROD", "--bates-start", "1,5,9" }, out _));
    }

    [Fact]
    public void TryBuild_ProductionSet_ContinuousOverlap_EmitsError()
    {
        bool result = true;
        var error = CaptureError(() =>
        {
            result = TryBuild(true, 2, "continuous", 10, new[] { "--bates-prefix", "PROD,PROD", "--bates-start", "1,5" }, out _);
        });

        Assert.False(result);
        Assert.Contains("Error: Bates ranges overlap for prefix 'PROD': Set 1 (1-10) and Set 2 (5-14).", error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryBuild_ProductionSet_ContinuousSequential_ReturnsTrue()
    {
        Assert.True(TryBuild(true, 3, "continuous", 5, new[] { "--bates-prefix", "PROD", "--bates-start", "1" }, out var config));
        Assert.NotNull(config);
    }

    [Fact]
    public void TryBuild_ProductionSet_RestartMode_ReturnsTrue()
    {
        Assert.True(TryBuild(true, 2, "restart", 5, new[] { "--bates-prefix", "PROD", "--bates-start", "1" }, out _));
    }
}
