using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using Zipper.Analyzers;

using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    Zipper.Analyzers.FgrFlatAccessAnalyzer>;

namespace Zipper.Analyzers.Tests;

/// <summary>
/// Fixture-driven tests for <see cref="FgrFlatAccessAnalyzer"/>.
/// Verifies that all 35 flat pass-through properties on FileGenerationRequest
/// are detected, sub-config accesses are not reported, and accesses inside
/// FileGenerationRequest.cs itself are suppressed.
/// </summary>
public class FgrFlatAccessAnalyzerTests
{
    // ---------------------------------------------------------------------------
    // 1. Each of the 35 flat property names produces FGR_FLAT_ACCESS
    // ---------------------------------------------------------------------------

    /// <summary>
    /// For every flat property name in FlatPropertyNames, verify that accessing
    /// that property on a FileGenerationRequest receiver triggers one diagnostic.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetFlatPropertyTestData))]
    public async Task FlatProperty_OnFgr_ReportsDiagnostic(string propertyName)
    {
        var source = $@"
class FileGenerationRequest
{{
    public int {propertyName} {{ get; set; }}
}}

class T
{{
    void M(FileGenerationRequest r)
    {{
        var x = {{|FGR_FLAT_ACCESS:r.{propertyName}|}};
    }}
}}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    /// <summary>MemberData for the Theory above.</summary>
    public static IEnumerable<object[]> GetFlatPropertyTestData() =>
        FgrFlatAccessAnalyzer.FlatPropertyNames
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(name => new object[] { name });

    // ---------------------------------------------------------------------------
    // 2. Exactly 35 names are tracked
    // ---------------------------------------------------------------------------

    [Fact]
    public void FlatPropertyNames_Contains35Names()
    {
        Assert.Equal(35, FgrFlatAccessAnalyzer.FlatPropertyNames.Count);
    }

    // ---------------------------------------------------------------------------
    // 3. Diagnostic metadata
    // ---------------------------------------------------------------------------

    [Fact]
    public void DiagnosticId_IsCorrect()
    {
        Assert.Equal("FGR_FLAT_ACCESS", FgrFlatAccessAnalyzer.DiagnosticId);
    }

    [Fact]
    public void SupportedDiagnostics_ContainsInfoSeverityRule()
    {
        var analyzer = new FgrFlatAccessAnalyzer();
        var descriptor = Assert.Single(analyzer.SupportedDiagnostics);
        Assert.Equal(FgrFlatAccessAnalyzer.DiagnosticId, descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Info, descriptor.DefaultSeverity);
    }

    // ---------------------------------------------------------------------------
    // 4. Subconfig access does not trigger the diagnostic
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task SubconfigAccess_NoDiagnostic()
    {
        const string source = @"
class OutputConfig
{
    public string OutputPath { get; set; }
}

class FileGenerationRequest
{
    public OutputConfig Output { get; set; }
}

class T
{
    void M(FileGenerationRequest r)
    {
        var x = r.Output.OutputPath;
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    // ---------------------------------------------------------------------------
    // 5. Non-FGR type with same property name — no diagnostic
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task FlatPropertyNameOnDifferentType_NoDiagnostic()
    {
        const string source = @"
class SomeOtherClass
{
    public int OutputPath { get; set; }
}

class T
{
    void M(SomeOtherClass r)
    {
        var x = r.OutputPath;
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    // ---------------------------------------------------------------------------
    // 6. Access inside a file named FileGenerationRequest.cs — suppressed
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task AccessInFgrFile_IsSuppressed()
    {
        // Place the code in a file whose path ends with FileGenerationRequest.cs.
        // The analyzer suppresses all diagnostics from that file.
        const string source = @"
class FileGenerationRequest
{
    public int OutputPath { get; set; }

    public void SetViaOther(FileGenerationRequest other)
    {
        // Cross-instance flat access inside the file must be suppressed.
        var x = other.OutputPath;
    }
}
";
        var test = new CSharpAnalyzerTest<FgrFlatAccessAnalyzer, DefaultVerifier>
        {
            TestState =
            {
                Sources = { ("FileGenerationRequest.cs", source) },
            },
        };

        // If the file-path suppression is working, zero diagnostics are expected.
        await test.RunAsync();
    }

    // ---------------------------------------------------------------------------
    // 7. Verify diagnostic message arguments for specific cases
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task OutputPath_ReportsDiagnosticWithOutputSubConfig()
    {
        const string source = @"
class FileGenerationRequest
{
    public int OutputPath { get; set; }
}

class T
{
    void M(FileGenerationRequest r)
    {
        var x = {|FGR_FLAT_ACCESS:r.OutputPath|};
    }
}
";
        var expected = VerifyCS.Diagnostic(FgrFlatAccessAnalyzer.DiagnosticId)
            .WithArguments("OutputPath", "Output");

        await VerifyCS.VerifyAnalyzerAsync(source, expected);
    }

    [Fact]
    public async Task ChaosScenario_ReportsDiagnosticWithChaosSubConfig()
    {
        const string source = @"
class FileGenerationRequest
{
    public string ChaosScenario { get; set; }
}

class T
{
    void M(FileGenerationRequest r)
    {
        var x = {|FGR_FLAT_ACCESS:r.ChaosScenario|};
    }
}
";
        var expected = VerifyCS.Diagnostic(FgrFlatAccessAnalyzer.DiagnosticId)
            .WithArguments("ChaosScenario", "Chaos");

        await VerifyCS.VerifyAnalyzerAsync(source, expected);
    }
}
