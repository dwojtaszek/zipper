using System.Text;
using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles;

public class TextOutputPolicyTests
{
    private static FileGenerationRequest Req(LoadFileFormat format, string? encoding = null, bool isExplicit = false, string eol = "CRLF")
    {
        return new FileGenerationRequest
        {
            LoadFile = new LoadFileConfig
            {
                LoadFileFormat = format,
                Encoding = encoding ?? "UTF-8",
                IsEncodingExplicit = isExplicit
            },
            Delimiters = new DelimiterConfig
            {
                EndOfLine = eol
            }
        };
    }

    [Fact]
    public void Opt_Standard_NoExplicitEncoding_DefaultsToAnsi()
    {
        var req = Req(LoadFileFormat.Opt, encoding: "UTF-8", isExplicit: false);
        var policy = new TextOutputPolicy(req, LoadFileFormat.Opt, WriterMode.Standard, hasChaos: false);

        Assert.Equal(1252, policy.Encoding.CodePage); // Windows-1252 / ANSI
        Assert.Equal(Environment.NewLine, policy.EndOfLine);
    }

    [Fact]
    public void Opt_ExplicitEncoding_UsesExplicit()
    {
        var req = Req(LoadFileFormat.Opt, encoding: "UTF-16", isExplicit: true);
        var policy = new TextOutputPolicy(req, LoadFileFormat.Opt, WriterMode.Standard, hasChaos: false);

        Assert.Equal(Encoding.Unicode.CodePage, policy.Encoding.CodePage);
    }

    [Fact]
    public void Dat_Standard_NoChaos_PlatformNewLine()
    {
        var req = Req(LoadFileFormat.Dat, eol: "LF");
        var policy = new TextOutputPolicy(req, LoadFileFormat.Dat, WriterMode.Standard, hasChaos: false);

        Assert.Equal(Environment.NewLine, policy.EndOfLine);
    }

    [Fact]
    public void Dat_LoadfileOnly_ConfiguredNewLine()
    {
        var req = Req(LoadFileFormat.Dat, eol: "LF");
        var policy = new TextOutputPolicy(req, LoadFileFormat.Dat, WriterMode.LoadfileOnly, hasChaos: false);

        Assert.Equal("\n", policy.EndOfLine);
    }

    [Fact]
    public void Dat_Standard_WithChaos_ConfiguredNewLine()
    {
        var req = Req(LoadFileFormat.Dat, eol: "CR");
        var policy = new TextOutputPolicy(req, LoadFileFormat.Dat, WriterMode.Standard, hasChaos: true);

        Assert.Equal("\r", policy.EndOfLine);
    }

    [Fact]
    public void Csv_Standard_NoChaos_PlatformNewLine()
    {
        var req = Req(LoadFileFormat.Csv, eol: "LF");
        var policy = new TextOutputPolicy(req, LoadFileFormat.Csv, WriterMode.Standard, hasChaos: false);

        Assert.Equal(Environment.NewLine, policy.EndOfLine);
    }

    [Fact]
    public void Csv_ConfiguredEncoding_UsesIt()
    {
        var req = Req(LoadFileFormat.Csv, encoding: "ASCII", isExplicit: true);
        var policy = new TextOutputPolicy(req, LoadFileFormat.Csv, WriterMode.Standard, hasChaos: false);

        Assert.Equal(Encoding.ASCII.CodePage, policy.Encoding.CodePage);
    }

    [Fact]
    public void Csv_WithChaos_PlatformNewLine()
    {
        var req = Req(LoadFileFormat.Csv, eol: "LF");
        var policy = new TextOutputPolicy(req, LoadFileFormat.Csv, WriterMode.Standard, hasChaos: true);

        // Csv should always use Environment.NewLine even with chaos
        Assert.Equal(Environment.NewLine, policy.EndOfLine);
    }

    [Fact]
    public void Concordance_Standard_NoChaos_PlatformNewLine()
    {
        var req = Req(LoadFileFormat.Concordance, eol: "LF");
        var policy = new TextOutputPolicy(req, LoadFileFormat.Concordance, WriterMode.Standard, hasChaos: false);

        Assert.Equal(Environment.NewLine, policy.EndOfLine);
    }

    [Fact]
    public void Concordance_WithChaos_PlatformNewLine()
    {
        var req = Req(LoadFileFormat.Concordance, eol: "LF");
        var policy = new TextOutputPolicy(req, LoadFileFormat.Concordance, WriterMode.Standard, hasChaos: true);

        // Concordance should always use Environment.NewLine even with chaos
        Assert.Equal(Environment.NewLine, policy.EndOfLine);
    }

    [Fact]
    public void Concordance_LoadfileOnly_PlatformNewLine()
    {
        var req = Req(LoadFileFormat.Concordance, eol: "LF");
        var policy = new TextOutputPolicy(req, LoadFileFormat.Concordance, WriterMode.LoadfileOnly, hasChaos: false);

        // Concordance should always use Environment.NewLine even outside Standard mode
        Assert.Equal(Environment.NewLine, policy.EndOfLine);
    }

    [Fact]
    public void Opt_ImplicitNonUtf8_UsesResolvedEncoding()
    {
        var req = Req(LoadFileFormat.Opt, encoding: "ASCII", isExplicit: false);
        var policy = new TextOutputPolicy(req, LoadFileFormat.Opt, WriterMode.Standard, hasChaos: false);

        // If implicit but not UTF8, it should preserve the requested encoding
        Assert.Equal(Encoding.ASCII.CodePage, policy.Encoding.CodePage);
    }
}
