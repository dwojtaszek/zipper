using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ChaosModuleTests
{
    private static bool TryBuildWithValues(ParsedArguments parsed, ChaosConfigSpec spec, out ChaosConfig config, bool loadfileOnly = true, LoadFileFormat currentFormat = LoadFileFormat.Dat)
    {
        var module = new ChaosModule();
        if (spec.Mode)
        {
            Assert.True(module.TryApply("--chaos-mode", null));
        }
        if (spec.Amount is not null)
        {
            Assert.True(module.TryApply("--chaos-amount", spec.Amount));
        }
        if (spec.Types is not null)
        {
            Assert.True(module.TryApply("--chaos-types", spec.Types));
        }
        if (spec.Scenario is not null)
        {
            Assert.True(module.TryApply("--chaos-scenario", spec.Scenario));
        }
        return module.TryBuild(parsed, loadfileOnly, currentFormat, out config);
    }

    private sealed record ChaosConfigSpec(string? Amount, string? Types, string? Scenario)
    {
        public bool Mode { get; init; }
    }

    [Fact]
    public void TryBuild_ChaosMode_SetsChaosProperties()
    {
        var parsed = new ParsedArguments();
        var spec = new ChaosConfigSpec(Amount: "5%", Types: "quotes,columns", Scenario: null) { Mode = true };
        Assert.True(TryBuildWithValues(parsed, spec, out var config));

        Assert.True(config.ChaosMode);
        Assert.Equal("5%", config.ChaosAmount);
        Assert.Equal("quotes,columns", config.ChaosTypes);
    }

    [Fact]
    public void TryBuild_ChaosModeWithoutLoadfileOnly_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        var spec = new ChaosConfigSpec(Amount: null, Types: null, Scenario: null) { Mode = true };
        Assert.False(TryBuildWithValues(parsed, spec, out _, loadfileOnly: false));
    }

    [Fact]
    public void TryBuild_ChaosAmountWithoutChaosMode_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        var spec = new ChaosConfigSpec(Amount: "5%", Types: null, Scenario: null);
        Assert.False(TryBuildWithValues(parsed, spec, out _));
    }

    [Fact]
    public void TryBuild_ChaosTypesWithoutChaosMode_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        var spec = new ChaosConfigSpec(Amount: null, Types: "quotes", Scenario: null);
        Assert.False(TryBuildWithValues(parsed, spec, out _));
    }

    [Fact]
    public void TryBuild_ChaosScenarioWithoutChaosMode_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        var spec = new ChaosConfigSpec(Amount: null, Types: null, Scenario: "basic");
        Assert.False(TryBuildWithValues(parsed, spec, out _));
    }

    [Fact]
    public void TryBuild_ChaosScenarioWithTypes_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        var spec = new ChaosConfigSpec(Amount: null, Types: "quotes", Scenario: "basic") { Mode = true };
        Assert.False(TryBuildWithValues(parsed, spec, out _));
    }

    [Fact]
    public void TryBuild_InvalidChaosAmount_ReturnsFalse()
    {
        var parsed = new ParsedArguments();
        var spec = new ChaosConfigSpec(Amount: "abc", Types: null, Scenario: null) { Mode = true };
        Assert.False(TryBuildWithValues(parsed, spec, out _));

        var spec2 = new ChaosConfigSpec(Amount: "10.5x%", Types: null, Scenario: null) { Mode = true };
        Assert.False(TryBuildWithValues(parsed, spec2, out _));
    }

    [Fact]
    public void IsValidChaosAmount_ValidPercentage_ReturnsTrue()
    {
        Assert.True(ChaosModule.IsValidChaosAmount("1%"));
        Assert.True(ChaosModule.IsValidChaosAmount("100%"));
        Assert.True(ChaosModule.IsValidChaosAmount("0.5%"));
    }

    [Fact]
    public void IsValidChaosAmount_ValidExact_ReturnsTrue()
    {
        Assert.True(ChaosModule.IsValidChaosAmount("500"));
        Assert.True(ChaosModule.IsValidChaosAmount("1"));
    }

    [Fact]
    public void IsValidChaosAmount_Invalid_ReturnsFalse()
    {
        Assert.False(ChaosModule.IsValidChaosAmount("abc"));
        Assert.False(ChaosModule.IsValidChaosAmount("0%"));
        Assert.False(ChaosModule.IsValidChaosAmount("-5"));
    }

    [Fact]
    public void TryBuild_ValidScenario_BuildsConfig()
    {
        var parsed = new ParsedArguments();
        var spec = new ChaosConfigSpec(Amount: null, Types: null, Scenario: "structured-import-failures") { Mode = true };
        Assert.True(TryBuildWithValues(parsed, spec, out var config));

        Assert.Equal("structured-import-failures", config.ChaosScenario);
    }

    [Fact]
    public void TryBuild_ChaosModeWithCsvFormat_ReturnsFalse()
    {
        // currentFormat is the single-format value; a non-DAT/OPT format rejects --chaos-mode.
        var parsed = new ParsedArguments();
        var spec = new ChaosConfigSpec(Amount: null, Types: null, Scenario: null) { Mode = true };
        Assert.False(TryBuildWithValues(parsed, spec, out _, currentFormat: LoadFileFormat.Csv));
    }

    [Fact]
    public void TryApply_ChaosArgs_ParsesCorrectly()
    {
        var module = new ChaosModule();
        Assert.True(module.TryApply("--chaos-mode", null));
        Assert.True(module.TryApply("--chaos-amount", "5%"));
        Assert.True(module.TryApply("--chaos-types", "quotes,columns"));
        Assert.True(module.TryApply("--chaos-scenario", "test"));

        var parsed = new ParsedArguments();
        Assert.False(module.TryBuild(parsed, true, LoadFileFormat.Dat, out _));
    }

    [Fact]
    public void TryApply_ChaosArgs_RoundTripsThroughConfig()
    {
        var parsed = new ParsedArguments();
        var spec = new ChaosConfigSpec(Amount: "5%", Types: "quotes,columns", Scenario: null) { Mode = true };
        Assert.True(TryBuildWithValues(parsed, spec, out var config));

        Assert.Equal("5%", config.ChaosAmount);
        Assert.Equal("quotes,columns", config.ChaosTypes);
    }

    [Fact]
    public void TryBuild_NullArg_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new ChaosModule().TryBuild(null!, false, LoadFileFormat.Dat, out _));
    }

    [Fact]
    public void TryApply_UnknownFlag_ReturnsFalse()
    {
        Assert.False(new ChaosModule().TryApply("--unknown-flag", "x"));
    }
}
