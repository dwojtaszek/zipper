using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class HashModuleTests
{
    private static HashModule CreateModule(string? mode = null, string? algorithms = null)
    {
        var module = new HashModule();
        if (mode is not null)
        {
            Assert.True(module.TryApply("--hash-mode", mode));
        }
        if (algorithms is not null)
        {
            Assert.True(module.TryApply("--hash-algorithms", algorithms));
        }
        return module;
    }

    [Theory]
    [InlineData("actual")]
    [InlineData("simulated")]
    [InlineData("none")]
    [InlineData("ACTUAL")]
    public void TryBuild_ValidHashMode_ReturnsTrue(string hashMode)
    {
        var module = CreateModule(mode: hashMode);
        Assert.True(module.TryBuild(new ParsedArguments(), out _));
    }

    [Fact]
    public void TryBuild_InvalidHashMode_ReturnsFalse()
    {
        var module = CreateModule(mode: "invalid");
        Assert.False(module.TryBuild(new ParsedArguments(), out _));
    }

    [Fact]
    public void TryBuild_EmptyHashMode_ReturnsFalse()
    {
        var module = CreateModule(mode: "");
        Assert.False(module.TryBuild(new ParsedArguments(), out _));
    }

    [Theory]
    [InlineData("md5")]
    [InlineData("sha1,sha256")]
    [InlineData("MD5,SHA256")]
    public void TryBuild_ValidHashAlgorithms_ReturnsTrue(string algorithms)
    {
        var module = CreateModule(mode: "actual", algorithms: algorithms);
        Assert.True(module.TryBuild(new ParsedArguments(), out _));
    }

    [Fact]
    public void TryBuild_InvalidHashAlgorithm_ReturnsFalse()
    {
        var module = CreateModule(mode: "actual", algorithms: "md5,sha512");
        Assert.False(module.TryBuild(new ParsedArguments(), out _));
    }

    [Fact]
    public void TryBuild_EmptyHashAlgorithms_ReturnsFalse()
    {
        var module = CreateModule(mode: "actual", algorithms: "");
        Assert.False(module.TryBuild(new ParsedArguments(), out _));
    }

    [Fact]
    public void TryBuild_MalformedHashAlgorithms_ReturnsFalse()
    {
        var module = CreateModule(mode: "actual", algorithms: "md5,,sha256");
        Assert.False(module.TryBuild(new ParsedArguments(), out _));
    }

    [Fact]
    public void TryBuild_HashAlgorithmsWithoutHashMode_ReturnsFalse()
    {
        var module = CreateModule(algorithms: "md5");
        Assert.False(module.TryBuild(new ParsedArguments(), out _));
    }

    [Fact]
    public void TryBuild_ActualWithLoadfileOnly_ReturnsFalse()
    {
        var parsed = new ParsedArguments
        {
            FileType = null,
            LoadfileOnly = true,
        };
        var module = CreateModule(mode: "actual");
        Assert.False(module.TryBuild(parsed, out _));
    }

    [Fact]
    public void TryBuild_ActualModeWithAlgorithms_SetsHashConfig()
    {
        var module = CreateModule(mode: "actual", algorithms: "md5,sha256");
        Assert.True(module.TryBuild(new ParsedArguments(), out var hash));

        Assert.Equal(Config.HashMode.Actual, hash.Mode);
        Assert.Contains(Config.HashAlgorithm.MD5, hash.Algorithms);
        Assert.Contains(Config.HashAlgorithm.SHA256, hash.Algorithms);
        Assert.DoesNotContain(Config.HashAlgorithm.SHA1, hash.Algorithms);
        Assert.True(hash.IsEnabled);
    }

    [Fact]
    public void TryBuild_SimulatedMode_SetsSimulatedMode()
    {
        var module = CreateModule(mode: "simulated", algorithms: "sha1");
        Assert.True(module.TryBuild(new ParsedArguments(), out var hash));

        Assert.Equal(Config.HashMode.Simulated, hash.Mode);
        Assert.Contains(Config.HashAlgorithm.SHA1, hash.Algorithms);
        Assert.True(hash.IsEnabled);
    }

    [Fact]
    public void TryBuild_Default_NoneModeEmptyAlgorithms()
    {
        var module = CreateModule();
        Assert.True(module.TryBuild(new ParsedArguments(), out var hash));

        Assert.Equal(Config.HashMode.None, hash.Mode);
        Assert.Empty(hash.Algorithms);
        Assert.False(hash.IsEnabled);
    }

    [Fact]
    public void Parse_ActualMode_ReturnsCorrectConfig()
    {
        var config = HashModule.Parse("actual", "md5,sha1,sha256");
        Assert.Equal(Config.HashMode.Actual, config.Mode);
        Assert.Equal(3, config.Algorithms.Count);
        Assert.Contains(Config.HashAlgorithm.MD5, config.Algorithms);
        Assert.Contains(Config.HashAlgorithm.SHA1, config.Algorithms);
        Assert.Contains(Config.HashAlgorithm.SHA256, config.Algorithms);
    }

    [Fact]
    public void Parse_SimulatedMode_ReturnsDefaultMD5()
    {
        var config = HashModule.Parse("simulated", null);
        Assert.Equal(Config.HashMode.Simulated, config.Mode);
        Assert.Contains(Config.HashAlgorithm.MD5, config.Algorithms);
        Assert.True(config.IsEnabled);
    }

    [Fact]
    public void Parse_InvalidMode_DefaultsToNone()
    {
        var config = HashModule.Parse("invalid", null);
        Assert.Equal(Config.HashMode.None, config.Mode);
    }

    [Fact]
    public void Parse_Default_NoneModeEmptyAlgorithms()
    {
        var config = HashModule.Parse(null, null);
        Assert.Equal(Config.HashMode.None, config.Mode);
        Assert.Empty(config.Algorithms);
    }

    [Fact]
    public void TryBuild_NullArg_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new HashModule().TryBuild(null!, out _));
    }

    [Fact]
    public void TryApply_UnknownFlag_ReturnsFalse()
    {
        Assert.False(new HashModule().TryApply("--unknown-flag", "x"));
    }
}
