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

    [Fact]
    public async Task DirectTextWriterWriteLineInComposer_ReportsDiagnostic()
    {
        const string source = @"
class MyComposer
{
    public void WriteSomething(System.IO.TextWriter writer)
    {
        {|ZIP001:writer.WriteLine|}(""hello"");
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task DirectStreamWriteAsyncInComposer_ReportsDiagnostic()
    {
        const string source = @"
using System.Threading.Tasks;

class MyComposer
{
    public async Task WriteSomethingAsync(System.IO.Stream stream)
    {
        await {|ZIP001:stream.WriteAsync|}(new byte[] { 1, 2, 3 }, 0, 3);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task DirectTextWriterWriteLineAsyncInEmitter_ReportsDiagnostic()
    {
        const string source = @"
using System.Threading.Tasks;

class MyEmitter
{
    public async Task EmitAsync(System.IO.TextWriter writer)
    {
        await {|ZIP001:writer.WriteLineAsync|}(""hello"");
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }
}
