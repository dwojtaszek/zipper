using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace Zipper.Tests;

/// <summary>
/// Verifies the presence and key structure of the architecture-deepening skill file.
/// </summary>
public class ArchitectureDeepeningSkillTests
{
    private const int RegexTimeoutSeconds = 1;
    private static readonly string SkillRelativePath = Path.Combine(".claude", "skills", "architecture-deepening", "SKILL.md");
    private static readonly string RepoRoot = RepoRootFinder.Find();

    [Fact]
    public void SkillFile_ShouldExist_AndBeValid()
    {
        var path = Path.Combine(RepoRoot, SkillRelativePath);
        Assert.True(File.Exists(path), $"Expected skill file to exist at: {path}");

        var content = File.ReadAllText(path);

        // Assert frontmatter exists at the start and has correct name and description
        var frontmatterRegex = @"^---\r?\nname:\s*Architecture-Deepening\r?\ndescription:[\s\S]*?\r?\n---";
        var isMatch = Regex.IsMatch(
            content,
            frontmatterRegex,
            RegexOptions.None,
            TimeSpan.FromSeconds(RegexTimeoutSeconds));

        Assert.True(
            isMatch,
            "YAML frontmatter is missing, malformed, or has incorrect name/description at the start of the file.");

        // Assert that the key phases are documented
        Assert.True(content.Contains("Phase 1: Explore", StringComparison.Ordinal), "Phase 1 missing");
        Assert.True(content.Contains("Phase 2: Synthesize", StringComparison.Ordinal), "Phase 2 missing");
        Assert.True(content.Contains("Phase 3: Validate one-by-one", StringComparison.Ordinal), "Phase 3 missing");
        Assert.True(content.Contains("Phase 4: Rejection criteria and documentation", StringComparison.Ordinal), "Phase 4 missing");
    }
}
