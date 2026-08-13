using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class BatesModuleTests
{
    private static bool TryBuild(ParsedArguments parsed, string[] apply, out BatesNumberConfig? config)
    {
        var module = new BatesModule();
        for (int i = 0; i < apply.Length; i += 2)
        {
            Assert.True(module.TryApply(apply[i], apply[i + 1]));
        }
        return module.TryBuild(parsed, out config);
    }

    [Fact]
    public void TryBuild_NoBatesArgs_BuildsNullConfig()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, Array.Empty<string>(), out var config));
        Assert.Null(config);
    }

    [Fact]
    public void TryBuild_BatesPrefixStartDigits_BuildsConfig()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, new[] { "--bates-prefix", "CL001", "--bates-start", "100", "--bates-digits", "6" }, out var config));

        Assert.NotNull(config);
        Assert.Equal("CL001", config!.Prefix);
        Assert.Equal(100, config.Start);
        Assert.Equal(6, config.Digits);
    }

    [Fact]
    public void TryBuild_StartOnly_WithoutPrefix_BuildsNullConfig()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, new[] { "--bates-start", "100" }, out var config));
        Assert.Null(config);
    }

    [Fact]
    public void TryBuild_PrefixOnly_AppliesDefaults()
    {
        var parsed = new ParsedArguments();
        Assert.True(TryBuild(parsed, new[] { "--bates-prefix", "CL001" }, out var config));

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
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, new[] { "--bates-prefix", "foo/bar" }, out _));
        Assert.False(TryBuild(parsed, new[] { "--bates-prefix", "foo\\bar" }, out _));
    }

    [Fact]
    public void TryBuild_PrefixWithDotDot_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, new[] { "--bates-prefix", ".." }, out _));
    }

    [Fact]
    public void TryBuild_PrefixWithSpecialChars_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        Assert.False(TryBuild(parsed, new[] { "--bates-prefix", "hello!@#" }, out _));
    }

    [Fact]
    public void TryBuild_InvalidPrefix_EmitsError()
    {
        var parsed = new ParsedArguments();
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                TryBuild(parsed, new[] { "--bates-prefix", "foo/bar" }, out _);
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
        var parsed = new ParsedArguments { ProductionSet = true, RollingCount = 2, Count = 10 };
        Assert.False(TryBuild(parsed, new[] { "--bates-prefix", "PROD,PROD,PROD" }, out _));
    }

    [Fact]
    public void TryBuild_ProductionSet_EmptyPrefixInList_ReturnsFalse()
    {
        var parsed = new ParsedArguments { ProductionSet = true, RollingCount = 2, Count = 10 };
        Assert.False(TryBuild(parsed, new[] { "--bates-prefix", "PROD," }, out _));
    }

    [Fact]
    public void TryBuild_ProductionSet_MismatchedStartCount_ReturnsFalse()
    {
        var parsed = new ParsedArguments { ProductionSet = true, RollingCount = 2, Count = 10 };
        Assert.False(TryBuild(parsed, new[] { "--bates-prefix", "PROD,PROD", "--bates-start", "1,5,9" }, out _));
    }

    [Fact]
    public void TryBuild_ProductionSet_ContinuousOverlap_ReturnsFalse()
    {
        var parsed = new ParsedArguments { ProductionSet = true, RollingCount = 2, Count = 10 };
        Assert.False(TryBuild(parsed, new[] { "--bates-prefix", "PROD,PROD", "--bates-start", "1,5" }, out _));
    }

    [Fact]
    public void TryBuild_ProductionSet_ContinuousOverlap_EmitsError()
    {
        var parsed = new ParsedArguments { ProductionSet = true, RollingCount = 2, Count = 10 };
        var originalError = Console.Error;
        using (var errWriter = new StringWriter())
        {
            Console.SetError(errWriter);
            try
            {
                TryBuild(parsed, new[] { "--bates-prefix", "PROD,PROD", "--bates-start", "1,5" }, out _);
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
        var parsed = new ParsedArguments { ProductionSet = true, RollingCount = 3, Count = 5 };
        Assert.True(TryBuild(parsed, new[] { "--bates-prefix", "PROD", "--bates-start", "1" }, out var config));
        Assert.NotNull(config);
    }

    [Fact]
    public void TryBuild_ProductionSet_RestartMode_ReturnsTrue()
    {
        var parsed = new ParsedArguments { ProductionSet = true, RollingCount = 2, Count = 5, RollingBatesMode = "restart" };
        Assert.True(TryBuild(parsed, new[] { "--bates-prefix", "PROD", "--bates-start", "1" }, out _));
    }

    [Fact]
    public void TryBuild_NullArg_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BatesModule().TryBuild(null!, out _));
    }
}
