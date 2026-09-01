using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    Zipper.Analyzers.DeterministicFormattableAnalyzer>;

namespace Zipper.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="DeterministicFormattableAnalyzer"/> (ZIP002).
/// </summary>
public class DeterministicFormattableAnalyzerTests
{
    [Fact]
    public async Task StringFormatInFormatter_ReportsDiagnostic()
    {
        const string source = @"
class MyFormatter
{
    public string FormatSomething(string value)
    {
        return {|ZIP002:string.Format(""{0}"", value)|};
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StringFormatInNonFormatter_NoDiagnostic()
    {
        const string source = @"
class SomeHelper
{
    public string Format(string value)
    {
        return string.Format(""{0}"", value);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task InterpolatedString_NoDiagnostic()
    {
        const string source = @"
class MyFormatter
{
    public string FormatSomething(string value)
    {
        return $""{value}"";
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ToStringOnValueTypeInFormatter_ReportsDiagnostic()
    {
        const string source = @"
class MyFormatter
{
    public string Format(int value)
    {
        return {|ZIP002:value.ToString()|};
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ToStringOnString_NoDiagnostic()
    {
        const string source = @"
class MyFormatter
{
    public string Format(string value)
    {
        return value.ToString();
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }
}
