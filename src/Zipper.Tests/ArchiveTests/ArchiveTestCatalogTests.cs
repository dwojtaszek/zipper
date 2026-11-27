using Xunit;
using Zipper.ArchiveTests;

namespace Zipper.Tests;

/// <summary>
/// Ticket #1466: recipe factories must keep the policy and bounded deep-path
/// guards separate, so only opt-in names bypass ordinary path validation.
/// </summary>
public class ArchiveTestCatalogTests
{
    [Fact]
    public void File_NormalName_UsesNoBypassGuard()
    {
        var entry = ArchiveTestRecipeEntry.File("file.txt", 1);

        Assert.False(entry.IsPolicyName);
        Assert.False(entry.IsBoundedDeepName);
    }

    [Fact]
    public void Directory_NormalName_UsesNoBypassGuard()
    {
        var entry = ArchiveTestRecipeEntry.Directory("folder/");

        Assert.False(entry.IsPolicyName);
        Assert.False(entry.IsBoundedDeepName);
    }

    [Fact]
    public void DeepPathFile_DeepName_UsesOnlyTheBoundedDeepPathGuard()
    {
        var entry = ArchiveTestRecipeEntry.DeepPathFile("a/b.txt", "text");

        Assert.False(entry.IsPolicyName);
        Assert.True(entry.IsBoundedDeepName);
    }

    [Theory]
    [InlineData("directory-tree-explicit")]
    [InlineData("directory-tree-only")]
    public void TreeRecipes_DeepEmptyBranch_UsesTheBoundedDeepPathGuard(string caseKey)
    {
        var definition = Assert.Single(
            ArchiveTestCatalog.ListSuite(ArchiveTestCatalog.CompatibilitySuite),
            candidate => candidate.CaseKey == caseKey);

        Assert.All(
            definition.Recipe.Entries.Where(entry => entry.Name.StartsWith("café/空/a/", StringComparison.Ordinal)),
            entry => Assert.True(entry.IsBoundedDeepName, entry.Name));
    }

    [Fact]
    public void PolicyFile_HazardName_UsesOnlyThePolicyGuard()
    {
        var entry = ArchiveTestRecipeEntry.PolicyFile("hazard.txt", "text");

        Assert.True(entry.IsPolicyName);
        Assert.False(entry.IsBoundedDeepName);
    }
}
