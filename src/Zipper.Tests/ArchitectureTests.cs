using NetArchTest.Rules;
using Xunit;
using Zipper.Cli.Modules;
using Zipper.Emails;
using Zipper.LoadFiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests;

/// <summary>
/// Architecture tests enforced via NetArchTest against the production assembly.
/// Guards documented boundaries in docs/architecture.md and ADRs 0004–0008.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly System.Reflection.Assembly ProductionAssembly = typeof(Program).Assembly;

    private static void AssertArch(TestResult result, string ruleDescription)
    {
        var failing = result.FailingTypes is { Count: > 0 }
            ? string.Join(", ", result.FailingTypes.Select(t => t.FullName ?? t.Name))
            : "(none)";
        Assert.True(result.IsSuccessful, $"{ruleDescription}. Failing types: {failing}");
    }

    private static readonly string[] ForbiddenIoTypes =
    [
        "System.IO.Stream",
        "System.IO.File",
        "System.IO.FileStream",
        "System.IO.StreamWriter",
        "System.IO.TextWriter",
        "System.IO.BinaryWriter",
        "System.IO.Directory",
    ];

    // --- Critical Rule 5 / ADR-0007: Load File Composition Seam ---

    [Fact]
    public void LoadFileComposers_ShouldNotDependOnIoOrEmitterOrChaos()
    {
        var forbidden = ForbiddenIoTypes
            .Concat([typeof(LoadFileEmitter).FullName!, typeof(ChaosEngine).FullName!])
            .ToArray();

        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespace("Zipper.LoadFiles")
            .And()
            .ImplementInterface<ILoadFileComposer>()
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        AssertArch(result, "Load File composers own column selection only and must not perform I/O or depend on LoadFileEmitter/ChaosEngine");
    }

    [Fact]
    public void LoadFileSerializers_ShouldNotDependOnIoOrEmitterOrChaos()
    {
        var forbidden = ForbiddenIoTypes
            .Concat([typeof(LoadFileEmitter).FullName!, typeof(ChaosEngine).FullName!])
            .ToArray();

        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespace("Zipper.LoadFiles")
            .And()
            .ImplementInterface<ILoadFileSerializer>()
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        AssertArch(result, "Load File serializers are pure line renderers and must not perform I/O or depend on LoadFileEmitter/ChaosEngine");
    }

    [Fact]
    public void LoadFileWriters_ShouldResideInLoadFilesNamespace_AndOnlyXmlIsCarveOut()
    {
        var inNamespace = Types.InAssembly(ProductionAssembly)
            .That()
            .ImplementInterface<ILoadFileWriter>()
            .Should()
            .ResideInNamespace("Zipper.LoadFiles")
            .And()
            .BeSealed()
            .GetResult();

        AssertArch(inNamespace, "All ILoadFileWriter implementations must reside in Zipper.LoadFiles and be sealed");

        var nonComposing = Types.InAssembly(ProductionAssembly)
            .That()
            .ImplementInterface<ILoadFileWriter>()
            .And()
            .DoNotHaveNameEndingWith("ComposingWriter")
            .GetTypes()
            .Select(t => t.Name)
            .ToArray();

        Assert.Equal(new[] { "XmlLoadFileWriter" }, nonComposing);
    }

    [Fact]
    public void LoadFileComposersAndSerializers_ShouldBeSealed()
    {
        var composers = Types.InAssembly(ProductionAssembly)
            .That()
            .ImplementInterface<ILoadFileComposer>()
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .GetResult();

        var serializers = Types.InAssembly(ProductionAssembly)
            .That()
            .ImplementInterface<ILoadFileSerializer>()
            .And()
            .AreNotAbstract()
            .Should()
            .BeSealed()
            .GetResult();

        AssertArch(composers, "All concrete ILoadFileComposer implementations must be sealed");
        AssertArch(serializers, "All concrete ILoadFileSerializer implementations must be sealed");
    }

    // --- ADR-0006 & ADR-0008: Three Generation Modes ---

    [Fact]
    public void GenerationModes_ShouldBeExactlyThreeSealedModes()
    {
        var modes = Types.InAssembly(ProductionAssembly)
            .That()
            .ImplementInterface<IGenerationMode>()
            .GetTypes()
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "LoadFileOnlyMode", "ProductionSetMode", "StandardMode" }, modes);

        var sealedResult = Types.InAssembly(ProductionAssembly)
            .That()
            .ImplementInterface<IGenerationMode>()
            .Should()
            .BeSealed()
            .GetResult();

        AssertArch(sealedResult, "All IGenerationMode implementations must be sealed");
    }

    // --- ADR-0008: Archive Test Isolation ---

    [Fact]
    public void ArchiveTestsNamespace_ShouldNotDependOnGenerationPipelineNamespaces()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespace("Zipper.ArchiveTests")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Zipper.LoadFiles",
                "Zipper.Validation",
                "Zipper.ManifestComparison",
                "Zipper.Profiles",
                "Zipper.Emails",
                "Zipper.SourceInput",
                typeof(FileGenerationRequest).FullName!)
            .GetResult();

        AssertArch(result, "Zipper.ArchiveTests is a standalone pre-pipeline short-circuit (ADR-0008)");
    }

    [Fact]
    public void DomainAndGenerationNamespaces_ShouldNotDependOnArchiveTests()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespaceMatching(@"^Zipper\.(Config|LoadFiles|Validation|Profiles|Emails|SourceInput|ManifestComparison|Utils)(\.|$)")
            .ShouldNot()
            .HaveDependencyOnAny("Zipper.ArchiveTests")
            .GetResult();

        AssertArch(result, "Generation and domain namespaces must not depend on Zipper.ArchiveTests");
    }

    // --- CLI Layer Boundary ---

    [Fact]
    public void NonCliNamespaces_ShouldNotDependOnCliLayer()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespaceMatching(@"^Zipper\.(Config|LoadFiles|Validation|Profiles|Emails|SourceInput|ManifestComparison|ArchiveTests|Utils)(\.|$)")
            .And()
            .DoNotHaveName("ArchiveTestCliWorkflow") // ADR-0008 pre-pipeline CLI adapter
            .ShouldNot()
            .HaveDependencyOnAny("Zipper.Cli", "Zipper.Cli.Modules")
            .GetResult();

        AssertArch(result, "Domain and pipeline namespaces must not depend on Zipper.Cli or Zipper.Cli.Modules");
    }

    [Fact]
    public void CliModules_ShouldResideInCliModulesNamespaceAndBeSealed()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .Inherit<CliModule>()
            .Should()
            .ResideInNamespace("Zipper.Cli.Modules")
            .And()
            .HaveNameEndingWith("Module")
            .And()
            .BeSealed()
            .GetResult();

        AssertArch(result, "All CliModule implementations must reside in Zipper.Cli.Modules, end with Module, and be sealed");
    }

    // --- Config, Utils, and Domain Layer Isolation ---

    [Fact]
    public void ConfigNamespace_ShouldRemainPure()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespace("Zipper.Config")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Zipper.Cli",
                "Zipper.LoadFiles",
                "Zipper.Validation",
                "Zipper.ArchiveTests",
                "Zipper.ManifestComparison",
                "Zipper.SourceInput")
            .GetResult();

        AssertArch(result, "Zipper.Config must remain pure configuration models without upward pipeline dependencies");
    }

    [Fact]
    public void UtilsNamespace_ShouldNotDependOnDomainOrPipelineNamespaces()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespace("Zipper.Utils")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Zipper.Cli",
                "Zipper.LoadFiles",
                "Zipper.Validation",
                "Zipper.ArchiveTests",
                "Zipper.ManifestComparison",
                "Zipper.Emails",
                "Zipper.Profiles",
                "Zipper.SourceInput")
            .GetResult();

        AssertArch(result, "Zipper.Utils must not depend on domain or pipeline namespaces");
    }

    [Fact]
    public void EmailsNamespace_ShouldNotDependOnPipelineOrCliNamespaces()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespace("Zipper.Emails")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Zipper.Cli",
                "Zipper.LoadFiles",
                "Zipper.Validation",
                "Zipper.ArchiveTests",
                "Zipper.ManifestComparison")
            .GetResult();

        AssertArch(result, "Zipper.Emails must stay decoupled from CLI, Load Files, Validation, Archive Tests, and Manifest Comparison");
    }

    [Fact]
    public void SourceInputNamespace_ShouldNotDependOnPipelineOrCliNamespaces()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespace("Zipper.SourceInput")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Zipper.Cli",
                "Zipper.LoadFiles",
                "Zipper.Validation",
                "Zipper.ArchiveTests",
                "Zipper.ManifestComparison")
            .GetResult();

        AssertArch(result, "Zipper.SourceInput must stay decoupled from CLI, Load Files, Validation, Archive Tests, and Manifest Comparison");
    }

    [Fact]
    public void ProfilesNamespace_ShouldNotDependOnPipelineOrCliNamespaces()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespace("Zipper.Profiles")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Zipper.Cli",
                "Zipper.LoadFiles",
                "Zipper.Validation",
                "Zipper.ArchiveTests",
                "Zipper.ManifestComparison")
            .GetResult();

        AssertArch(result, "Zipper.Profiles must stay decoupled from CLI, Load Files, Validation, Archive Tests, and Manifest Comparison");
    }

    [Fact]
    public void ValidationNamespace_ShouldNotDependOnCliArchiveTestsOrManifestComparison()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespace("Zipper.Validation")
            .ShouldNot()
            .HaveDependencyOnAny("Zipper.Cli", "Zipper.ArchiveTests", "Zipper.ManifestComparison")
            .GetResult();

        AssertArch(result, "Zipper.Validation must not depend on CLI, ArchiveTests, or ManifestComparison");
    }

    [Fact]
    public void ManifestComparisonNamespace_ShouldNotDependOnGenerationNamespaces()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .ResideInNamespace("Zipper.ManifestComparison")
            .ShouldNot()
            .HaveDependencyOnAny(
                "Zipper.Cli",
                "Zipper.LoadFiles",
                "Zipper.ArchiveTests",
                "Zipper.Emails",
                "Zipper.Profiles")
            .GetResult();

        AssertArch(result, "Zipper.ManifestComparison must stay decoupled from generation and CLI namespaces");
    }

    // --- ADR-0004 & ADR-0005: Generators & Email Aggregate ---

    [Fact]
    public void FileAndColumnGenerators_ShouldBeSealed()
    {
        var fileGenerators = Types.InAssembly(ProductionAssembly)
            .That()
            .ImplementInterface<IFileGenerator>()
            .Should()
            .BeSealed()
            .GetResult();

        var columnGenerators = Types.InAssembly(ProductionAssembly)
            .That()
            .ImplementInterface<IColumnValueGenerator>()
            .Should()
            .BeSealed()
            .GetResult();

        AssertArch(fileGenerators, "All IFileGenerator implementations must be sealed");
        AssertArch(columnGenerators, "All IColumnValueGenerator implementations must be sealed");
    }

    [Fact]
    public void EmailAggregate_ShouldLiveInEmailsNamespace()
    {
        Assert.Equal("Zipper.Emails", typeof(Email).Namespace);
        Assert.Equal("Zipper.Emails", typeof(EmailFactory).Namespace);
        Assert.Equal("Zipper.Emails", typeof(EmailSerializer).Namespace);
    }

    // --- General Production Hygiene ---

    [Fact]
    public void Interfaces_ShouldStartWithI()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .That()
            .AreInterfaces()
            .Should()
            .HaveNameStartingWith("I")
            .GetResult();

        AssertArch(result, "All interfaces in Zipper must start with 'I'");
    }

    [Fact]
    public void ProductionAssembly_ShouldNotDependOnTestFrameworks()
    {
        var result = Types.InAssembly(ProductionAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Xunit", "FsCheck", "NetArchTest")
            .GetResult();

        AssertArch(result, "Production assembly must never depend on test libraries");
    }
}
