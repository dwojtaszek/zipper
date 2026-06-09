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

    /// <summary>
    /// Walks up the directory tree to locate the root directory containing the build properties file.
    /// </summary>
    /// <param name="startDir">The directory to start the search from.</param>
    /// <returns>The path to the repository root directory.</returns>
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

    /// <summary>
    /// Loads the root <c>Directory.Build.props</c> file as an XML document.
    /// </summary>
    /// <returns>An <see cref="XDocument"/> representing the build properties file.</returns>
    private XDocument LoadBuildProps()
    {
        var path = Path.Combine(RepoRoot, BuildPropsFileName);
        Assert.True(File.Exists(path), $"{BuildPropsFileName} not found at: {path}");
        return XDocument.Load(path);
    }

    /// <summary>
    /// Gets the first XML element with the specified local name.
    /// </summary>
    /// <param name="doc">The XML document to search.</param>
    /// <param name="propertyName">The local name of the property element to find.</param>
    /// <returns>The <see cref="XElement"/> if found; otherwise, <c>null</c>.</returns>
    private static XElement? GetPropertyElement(XDocument doc, string propertyName)
        => doc.Descendants().FirstOrDefault(e => e.Name.LocalName == propertyName);

    /// <summary>
    /// Gets the string value of the specified property element.
    /// </summary>
    /// <param name="doc">The XML document to search.</param>
    /// <param name="propertyName">The local name of the property element to find.</param>
    /// <returns>The trimmed value of the property if found; otherwise, <c>null</c>.</returns>
    private static string? GetPropertyValue(XDocument doc, string propertyName)
        => GetPropertyElement(doc, propertyName)?.Value.Trim();

    /// <summary>
    /// Verifies that the <c>AnalysisMode</c> property is set to <c>latest-Recommended</c>.
    /// </summary>
    [Fact]
    public void DirectoryBuildProps_ShouldHave_AnalysisMode_Set()
    {
        var doc = LoadBuildProps();
        var value = GetPropertyValue(doc, "AnalysisMode");
        Assert.NotNull(value);
        Assert.NotEmpty(value);
        Assert.Equal("latest-Recommended", value);
    }

    /// <summary>
    /// Verifies that the <c>EnableSingleFileAnalyzer</c> property is enabled with the correct TargetFramework condition.
    /// </summary>
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
