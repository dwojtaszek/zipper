using Xunit;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ComparisonModuleTests
{
    private static CliModuleSet CreateModules(Action<CliModuleSet>? configure = null)
    {
        var modules = CliModules.Create();
        configure?.Invoke(modules);
        return modules;
    }

    [Fact]
    public void TryBuild_NoComparisonFlags_ReturnsNullRequest()
    {
        Assert.True(CreateModules().Comparison.TryBuild(out var request));
        Assert.Null(request);
    }

    [Fact]
    public void Parse_CompareManifests_SetsModuleState()
    {
        var modules = CreateModules(m => m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json"));
        Assert.True(modules.Comparison.HasComparisonRequest);
    }

    [Fact]
    public void TryApply_MissingValue_ReturnsFalse()
    {
        Assert.False(CreateModules().Comparison.TryApply("--comparison-mode", null));
        Assert.False(CreateModules().Comparison.TryApply("--compare-production-manifests", null));
        Assert.False(CreateModules().Comparison.TryApply("--comparison-output", null));
    }

    // REQ-177/REQ-178: companion flags without the main flag must fail.
    [Fact]
    public void TryBuild_ComparisonMode_WithoutCompareManifests_ReturnsFalse()
    {
        var modules = CreateModules(m => m.Comparison.TryApply("--comparison-mode", "replacement"));
        Assert.False(modules.Comparison.TryBuild(out _));
    }

    [Fact]
    public void TryBuild_ComparisonOutput_WithoutCompareManifests_ReturnsFalse()
    {
        var modules = CreateModules(m => m.Comparison.TryApply("--comparison-output", "/tmp/report.json"));
        Assert.False(modules.Comparison.TryBuild(out _));
    }

    // REQ-176/REQ-178: compare requires both companions.
    [Fact]
    public void TryBuild_CompareManifests_WithoutComparisonMode_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json");
            m.Comparison.TryApply("--comparison-output", "/tmp/report.json");
        });
        Assert.False(modules.Comparison.TryBuild(out _));
    }

    [Fact]
    public void TryBuild_CompareManifests_WithoutComparisonOutput_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json");
            m.Comparison.TryApply("--comparison-mode", "replacement");
        });
        Assert.False(modules.Comparison.TryBuild(out _));
    }

    [Theory]
    [InlineData("replacement")]
    [InlineData("supplemental")]
    [InlineData("reproduction")]
    [InlineData("REPRODUCTION")]
    public void TryBuild_ValidMode_ReturnsRequest(string mode)
    {
        var modules = CreateModules(m =>
        {
            m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json");
            m.Comparison.TryApply("--comparison-mode", mode);
            m.Comparison.TryApply("--comparison-output", "/tmp/report.json");
        });
        Assert.True(modules.Comparison.TryBuild(out var request));
        Assert.NotNull(request);
        Assert.Equal("/tmp/a.json,/tmp/b.json", request!.ManifestPaths);
        Assert.Equal("/tmp/report.json", request.OutputPath);
    }

    [Fact]
    public void TryBuild_InvalidMode_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json");
            m.Comparison.TryApply("--comparison-mode", "swap");
            m.Comparison.TryApply("--comparison-output", "/tmp/report.json");
        });
        Assert.False(modules.Comparison.TryBuild(out _));
    }

    // REQ-179: valid trio short-circuits without any generation-flag validation.
    [Fact]
    public void TryBuild_ValidTrio_DoesNotTouchOtherModules()
    {
        var modules = CreateModules(m =>
        {
            m.Comparison.TryApply("--compare-production-manifests", "/tmp/a.json,/tmp/b.json");
            m.Comparison.TryApply("--comparison-mode", "replacement");
            m.Comparison.TryApply("--comparison-output", "/tmp/report.json");
        });
        Assert.True(modules.Comparison.TryBuild(out var request));
        Assert.NotNull(request);
    }
}
