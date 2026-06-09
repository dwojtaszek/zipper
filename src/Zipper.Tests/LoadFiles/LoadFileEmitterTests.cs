using System.Text;

using Xunit;
using Zipper.LoadFiles;

namespace Zipper.Tests.LoadFiles;

public class LoadFileEmitterTests
{
    /// <summary>
    /// Minimal serializer that renders columns/values verbatim, pipe-delimited,
    /// so emitter behaviour (preamble, EOL, ordering, chaos) is asserted in isolation.
    /// </summary>
    private sealed class FakeSerializer : ILoadFileSerializer
    {
        public string FormatName => "FAKE";

        public string FileExtension => ".fake";

        public string RenderHeader(IReadOnlyList<string> columns) => string.Join("|", columns);

        public string RenderRecord(LoadFileRecord record) =>
            string.Join("|", record.Columns.Select(c => record.Values.TryGetValue(c, out var v) ? v : string.Empty));
    }

    private static readonly UTF8Encoding NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static LoadFileRecord Rec(string id, params (string Col, string Val)[] cells) => new()
    {
        Columns = cells.Select(c => c.Col).ToList(),
        Values = cells.ToDictionary(c => c.Col, c => c.Val, StringComparer.Ordinal),
        RecordId = id,
    };

    [Fact]
    public async Task NoChaos_WithHeader_WritesHeaderThenRecords()
    {
        using var ms = new MemoryStream();
        var records = new[]
        {
            Rec("1", ("A", "x"), ("B", "y")),
            Rec("2", ("A", "p"), ("B", "q")),
        };

        await LoadFileEmitter.EmitAsync(ms, new FakeSerializer(), new[] { "A", "B" }, records, NoBom, "\n", chaosEngine: null);

        var content = NoBom.GetString(ms.ToArray());
        Assert.Equal("A|B\nx|y\np|q\n", content);
    }

    [Fact]
    public async Task NoChaos_NoHeader_WritesRecordsOnly()
    {
        using var ms = new MemoryStream();
        var records = new[] { Rec("1", ("A", "x")), Rec("2", ("A", "y")) };

        await LoadFileEmitter.EmitAsync(ms, new FakeSerializer(), Array.Empty<string>(), records, NoBom, "\r\n", chaosEngine: null);

        Assert.Equal("x\r\ny\r\n", NoBom.GetString(ms.ToArray()));
    }

    [Fact]
    public async Task NoChaos_EmitsEncodingPreamble()
    {
        using var ms = new MemoryStream();
        var withBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        await LoadFileEmitter.EmitAsync(ms, new FakeSerializer(), Array.Empty<string>(), new[] { Rec("1", ("A", "x")) }, withBom, "\n", chaosEngine: null);

        var bytes = ms.ToArray();
        Assert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
    }

    [Fact]
    public async Task NoChaos_StreamsLargeInputWithoutLoss()
    {
        using var ms = new MemoryStream();
        var records = Enumerable.Range(0, 5000).Select(i => Rec(i.ToString(System.Globalization.CultureInfo.InvariantCulture), ("A", $"v{i}")));

        await LoadFileEmitter.EmitAsync(ms, new FakeSerializer(), Array.Empty<string>(), records, NoBom, "\n", chaosEngine: null);

        var lines = NoBom.GetString(ms.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(5000, lines.Length);
        Assert.Equal("v0", lines[0]);
        Assert.Equal("v4999", lines[4999]);
    }

    [Fact]
    public async Task Chaos_WithNoTargets_MatchesNoChaosOutput()
    {
        var records = new[] { Rec("1", ("A", "x"), ("B", "y")), Rec("2", ("A", "p"), ("B", "q")) };
        var header = new[] { "A", "B" };

        using var plain = new MemoryStream();
        await LoadFileEmitter.EmitAsync(plain, new FakeSerializer(), header, records, NoBom, "\n", chaosEngine: null);

        // 3 lines total (header + 2 records); zero chaos amount => no interception, no anomalies.
        var chaos = new ChaosEngine(3, "0", null, LoadFileFormat.Dat, "|", "\"", "\n", seed: 1);
        using var chaosMs = new MemoryStream();
        await LoadFileEmitter.EmitAsync(chaosMs, new FakeSerializer(), header, records, NoBom, "\n", chaos);

        Assert.Equal(plain.ToArray(), chaosMs.ToArray());
    }
}
