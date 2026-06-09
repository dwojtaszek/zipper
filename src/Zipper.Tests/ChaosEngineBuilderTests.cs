using System.Reflection;
using Xunit;

namespace Zipper.Tests;

public class ChaosEngineBuilderTests
{
    private static object? GetPrivateField(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(obj);
    }

    [Fact]
    public void Build_WhenChaosModeIsDisabled_ReturnsNull()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with { ChaosMode = false };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.Null(engine);
    }

    [Fact]
    public void Build_WhenChaosModeIsEnabled_ReturnsEngineWithSpecifiedSettings()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosAmount = "5%",
            ChaosTypes = "mixed-delimiters,quotes"
        };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = "|",
            QuoteDelimiter = "\"",
            EndOfLine = "CRLF"
        };
        request.Metadata = request.Metadata with { Seed = 123 };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.NotNull(engine);

        var colDelim = GetPrivateField(engine, "columnDelimiter") as string;
        var quoteDelim = GetPrivateField(engine, "quoteDelimiter") as string;
        var eol = GetPrivateField(engine, "eol") as string;
        var format = GetPrivateField(engine, "format");
        var enabledTypes = GetPrivateField(engine, "enabledTypes") as HashSet<string>;

        Assert.Equal("|", colDelim);
        Assert.Equal("\"", quoteDelim);
        Assert.Equal("\r\n", eol);
        Assert.Equal(LoadFileFormat.Dat, format);

        Assert.NotNull(enabledTypes);
        Assert.Contains("mixed-delimiters", enabledTypes);
        Assert.Contains("quotes", enabledTypes);
        Assert.Equal(2, enabledTypes.Count);
    }

    [Fact]
    public void Build_WithScenario_ResolvesTypesAndAmountFromScenario()
    {
        // Arrange
        var scenarioName = "structured-import-failures";
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosScenario = scenarioName,
            ChaosAmount = null // Fall back to scenario default
        };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.NotNull(engine);
        var enabledTypes = GetPrivateField(engine, "enabledTypes") as HashSet<string>;
        var targetLines = GetPrivateField(engine, "targetLines") as HashSet<long>;

        var scenario = ChaosScenarios.GetByName(scenarioName);
        Assert.NotNull(scenario);

        var expectedTypes = new HashSet<string>(
            scenario.ChaosTypes.Split(',').Select(t => t.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

        Assert.NotNull(enabledTypes);
        Assert.Equal(expectedTypes, enabledTypes);

        var pct = double.Parse(scenario.DefaultAmount.TrimEnd('%'), System.Globalization.CultureInfo.InvariantCulture);
        var expectedCount = Math.Max(1, (int)(100 * pct / 100.0));

        Assert.NotNull(targetLines);
        Assert.Equal(expectedCount, targetLines.Count);
    }

    [Fact]
    public void Build_WithScenarioAndExplicitAmount_ScenarioResolvesTypesButKeepsExplicitAmount()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosScenario = "structured-import-failures",
            ChaosAmount = "20" // Explicit exact count
        };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.NotNull(engine);
        var targetLines = GetPrivateField(engine, "targetLines") as HashSet<long>;

        Assert.NotNull(targetLines);
        Assert.Equal(20, targetLines.Count);
    }

    [Fact]
    public void Build_WithInvalidScenario_IgnoresScenarioAndUsesRequestSettings()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with
        {
            ChaosMode = true,
            ChaosScenario = "nonexistent-scenario",
            ChaosAmount = "15",
            ChaosTypes = "mixed-delimiters"
        };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.NotNull(engine);
        var enabledTypes = GetPrivateField(engine, "enabledTypes") as HashSet<string>;
        var targetLines = GetPrivateField(engine, "targetLines") as HashSet<long>;

        Assert.NotNull(enabledTypes);
        Assert.Contains("mixed-delimiters", enabledTypes);
        Assert.Single(enabledTypes);

        Assert.NotNull(targetLines);
        Assert.Equal(15, targetLines.Count);
    }

    [Fact]
    public void Build_FormatOpt_ForcesOptDelimiters()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with { ChaosMode = true };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = "|",
            QuoteDelimiter = "\""
        };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Opt);

        // Assert
        Assert.NotNull(engine);
        var colDelim = GetPrivateField(engine, "columnDelimiter") as string;
        var quoteDelim = GetPrivateField(engine, "quoteDelimiter") as string;
        var format = GetPrivateField(engine, "format");

        Assert.Equal(",", colDelim); // Opt always uses comma
        Assert.Equal(string.Empty, quoteDelim); // Opt has no quote delimiter
        Assert.Equal(LoadFileFormat.Opt, format);
    }

    [Fact]
    public void Build_FormatDat_NullRequestDelimiters_FallbackToDefaultDelimiters()
    {
        // Arrange
        var request = new FileGenerationRequest();
        request.Chaos = request.Chaos with { ChaosMode = true };
        request.Delimiters = request.Delimiters with
        {
            ColumnDelimiter = null!, // Should default to thorn or similar
            QuoteDelimiter = null!
        };

        // Act
        var engine = ChaosEngineBuilder.Build(request, 100, LoadFileFormat.Dat);

        // Assert
        Assert.NotNull(engine);
        var colDelim = GetPrivateField(engine, "columnDelimiter") as string;
        var quoteDelim = GetPrivateField(engine, "quoteDelimiter") as string;

        Assert.Equal("\u0014", colDelim); // Defaults to Device Control 4 (Thorn-like delimiter in Concordance)
        Assert.Equal("\u00fe", quoteDelim); // Defaults to thorn quote
    }
}
