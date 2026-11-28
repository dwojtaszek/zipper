using Microsoft.CodeAnalysis;
using Xunit;

using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    Zipper.Analyzers.SealedDomainTypeAnalyzer>;

namespace Zipper.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="SealedDomainTypeAnalyzer"/> (ZIP004).
/// </summary>
public class SealedDomainTypeAnalyzerTests
{
    [Fact]
    public async Task SealedDomainType_NoDiagnostic()
    {
        const string source = @"
sealed class MyConfig
{
    public string Value { get; set; }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task UnsealedConfig_ReportsDiagnostic()
    {
        const string source = @"
class {|ZIP004:MyConfig|}
{
    public string Value { get; set; }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task NonDomainType_NoDiagnostic()
    {
        const string source = @"
class SomeRandomClass
{
    public string Value { get; set; }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task RecordClass_NoDiagnostic()
    {
        const string source = @"
record class MyConfig
{
    public string Value { get; set; }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task RecordStruct_NoDiagnostic()
    {
        const string source = @"
record struct MyConfig
{
    public string Value { get; set; }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task AbstractClassBaseConfig_NoDiagnostic()
    {
        const string source = @"
abstract class BaseConfig
{
    public string Value { get; set; }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StaticClassDefaults_NoDiagnostic()
    {
        const string source = @"
static class Defaults
{
    public static string Value { get; set; }
}

static class DefaultConfig
{
    public static string Value { get; set; }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public void SupportedDiagnostics_DefaultSeverityIsWarning()
    {
        var analyzer = new Zipper.Analyzers.SealedDomainTypeAnalyzer();
        var descriptor = Assert.Single(analyzer.SupportedDiagnostics);
        Assert.Equal(Zipper.Analyzers.SealedDomainTypeAnalyzer.DiagnosticId, descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Warning, descriptor.DefaultSeverity);
    }
}
