using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class CrossCuttingRulesTests
{
    private static CliModuleSet CreateModules(Action<CliModuleSet>? configure = null)
    {
        var modules = CliModules.Create();
        configure?.Invoke(modules);
        return modules;
    }

    private static void ApplyValidBase(CliModuleSet modules)
    {
        modules.Output.TryApply("--type", "pdf");
        modules.Output.TryApply("--count", "10");
        modules.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
    }

    private static void AssertValidationError(Action<CliModuleSet> configure, string expectedError)
    {
        var modules = CreateModules(configure);
        var (isValid, _, stderr) = ConsoleOutputCapture.RunWithCapturedOutput(() => CrossCuttingRules.Validate(modules));

        Assert.False(isValid);
        Assert.Contains(expectedError, stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ValidArgs_ReturnsTrue()
    {
        var modules = CreateModules(ApplyValidBase);
        Assert.True(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_MissingType_ReturnsFalse()
    {
        AssertValidationError(m =>
        {
            m.Output.TryApply("--count", "10");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
        }, "Error: --type is required.");
    }

    [Fact]
    public void Validate_LoadfileOnly_WithoutType_ReturnsTrue()
    {
        var modules = CreateModules(m =>
        {
            m.Output.TryApply("--count", "10");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
            m.LoadFile.TryApply("--loadfile-only", null);
        });
        Assert.True(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_ProductionSet_WithoutType_ReturnsTrue()
    {
        var modules = CreateModules(m =>
        {
            m.Output.TryApply("--count", "10");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
            m.Production.TryApply("--production-set", null);
            m.Bates.TryApply("--bates-prefix", "PREFIX");
        });
        Assert.True(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_MissingCount_ReturnsFalse()
    {
        AssertValidationError(m =>
        {
            m.Output.TryApply("--type", "pdf");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
        }, "Error: --count is required.");
    }

    [Fact]
    public void Validate_TargetZipSizeWithoutCount_ReturnsFalse()
    {
        AssertValidationError(m =>
        {
            m.Output.TryApply("--type", "pdf");
            m.Output.TryApply("--target-zip-size", "10MB");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
        }, "Error: --count is required.");
    }

    [Fact]
    public void Validate_ProductionSet_ConflictsWithLoadfileOnly_ReturnsFalse()
    {
        AssertValidationError(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--production-set", null);
            m.LoadFile.TryApply("--loadfile-only", null);
        }, "Error: --production-set conflicts with --loadfile-only.");
    }

    [Fact]
    public void Validate_RedactedProduction_ConflictsWithLoadfileOnly_ReturnsFalse()
    {
        AssertValidationError(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--redacted-production", null);
            m.LoadFile.TryApply("--loadfile-only", null);
        }, "Error: --redacted-production conflicts with --loadfile-only.");
    }

    [Fact]
    public void Validate_ProductionSet_WithoutBatesPrefix_ReturnsFalse()
    {
        AssertValidationError(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--production-set", null);
        }, "Error: --production-set requires --bates-prefix.");
    }

    [Fact]
    public void Validate_Types_WithLoadfileOnly_ReturnsFalse()
    {
        AssertValidationError(m =>
        {
            ApplyValidBase(m);
            m.Output.TryApply("--types", "pdf:70,xls:30");
            m.LoadFile.TryApply("--loadfile-only", null);
        }, "Error: --types is not supported with --loadfile-only.");
    }

    [Fact]
    public void Validate_Types_WithColumnProfile_ReturnsFalse()
    {
        AssertValidationError(m =>
        {
            ApplyValidBase(m);
            m.Output.TryApply("--types", "pdf:70,xls:30");
            m.Metadata.TryApply("--column-profile", "standard");
        }, "Error: --types is not supported with --column-profile. Use --type for profile-driven generation.");
    }

    [Fact]
    public void Validate_ColumnProfile_WithProductionSet_ReturnsFalse()
    {
        AssertValidationError(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--production-set", null);
            m.Bates.TryApply("--bates-prefix", "PREFIX");
            m.Metadata.TryApply("--column-profile", "edrm-standard");
        }, "Error: --column-profile is not supported with --production-set.");
    }
}
