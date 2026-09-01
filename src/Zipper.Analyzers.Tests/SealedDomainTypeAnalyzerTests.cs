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
}
