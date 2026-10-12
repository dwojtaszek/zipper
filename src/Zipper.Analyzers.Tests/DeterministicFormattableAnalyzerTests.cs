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

    [Fact]
    public async Task StringFormatWithInvariantCulture_NoDiagnostic()
    {
        const string source = @"
using System.Globalization;

class MyFormatter
{
    public string FormatSomething(string value)
    {
        return string.Format(CultureInfo.InvariantCulture, ""{0}"", value);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task ToStringWithInvariantCulture_NoDiagnostic()
    {
        const string source = @"
using System.Globalization;

class MyFormatter
{
    public string Format(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StringFormatWithNullProviderInFormatter_ReportsDiagnostic()
    {
        const string source = @"
using System;

class MyFormatter
{
    public string FormatSomething(string value)
    {
        return {|ZIP002:string.Format((IFormatProvider)null, ""{0}"", value)|};
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StringFormatInLoadFileComposer_ReportsDiagnostic()
    {
        const string source = @"
class LoadFileDatComposer
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
    public async Task StringFormatWithReorderedNamedArgumentsNullProvider_ReportsDiagnostic()
    {
        const string source = @"
using System;

class MyFormatter
{
    public string FormatSomething(string value)
    {
        return {|ZIP002:string.Format(format: ""{0}"", provider: null, arg0: value)|};
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StringFormatWithReorderedNamedArgumentsInvariantCulture_NoDiagnostic()
    {
        const string source = @"
using System.Globalization;

class MyFormatter
{
    public string FormatSomething(string value)
    {
        return string.Format(format: ""{0}"", provider: CultureInfo.InvariantCulture, arg0: value);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StringFormatWithCurrentCultureInFormatter_ReportsDiagnostic()
    {
        const string source = @"
using System.Globalization;

class MyFormatter
{
    public string FormatSomething(string value)
    {
        return {|ZIP002:string.Format(CultureInfo.CurrentCulture, ""{0}"", value)|};
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StringFormatWithArbitraryVariableProvider_ReportsDiagnostic()
    {
        const string source = @"
using System;

class MyFormatter
{
    public string FormatSomething(IFormatProvider provider, string value)
    {
        return {|ZIP002:string.Format(provider, ""{0}"", value)|};
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }
}

