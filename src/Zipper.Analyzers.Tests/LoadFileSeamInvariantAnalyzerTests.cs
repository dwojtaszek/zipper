using Xunit;

using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    Zipper.Analyzers.LoadFileSeamInvariantAnalyzer>;

namespace Zipper.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="LoadFileSeamInvariantAnalyzer"/> (ZIP001).
/// </summary>
public class LoadFileSeamInvariantAnalyzerTests
{
    [Fact]
    public async Task DirectStreamWriteInComposer_ReportsDiagnostic()
    {
        const string source = @"
class MyComposer
{
    public void WriteSomething(System.IO.Stream stream)
    {
        {|ZIP001:stream.Write|}(new byte[] { 1, 2, 3 }, 0, 3);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task DirectTextWriterWriteInEmitter_ReportsDiagnostic()
    {
        const string source = @"
class MyEmitter
{
    public void Emit(System.IO.TextWriter writer)
    {
        {|ZIP001:writer.Write|}(""hello"");
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task WriteThroughSerializer_NoDiagnostic()
    {
        const string source = @"
interface ILoadFileSerializer
{
    void RenderRecord(object o);
}

class MyComposer
{
    public void Write(ILoadFileSerializer serializer, System.IO.Stream stream)
    {
        serializer.RenderRecord(null);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StreamWriteInNonComposer_NoDiagnostic()
    {
        const string source = @"
class SomeHelper
{
    public void Write(System.IO.Stream stream)
    {
        stream.Write(new byte[] { 1 }, 0, 1);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task DirectBinaryWriterWriteInSerializer_ReportsDiagnostic()
    {
        const string source = @"
class MySerializer
{
    public void Write(System.IO.BinaryWriter writer)
    {
        {|ZIP001:writer.Write|}(""abc"");
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }
}
