using Xunit;

namespace Zipper.Tests;

public class ArchitectureNamingTests
{
    [Fact]
    public void Loadfile_Capitalization_ShouldBeCorrect()
    {
        // Get the assembly containing production code
        var productionAssembly = typeof(Zipper.Program).Assembly;

        // Find all types in the production assembly
        var types = productionAssembly.GetTypes();

        // Assert that no types are named LoadfileOnlyGenerator, LoadfileOnlyMode, or LoadfileAuditWriter
        var outdatedTypeNames = new[] { "LoadfileOnlyGenerator", "LoadfileOnlyMode", "LoadfileAuditWriter" };
        foreach (var name in outdatedTypeNames)
        {
            var exists = types.Any(t => t.Name == name || t.FullName == $"Zipper.{name}");
            Assert.False(exists, $"Outdated type '{name}' should not exist. It should be renamed to 'LoadFile...'.");
        }

        // Assert that the renamed versions exist
        var expectedTypeNames = new[] { "LoadFileOnlyGenerator", "LoadFileOnlyMode", "LoadFileAuditWriter" };
        foreach (var name in expectedTypeNames)
        {
            var exists = types.Any(t => t.Name == name);
            Assert.True(exists, $"Renamed type '{name}' should exist in the production codebase.");
        }
    }

    [Fact]
    public void ProductionNativeFilePlan_ShouldBeNamedCorrectly()
    {
        var productionAssembly = typeof(Zipper.Program).Assembly;
        var types = productionAssembly.GetTypes();

        var documentPlanExists = types.Any(t => t.Name == "ProductionDocumentPlan" || t.FullName == "Zipper.ProductionDocumentPlan");
        Assert.False(documentPlanExists, "ProductionDocumentPlan should be renamed to ProductionNativeFilePlan");

        var nativeFilePlanExists = types.Any(t => t.Name == "ProductionNativeFilePlan" || t.FullName == "Zipper.ProductionNativeFilePlan");
        Assert.True(nativeFilePlanExists, "ProductionNativeFilePlan should exist in the production codebase.");
    }
}
