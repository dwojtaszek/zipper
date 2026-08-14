using Xunit;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class BatesModuleTests
{
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
    public void TryApply_CommaSeparatedStart_ParsesList()
    {
        var module = new BatesModule();
        Assert.True(module.TryApply("--bates-start", "1, 5"));

        Assert.Equal(new[] { 1L, 5L }, module.BatesStarts);
        Assert.Equal(1, module.BatesStart);
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
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                TryBuild(false, 1, "continuous", null, new[] { "--bates-prefix", "foo/bar" }, out _);
                Assert.Contains("Error: --bates-prefix must not contain path separators.", errWriter.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
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
    public void TryBuild_ProductionSet_ContinuousOverlap_ReturnsFalse()
    {
        Assert.False(TryBuild(true, 2, "continuous", 10, new[] { "--bates-prefix", "PROD,PROD", "--bates-start", "1,5" }, out _));
    }

    [Fact]
    public void TryBuild_ProductionSet_ContinuousOverlap_EmitsError()
    {
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                TryBuild(true, 2, "continuous", 10, new[] { "--bates-prefix", "PROD,PROD", "--bates-start", "1,5" }, out _);
                Assert.Contains("Error: Bates ranges overlap for prefix 'PROD': Set 1 (1-10) and Set 2 (5-14).", errWriter.ToString(), StringComparison.Ordinal);
            }
            finally
            {
                Console.SetError(originalError);
            }
        }
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
