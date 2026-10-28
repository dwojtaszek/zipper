using Xunit;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ChaosModuleTests
{
    private static bool TryBuildWithValues(ChaosConfigSpec spec, out ChaosConfig config, bool loadfileOnly = true, IReadOnlyList<LoadFileFormat>? formats = null)
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
        return module.TryBuild(loadfileOnly, formats ?? new List<LoadFileFormat> { LoadFileFormat.Dat }, out config);
    }

    private sealed record ChaosConfigSpec(string? Amount, string? Types, string? Scenario)
    {
        public bool Mode { get; init; }
    }

    [Fact]
    public void TryBuild_ChaosMode_SetsChaosProperties()
    {
        var spec = new ChaosConfigSpec(Amount: "5%", Types: "quotes,columns", Scenario: null) { Mode = true };
        Assert.True(TryBuildWithValues(spec, out var config));

        Assert.True(config.ChaosMode);
        Assert.Equal("5%", config.ChaosAmount);
        Assert.Equal("quotes,columns", config.ChaosTypes);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", null, true)]
    [InlineData("mixed-delimiters", null, false)]
    [InlineData("path-pointer", null, true)]
    [InlineData(null, "structured-import-failures", false)]
    [InlineData(null, "realistic-import-failures", true)]
    [InlineData(null, "full-chaos", true)]
    [InlineData("", "structured-import-failures", false)]
    [InlineData("", "full-chaos", true)]
    public void RequiresSourcePaths_UsesEffectiveDatAnomalyTypes(
        string? chaosTypes,
        string? scenario,
        bool expected)
    {
        var module = new ChaosModule();
        Assert.True(module.TryApply("--chaos-mode", null));
        if (chaosTypes is not null)
        {
            Assert.True(module.TryApply("--chaos-types", chaosTypes));
        }
        if (scenario is not null)
        {
            Assert.True(module.TryApply("--chaos-scenario", scenario));
        }

        Assert.Equal(expected, module.RequiresSourcePaths([LoadFileFormat.Dat]));
        Assert.False(module.RequiresSourcePaths([LoadFileFormat.Opt]));
    }

    [Fact]
    public void TryBuild_ChaosTypesWithWhitespace_StoresTrimmedNames()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: "quotes, columns", Scenario: null) { Mode = true };
        Assert.True(TryBuildWithValues(spec, out var config));

        Assert.Equal("quotes,columns", config.ChaosTypes);
    }

    [Fact]
    public void TryBuild_ChaosTypesWithWhitespace_AppliesEveryValidatedType()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: "quotes, columns", Scenario: null) { Mode = true };
        Assert.True(TryBuildWithValues(spec, out var config));

        var engine = new ChaosEngine(
            totalLines: 20,
            chaosAmount: "20",
            chaosTypes: config.ChaosTypes,
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 1234);

        var appliedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i <= 20; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                engine.Intercept(i, "\u00feValue\u00fe\u0014Other", $"DOC{i:D8}");
            }
        }

        foreach (var anomaly in engine.Anomalies)
        {
            appliedTypes.Add(anomaly.ErrorType);
        }

        Assert.Contains("quotes", appliedTypes);
        Assert.Contains("columns", appliedTypes);
    }

    [Fact]
    public void TryBuild_ChaosModeWithoutLoadfileOnly_ReturnsFalse()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: null, Scenario: null) { Mode = true };
        Assert.False(TryBuildWithValues(spec, out _, loadfileOnly: false));
    }
    [Fact]
    public void TryBuild_ChaosAmountWithoutChaosMode_ReturnsFalse()
    {
        var spec = new ChaosConfigSpec(Amount: "5%", Types: null, Scenario: null);
        Assert.False(TryBuildWithValues(spec, out _));
    }

    [Fact]
    public void TryBuild_ChaosTypesWithoutChaosMode_ReturnsFalse()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: "quotes", Scenario: null);
        Assert.False(TryBuildWithValues(spec, out _));
    }

    [Fact]
    public void TryBuild_ChaosScenarioWithoutChaosMode_ReturnsFalse()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: null, Scenario: "basic");
        Assert.False(TryBuildWithValues(spec, out _));
    }

    [Fact]
    public void TryBuild_ChaosScenarioWithTypes_ReturnsFalse()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: "quotes", Scenario: "basic") { Mode = true };
        Assert.False(TryBuildWithValues(spec, out _));
    }

    [Fact]
    public void TryBuild_InvalidChaosAmount_ReturnsFalse()
    {
        var spec = new ChaosConfigSpec(Amount: "abc", Types: null, Scenario: null) { Mode = true };
        Assert.False(TryBuildWithValues(spec, out _));

        var spec2 = new ChaosConfigSpec(Amount: "10.5x%", Types: null, Scenario: null) { Mode = true };
        Assert.False(TryBuildWithValues(spec2, out _));
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
        var spec = new ChaosConfigSpec(Amount: null, Types: null, Scenario: "structured-import-failures") { Mode = true };
        Assert.True(TryBuildWithValues(spec, out var config));

        Assert.Equal("structured-import-failures", config.ChaosScenario);
    }

    [Fact]
    public void TryBuild_ScenarioRequiringDifferentFormat_ReturnsFalse()
    {
        // 'broken-boundaries' requires the OPT format; asking for DAT is a mismatch.
        var spec = new ChaosConfigSpec(Amount: null, Types: null, Scenario: "broken-boundaries") { Mode = true };
        Assert.False(TryBuildWithValues(spec, out _, formats: new List<LoadFileFormat> { LoadFileFormat.Dat }));
    }

    [Fact]
    public void TryBuild_ScenarioRequiringMatchingFormat_BuildsConfig()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: null, Scenario: "broken-boundaries") { Mode = true };
        Assert.True(TryBuildWithValues(spec, out var config, formats: new List<LoadFileFormat> { LoadFileFormat.Opt }));

        Assert.Equal("broken-boundaries", config.ChaosScenario);
    }

    [Fact]
    public void TryBuild_OptEncodingTypeForOptFormat_BuildsConfig()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: "opt-encoding", Scenario: null) { Mode = true };

        Assert.True(TryBuildWithValues(spec, out var config, formats: new List<LoadFileFormat> { LoadFileFormat.Opt }));

        Assert.Equal("opt-encoding", config.ChaosTypes);
    }

    [Fact]
    public void TryBuild_ChaosModeWithCsvFormat_ReturnsFalse()
    {
        // With no types specified, CSV format still rejects --chaos-mode (requires explicit csv types).
        var spec = new ChaosConfigSpec(Amount: null, Types: null, Scenario: null) { Mode = true };
        Assert.False(TryBuildWithValues(spec, out _, formats: new List<LoadFileFormat> { LoadFileFormat.Csv }));
    }

    [Fact]
    public void TryBuild_ChaosModeWithCsvFormat_ReturnsTrue_WhenCsvChaosTypesSpecified()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: "csv-quote-escape", Scenario: null) { Mode = true };
        Assert.True(TryBuildWithValues(spec, out _, formats: new List<LoadFileFormat> { LoadFileFormat.Csv }));
    }

    [Fact]
    public void TryBuild_ChaosModeWithConcordanceFormat_ReturnsTrue_WhenConcordanceTypesSpecified()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: "concordance-delimiter", Scenario: null) { Mode = true };
        Assert.True(TryBuildWithValues(spec, out _, formats: new List<LoadFileFormat> { LoadFileFormat.Concordance }));
    }

    [Fact]
    public void TryBuild_ChaosModeWithEdrmXmlFormat_ReturnsTrue_WhenXmlTypesSpecified()
    {
        var spec = new ChaosConfigSpec(Amount: null, Types: "xml-unclosed-tag", Scenario: null) { Mode = true };
        Assert.True(TryBuildWithValues(spec, out _, formats: new List<LoadFileFormat> { LoadFileFormat.EdrmXml }));
    }

    [Fact]
    public void TryApply_ChaosArgs_ParsesCorrectly()
    {
        var module = new ChaosModule();
        Assert.True(module.TryApply("--chaos-mode", null));
        Assert.True(module.TryApply("--chaos-amount", "5%"));
        Assert.True(module.TryApply("--chaos-types", "quotes,columns"));
        Assert.True(module.TryApply("--chaos-scenario", "test"));

        Assert.False(module.TryBuild(true, new List<LoadFileFormat> { LoadFileFormat.Dat }, out _));
    }

    [Fact]
    public void TryApply_UnknownFlag_ReturnsFalse()
    {
        Assert.False(new ChaosModule().TryApply("--unknown-flag", "x"));
    }
}
