using System.Text.RegularExpressions;
using Xunit;

namespace Zipper.Tests;

/// <summary>
/// Tests that verify .editorconfig contains the required settings.
/// </summary>
public class EditorConfigTests
{
    private const string SolutionFileName = "zipper.sln";
    private const string EditorConfigFileName = ".editorconfig";
    private const int RegexTimeoutSeconds = 1;

    private static readonly string RepoRoot = FindRepoRoot(System.AppContext.BaseDirectory);

    private static string FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new System.InvalidOperationException(
            $"Could not find repo root ({SolutionFileName}) walking up from: {startDir}");
    }

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
