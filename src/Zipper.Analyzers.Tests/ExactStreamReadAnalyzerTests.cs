using Xunit;

using VerifyCS = Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
    Zipper.Analyzers.ExactStreamReadAnalyzer>;

namespace Zipper.Analyzers.Tests;

/// <summary>
/// Tests for <see cref="ExactStreamReadAnalyzer"/> (ZIP003).
/// </summary>
public class ExactStreamReadAnalyzerTests
{
    [Fact]
    public async Task StreamReadInSink_ReportsDiagnostic()
    {
        const string source = @"
using System.IO;

class MySink
{
    public void ReadFrom(Stream stream)
    {
        var buffer = new byte[1024];
        {|ZIP003:stream.Read|}(buffer, 0, buffer.Length);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StreamReadAsyncInWriter_ReportsDiagnostic()
    {
        const string source = @"
using System.IO;
using System.Threading.Tasks;

class MyWriter
{
    public async Task ReadFromAsync(Stream stream)
    {
        var buffer = new byte[1024];
        await {|ZIP003:stream.ReadAsync|}(buffer, 0, buffer.Length);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StreamReadInNonSink_NoDiagnostic()
    {
        const string source = @"
using System.IO;

class SomeHelper
{
    public void ReadFrom(Stream stream)
    {
        var buffer = new byte[1024];
        stream.Read(buffer, 0, buffer.Length);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StreamReadExactlyInSink_NoDiagnostic()
    {
        const string source = @"
using System.IO;

public static class StreamExtensions
{
    public static void ReadExactly(this Stream stream, byte[] buffer, int offset, int count) { }
}

class MySink
{
    public void ReadFrom(Stream stream)
    {
        var buffer = new byte[1024];
        stream.ReadExactly(buffer, 0, buffer.Length);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StreamReadAtLeastInSink_NoDiagnostic()
    {
        const string source = @"
using System.IO;

public static class StreamExtensions
{
    public static int ReadAtLeast(this Stream stream, byte[] buffer, int minBytes) => minBytes;
}

class MySink
{
    public void ReadFrom(Stream stream)
    {
        var buffer = new byte[1024];
        stream.ReadAtLeast(buffer, buffer.Length);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }

    [Fact]
    public async Task StreamReadExactlyAsyncInWriter_NoDiagnostic()
    {
        const string source = @"
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public static class StreamExtensions
{
    public static ValueTask ReadExactlyAsync(this Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken = default) => default;
}

class MyWriter
{
    public async Task ReadFromAsync(Stream stream)
    {
        var buffer = new byte[1024];
        await stream.ReadExactlyAsync(buffer, 0, buffer.Length);
    }
}
";
        await VerifyCS.VerifyAnalyzerAsync(source);
    }
}
