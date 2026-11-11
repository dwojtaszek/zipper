using Xunit;
using Zipper.Config;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public sealed class CrossFormatLoadFileRendererTests
{
    [Theory]
    [InlineData("CRLF", "Line1\nLine2", "Line3\rLine4", "\n", "Line1\nLine2", "Line3\nLine4")]
    [InlineData("LF", "Line1\rLine2", "Line3", "\r", "Line1\rLine2", "Line3")]
    [InlineData("CR", "Line1\nLine2", "Line3", "\n", "Line1\nLine2", "Line3")]
    public void RenderLines_EmbeddedNewlineToken_PreservesSerializedValues(string eol, string first, string second, string newlineDelimiter, string expectedFirst, string expectedSecond)
    {
        var composer = new FixedComposer(["DOCID", "TEXT"], [
            ["DOC1", first], ["DÓC2", second], ["last", ""],
        ]);
        var request = new FileGenerationRequest
        {
            Delimiters = new DelimiterConfig { EndOfLine = eol, QuoteDelimiter = string.Empty, NewlineDelimiter = newlineDelimiter },
        };
        var lines = CrossFormatLoadFileRenderer.RenderLines(
            composer, new DatSerializer(request), Array.Empty<FileData>(), new TextOutputPolicy(request, LoadFileFormat.Dat, WriterMode.LoadfileOnly, hasChaos: true), CancellationToken.None);

        Assert.Equal(new[] { "DOCID\u0014TEXT", $"DOC1\u0014{expectedFirst}", $"DÓC2\u0014{expectedSecond}", "last\u0014" }, lines);
        var optLines = CrossFormatLoadFileRenderer.RenderLines(
            new FixedComposer([], [["B1", "VOL001", "IMAGES\\VOL001\\DOC1.tif", "Y", "", "", "1"],
                ["B2", "VOL001", "IMAGES\\VOL001\\DÓC2.tif", "Y", "", "", "1"]]),
            new OptSerializer(), Array.Empty<FileData>(), new TextOutputPolicy(request, LoadFileFormat.Dat, WriterMode.LoadfileOnly, hasChaos: true), CancellationToken.None);
        Assert.Equal(new[] { "B1,VOL001,IMAGES\\VOL001\\DOC1.tif,Y,,,1", "B2,VOL001,IMAGES\\VOL001\\DÓC2.tif,Y,,,1" }, optLines);
    }

    [Theory]
    [InlineData("UTF-8", "CRLF")]
    [InlineData("UTF-16", "LF")]
    [InlineData("ANSI", "CR")]
    public async Task RenderAndApply_NoAnomalies_MatchesComposingWriterBytes(string encoding, string eol)
    {
        var request = new FileGenerationRequest
        {
            LoadfileOnly = true,
            Output = new OutputConfig { FileCount = 3 },
            Metadata = new MetadataConfig { Seed = 42 },
            LoadFile = new LoadFileConfig { Encoding = encoding, IsEncodingExplicit = true },
            Delimiters = new DelimiterConfig { EndOfLine = eol },
            Chaos = new ChaosConfig { ChaosAmount = "0" },
        };
        var result = CrossFormatLoadFileRenderer.RenderAndApply(request, request, [], WriterMode.LoadfileOnly,
            "loadfile.dat", "loadfile.opt", CancellationToken.None);
        foreach (var (format, lines) in new[] { (LoadFileFormat.Dat, result.DatLines), (LoadFileFormat.Opt, result.OptLines) })
        {
            using var expected = new MemoryStream();
            await LoadFileWriterFactory.CreateWriter(format, WriterMode.LoadfileOnly).WriteAsync(expected, request, []);
            using var actual = new MemoryStream();
            var policy = new TextOutputPolicy(request, format, WriterMode.LoadfileOnly, hasChaos: true);
            await LoadFileEmitter.EmitRenderedLinesAsync(actual, lines, policy.Encoding, policy.EndOfLine);
            Assert.Equal(expected.ToArray(), actual.ToArray());
        }
    }

    [Fact]
    public void RenderLines_Canceled_ThrowsBeforeComposing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => CrossFormatLoadFileRenderer.RenderLines(
            new FixedComposer([], []), new OptSerializer(), [], new TextOutputPolicy(new FileGenerationRequest(), LoadFileFormat.Opt, WriterMode.LoadfileOnly, hasChaos: true), cancellation.Token));
    }

    [Fact]
    public void RenderAndApply_AnsiOptWithUnicodeBates_UsesEncodedIdentityInAnomalies()
    {
        var request = new FileGenerationRequest
        {
            LoadfileOnly = true,
            Output = new OutputConfig { FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42 },
            Bates = new BatesNumberConfig { Prefix = "漢" },
            Chaos = new ChaosConfig { ChaosScenario = "cross-format-desync", ChaosAmount = "1" },
        };
        var result = CrossFormatLoadFileRenderer.RenderAndApply(request, request, [], WriterMode.LoadfileOnly,
            "loadfile.dat", "loadfile.opt", CancellationToken.None);
        var anomaly = Assert.Single(result.OptAnomalies);
        Assert.StartsWith("?", anomaly.RecordID, StringComparison.Ordinal);
        Assert.DoesNotContain("漢", anomaly.Description, StringComparison.Ordinal);
        Assert.All(result.OptLines, line => Assert.DoesNotContain("漢", line, StringComparison.Ordinal));
    }

    [Fact]
    public void RenderAndApply_ColumnDelimiterEqualsLineEnding_PreservesPhysicalLineCounts()
    {
        var request = new FileGenerationRequest
        {
            LoadfileOnly = true,
            Output = new OutputConfig { FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42 },
            Delimiters = new DelimiterConfig { ColumnDelimiter = "\n", EndOfLine = "LF" },
            Chaos = new ChaosConfig { ChaosScenario = "cross-format-desync", ChaosAmount = "1" },
        };
        var result = CrossFormatLoadFileRenderer.RenderAndApply(request, request, [], WriterMode.LoadfileOnly,
            "loadfile.dat", "loadfile.opt", CancellationToken.None);
        Assert.Equal(24, result.DatLines.Count);
        Assert.All(result.DatLines, line => Assert.DoesNotContain("\n", line, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Standard", false)]
    [InlineData("Standard", true)]
    [InlineData("LoadfileOnly", true)]
    [InlineData("ProductionSet", false)]
    public async Task RenderAndApply_WarnsExactlyWhenOptComposingWriterWarns(string writerModeName, bool loadfileOnly)
    {
        var writerMode = Enum.Parse<WriterMode>(writerModeName);
        var request = new FileGenerationRequest
        {
            LoadfileOnly = loadfileOnly,
            Output = new OutputConfig { FileCount = 1 },
            Metadata = new MetadataConfig { Seed = 42, WithMetadata = true },
        };

        var (_, expectedError) = await ConsoleOutputCapture.RunWithCapturedOutputAsync(
            () => LoadFileWriterFactory.CreateWriter(LoadFileFormat.Opt, writerMode)
                .WriteAsync(new MemoryStream(), request, []));
        var (_, _, actualError) = ConsoleOutputCapture.RunWithCapturedOutput(
            () => CrossFormatLoadFileRenderer.RenderAndApply(request, request, [], writerMode,
                "loadfile.dat", "loadfile.opt", CancellationToken.None));

        Assert.Equal(expectedError, actualError);
    }

    private sealed class FixedComposer(IReadOnlyList<string> columns, IReadOnlyList<string[]> values) : ILoadFileComposer
    {
        public IReadOnlyList<string> HeaderColumns => columns;

        public IEnumerable<LoadFileRecord> Compose(IReadOnlyList<FileData> processedFiles) => values.Select(row => new LoadFileRecord
        {
            Columns = columns.Count > 0 ? columns : Enumerable.Range(0, row.Length).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray(),
            Values = row,
        });
    }
}
