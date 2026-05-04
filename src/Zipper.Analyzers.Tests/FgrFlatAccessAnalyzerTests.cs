using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

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

    /// <summary>
    /// Exactly 35 flat pass-through property names are tracked.
    /// </summary>
    [Fact]
    public void FlatPropertyNames_Contains35Names()
    {
        Assert.Equal(35, FgrFlatAccessAnalyzer.FlatPropertyNames.Count);
    }

    /// <summary>
    /// The diagnostic ID constant matches the documented rule ID.
    /// </summary>
    [Fact]
    public void DiagnosticId_IsCorrect()
    {
        Assert.Equal("FGR_FLAT_ACCESS", FgrFlatAccessAnalyzer.DiagnosticId);
    }

    /// <summary>
    /// The rule ships with Info severity so it does not fail the build.
    /// </summary>
    [Fact]
    public void SupportedDiagnostics_ContainsInfoSeverityRule()
    {
        var analyzer = new FgrFlatAccessAnalyzer();
        var descriptor = Assert.Single(analyzer.SupportedDiagnostics);
        Assert.Equal(FgrFlatAccessAnalyzer.DiagnosticId, descriptor.Id);
        Assert.Equal(DiagnosticSeverity.Info, descriptor.DefaultSeverity);
    }

    /// <summary>
    /// Accessing a property via a sub-config (e.g. request.Output.OutputPath) does
    /// not trigger the diagnostic.
    /// </summary>
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

    /// <summary>
    /// A flat property name on a different type is not flagged.
    /// </summary>
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

    /// <summary>
    /// Code in a file named FileGenerationRequest.cs is fully suppressed.
    /// </summary>
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

    /// <summary>
    /// OutputPath maps to the Output sub-config in the diagnostic message.
    /// </summary>
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
        var x = r.OutputPath;
    }
}
";
        var expected = VerifyCS.Diagnostic(FgrFlatAccessAnalyzer.DiagnosticId)
            .WithLocation(11, 17)
            .WithArguments("OutputPath", "Output");

        await VerifyCS.VerifyAnalyzerAsync(source, expected);
    }

    /// <summary>
    /// ChaosScenario maps to the Chaos sub-config in the diagnostic message.
    /// </summary>
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
        var x = r.ChaosScenario;
    }
}
";
        var expected = VerifyCS.Diagnostic(FgrFlatAccessAnalyzer.DiagnosticId)
            .WithLocation(11, 17)
            .WithArguments("ChaosScenario", "Chaos");

        await VerifyCS.VerifyAnalyzerAsync(source, expected);
    }
}
