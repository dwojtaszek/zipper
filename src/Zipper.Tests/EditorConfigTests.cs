using System.Text.RegularExpressions;
using Xunit;

namespace Zipper.Tests;

/// <summary>
/// Tests that verify .editorconfig contains the required settings.
/// </summary>
public class EditorConfigTests
{
    private const string EditorConfigFileName = ".editorconfig";
    private const int RegexTimeoutSeconds = 1;

    private static readonly string RepoRoot = RepoRootFinder.Find();

    [Fact]
    public void EditorConfig_ShouldHave_SecurityCategory_SetToError()
    {
        var path = Path.Combine(RepoRoot, EditorConfigFileName);
        var content = File.ReadAllText(path);

        // Regex ensures we match the setting at the start of a line (or preceded by spaces, but not #)
        // and allows flexible whitespace around the equals sign.
        var hasSecurityCategory = Regex.IsMatch(content, @"^(?!\s*#)\s*dotnet_analyzer_diagnostic\.category-Security\.severity\s*=\s*error", RegexOptions.Multiline, System.TimeSpan.FromSeconds(RegexTimeoutSeconds));

        Assert.True(
            hasSecurityCategory,
            "Expected .editorconfig to elevate security analyzer rules (category-Security) to error severity."
        );
    }

    [Theory]
    [InlineData("ZIP001")]
    [InlineData("ZIP002")]
    [InlineData("ZIP003")]
    [InlineData("ZIP004")]
    [InlineData("CA2022")]
    [InlineData("MA0042")]
    [InlineData("MA0074")]
    [InlineData("MA0240")]
    [InlineData("RCS1075")]
    [InlineData("RS0030")]
    public void EditorConfig_ShouldEnforceCriticalRuleAsErrorInProductionSection(string diagnosticId)
    {
        var path = Path.Combine(RepoRoot, EditorConfigFileName);
        var content = File.ReadAllText(path);

        var testSectionIndex = content.IndexOf("[**/{Zipper.Tests,Zipper.Analyzers.Tests}/**.cs]", StringComparison.Ordinal);
        Assert.True(testSectionIndex > 0, "Expected test-project override section in .editorconfig.");

        var productionSection = content[..testSectionIndex];
        var pattern = $@"^(?!\s*#)\s*dotnet_diagnostic\.{Regex.Escape(diagnosticId)}\.severity\s*=\s*error\b";
        var isEnforcedInProduction = Regex.IsMatch(
            productionSection,
            pattern,
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(RegexTimeoutSeconds));

        Assert.True(
            isEnforcedInProduction,
            $"Expected {diagnosticId} to be set to error in the production [*.cs] section (before the test override section) of .editorconfig.");
    }

    [Fact]
    public void EditorConfig_TestSection_ShouldOnlySuppressRulesNotEnableProductionRules()
    {
        var path = Path.Combine(RepoRoot, EditorConfigFileName);
        var content = File.ReadAllText(path);

        var testSectionIndex = content.IndexOf("[**/{Zipper.Tests,Zipper.Analyzers.Tests}/**.cs]", StringComparison.Ordinal);
        Assert.True(testSectionIndex > 0, "Expected test-project override section in .editorconfig.");

        var testSection = content[testSectionIndex..];
        var activeRuleInTestSection = Regex.Match(
            testSection,
            @"^(?!\s*#)\s*dotnet_diagnostic\.\w+\.severity\s*=\s*(warning|error|suggestion)\b",
            RegexOptions.Multiline,
            TimeSpan.FromSeconds(RegexTimeoutSeconds));

        Assert.False(
            activeRuleInTestSection.Success,
            $"Found non-none diagnostic severity in test-only section ('{activeRuleInTestSection.Value.Trim()}'). Production rules must appear in [*.cs] above the test section.");
    }
}
