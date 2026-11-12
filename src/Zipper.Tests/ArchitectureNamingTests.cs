using System.Collections.Frozen;
using System.Reflection;
using FsCheck.Xunit;
using Xunit;

namespace Zipper.Tests;

public class ArchitectureNamingTests
{
    private static readonly FrozenSet<string> LegacyPropertyTestNames = new[]
    {
        "MimeRealismOptions_Parse_NonRfcTokens_AlwaysImpliesEncodedWords",
        "DatStandardComposer_Compose_HeaderColumnCountShouldAlwaysEqualEveryRecordValueCount",
        "LoadFileEmitter_Emit_ByteCountAndLineCountShouldMatchPreambleAndEolContract",
        "PostGenerationValidator_Validate_CleanOutputShouldPassAndInjectedFaultShouldFail",
        "ChaosExpectationValidator_Validate_GeneratedSidecarShouldMatchProducedOutput",
        "ChaosExpectationValidator_Validate_DriftedRecordCountShouldReportCountError",
        "ChaosExpectationValidator_Validate_WrongTypedFieldShouldReportErrorWithoutThrowing",
        "ChaosExpectationValidator_Validate_DamagedHeaderShouldStillReportDriftedRecordCount",
        "ChaosExpectationValidator_Validate_GeneratedXmlSidecarShouldMatchProducedDocuments",
        "ChaosExpectationValidator_Validate_DriftedXmlDocumentCountShouldReportError",
    }.ToFrozenSet(StringComparer.Ordinal);

    [Fact]
    public void PropertyTests_MethodNames_ShouldFollowMethodScenarioShouldExpectedConvention()
    {
        var testAssembly = typeof(ArchitectureNamingTests).Assembly;
        var violations = new List<string>();

        foreach (var type in testAssembly.GetTypes())
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.GetCustomAttribute<PropertyAttribute>() is null
                    || LegacyPropertyTestNames.Contains(method.Name))
                {
                    continue;
                }

                if (!method.Name.Contains("_Should", StringComparison.Ordinal))
                {
                    violations.Add($"{type.Name}.{method.Name} must follow '{{Method}}_{{Scenario}}_Should{{Expected}}'.");
                }
            }
        }

        Assert.Empty(violations);
    }

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
