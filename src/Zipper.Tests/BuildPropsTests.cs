using System.Xml.Linq;
using Xunit;

namespace Zipper.Tests;

/// <summary>
/// Tests that verify Directory.Build.props contains the required build property settings.
/// These tests enforce the build configuration contract so that settings cannot be
/// accidentally removed without a failing test.
/// </summary>
public class BuildPropsTests
{
    private const string BuildPropsFileName = "Directory.Build.props";

    private static readonly string RepoRoot = RepoRootFinder.Find();

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
    private static IEnumerable<XElement> GetPropertyElements(XDocument doc, string propertyName)
        => doc.Descendants().Where(e => string.Equals(e.Name.LocalName, propertyName, StringComparison.Ordinal));

    private static XElement? GetPropertyElement(XDocument doc, string propertyName)
        => GetPropertyElements(doc, propertyName).FirstOrDefault();

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
        var elements = GetPropertyElements(doc, "EnableSingleFileAnalyzer").ToList();
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

    [Fact]
    public void DirectoryBuildProps_ShouldHave_InvariantGlobalization_True()
    {
        var doc = LoadBuildProps();
        var elements = GetPropertyElements(doc, "InvariantGlobalization").ToList();
        var element = Assert.Single(elements);
        Assert.Equal("true", element.Value.Trim(), ignoreCase: true);
    }

    [Theory]
    [InlineData("EnableNETAnalyzers", "true")]
    [InlineData("EnforceCodeStyleInBuild", "true")]
    [InlineData("TreatWarningsAsErrors", "true")]
    [InlineData("AnalysisModeSecurity", "All")]
    [InlineData("Deterministic", "true")]
    public void DirectoryBuildProps_ShouldHave_Property(string propertyName, string expectedValue)
    {
        var doc = LoadBuildProps();
        var elements = GetPropertyElements(doc, propertyName).ToList();
        var element = Assert.Single(elements);
        Assert.Equal(expectedValue, element.Value.Trim(), ignoreCase: true);
    }

    [Fact]
    public void DirectoryBuildProps_ShouldHave_ContinuousIntegrationBuild_Conditional()
    {
        var doc = LoadBuildProps();
        var elements = GetPropertyElements(doc, "ContinuousIntegrationBuild").ToList();
        var element = Assert.Single(elements);
        Assert.Equal("true", element.Value.Trim(), ignoreCase: true);

        var condition = element.Attribute("Condition")?.Value;
        Assert.NotNull(condition);
        Assert.Contains("CI", condition, StringComparison.Ordinal);
        Assert.Contains("GITHUB_ACTIONS", condition, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectoryBuildProps_ShouldHave_RequiredAnalyzers()
    {
        var doc = LoadBuildProps();
        var packageReferences = GetPropertyElements(doc, "PackageReference")
            .Select(e => e.Attribute("Include")?.Value)
            .Where(v => v is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains("Roslynator.Analyzers", packageReferences);
        Assert.Contains("Roslynator.Formatting.Analyzers", packageReferences);
        Assert.Contains("Roslynator.CodeAnalysis.Analyzers", packageReferences);
        Assert.Contains("Meziantou.Analyzer", packageReferences);
        Assert.Contains("Microsoft.CodeAnalysis.BannedApiAnalyzers", packageReferences);
    }

    [Fact]
    public void DirectoryBuildProps_ShouldHave_BannedSymbols()
    {
        var doc = LoadBuildProps();
        var additionalFiles = GetPropertyElements(doc, "AdditionalFiles")
            .Select(e => e.Attribute("Include")?.Value)
            .ToList();

        Assert.Contains("$(MSBuildThisFileDirectory)BannedSymbols.txt", additionalFiles);
    }
}
