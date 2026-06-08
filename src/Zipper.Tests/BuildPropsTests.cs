using System.Xml.Linq;
using Xunit;

namespace Zipper;

/// <summary>
/// Tests that verify Directory.Build.props contains the required build property settings.
/// These tests enforce the build configuration contract so that settings cannot be
/// accidentally removed without a failing test.
/// </summary>
public class BuildPropsTests
{
    private const string BuildPropsFileName = "Directory.Build.props";

    private static readonly string RepoRoot = FindRepoRoot(AppContext.BaseDirectory);

    private static string FindRepoRoot(string startDir)
    {
        var dir = new DirectoryInfo(startDir);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, BuildPropsFileName)))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find repo root ({BuildPropsFileName}) walking up from: {startDir}");
    }

    private XDocument LoadBuildProps()
    {
        var path = Path.Combine(RepoRoot, BuildPropsFileName);
        Assert.True(File.Exists(path), $"{BuildPropsFileName} not found at: {path}");
        return XDocument.Load(path);
    }

    private static XElement? GetPropertyElement(XDocument doc, string propertyName)
        => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == propertyName);

    private static string? GetPropertyValue(XDocument doc, string propertyName)
        => GetPropertyElement(doc, propertyName)?.Value.Trim();

    [Fact]
    public void DirectoryBuildProps_ShouldHave_AnalysisMode_Set()
    {
        var doc = LoadBuildProps();
        var value = GetPropertyValue(doc, "AnalysisMode");
        Assert.NotNull(value);
        Assert.NotEmpty(value);
        Assert.Equal("latest-Recommended", value);
    }

    [Fact]
    public void DirectoryBuildProps_ShouldHave_EnableSingleFileAnalyzer_True()
    {
        var doc = LoadBuildProps();

        // Exactly one element: no accidental unconditional duplicate that would cause NETSDK1211
        var elements = doc.Descendants()
            .Where(e => e.Name.LocalName == "EnableSingleFileAnalyzer")
            .ToList();
        var element = Assert.Single(elements);
        Assert.Equal("true", element.Value.Trim(), ignoreCase: true);

        // The Condition must use IsTargetFrameworkCompatible (not a simple equality check like
        // '$(TargetFramework)' == 'net8.0') so that net9.0+ also receives the analyzer.
        // netstandard2.0 (Zipper.Analyzers) evaluates to false and is correctly excluded.
        var condition = element.Attribute("Condition")?.Value;
        Assert.NotNull(condition);
        Assert.Contains("IsTargetFrameworkCompatible", condition, StringComparison.Ordinal);
        Assert.Contains("net8.0", condition, StringComparison.Ordinal);
    }
}
