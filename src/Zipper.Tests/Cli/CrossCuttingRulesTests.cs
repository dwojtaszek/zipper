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

    [Fact]
    public void Validate_ValidArgs_ReturnsTrue()
    {
        var modules = CreateModules(ApplyValidBase);
        Assert.True(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_MissingType_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Output.TryApply("--count", "10");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
        });
        Assert.False(CrossCuttingRules.Validate(modules));
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
        var modules = CreateModules(m =>
        {
            m.Output.TryApply("--type", "pdf");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_TargetZipSizeWithoutCount_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            m.Output.TryApply("--type", "pdf");
            m.Output.TryApply("--target-zip-size", "10MB");
            m.Output.TryApply("--output-path", Directory.GetCurrentDirectory());
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_ProductionSet_ConflictsWithLoadfileOnly_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--production-set", null);
            m.LoadFile.TryApply("--loadfile-only", null);
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_RedactedProduction_ConflictsWithLoadfileOnly_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--redacted-production", null);
            m.LoadFile.TryApply("--loadfile-only", null);
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_ProductionSet_WithoutBatesPrefix_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--production-set", null);
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_Types_WithLoadfileOnly_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Output.TryApply("--types", "pdf:70,xls:30");
            m.LoadFile.TryApply("--loadfile-only", null);
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_Types_WithColumnProfile_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Output.TryApply("--types", "pdf:70,xls:30");
            m.Metadata.TryApply("--column-profile", "standard");
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }

    [Fact]
    public void Validate_ColumnProfile_WithProductionSet_ReturnsFalse()
    {
        var modules = CreateModules(m =>
        {
            ApplyValidBase(m);
            m.Production.TryApply("--production-set", null);
            m.Metadata.TryApply("--column-profile", "edrm-standard");
        });
        Assert.False(CrossCuttingRules.Validate(modules));
    }
}
