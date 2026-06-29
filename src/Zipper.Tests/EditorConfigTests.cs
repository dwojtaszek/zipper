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
}
