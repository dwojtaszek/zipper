using Xunit;
using Zipper.ManifestComparison;

namespace Zipper.Tests;

public class VolumeAnalyzerTests
{
    [Fact]
    public void AnalyzeVolumes_WithNewVolume_ShouldReportAdded()
    {
        var results = Analyze([], [Record("ABC00000001")]);

        var result = Assert.Single(results);
        Assert.Equal("added", result.Status);
    }

    [Fact]
    public void AnalyzeVolumes_WithMissingNewVolume_ShouldReportRemoved()
    {
        var results = Analyze([Record("ABC00000001")], []);

        var result = Assert.Single(results);
        Assert.Equal("removed", result.Status);
    }

    [Fact]
    public void AnalyzeVolumes_WithEquivalentRecordsInDifferentOrderAndCase_ShouldReportUnchanged()
    {
        var prior = new List<ComparisonRecord>
        {
            Record("ABC00000001", path: "NATIVES/ABC00000001.pdf", hash: "ABCDEF"),
            Record("ABC00000002", path: "NATIVES/ABC00000002.pdf", hash: "123456")
        };
        var newRecords = new List<ComparisonRecord>
        {
            Record("abc00000002", volume: "vol001", path: "natives/abc00000002.PDF", hash: "123456"),
            Record("abc00000001", volume: "vol001", path: "natives/abc00000001.PDF", hash: "abcdef")
        };

        var results = Analyze(prior, newRecords);

        var result = Assert.Single(results);
        Assert.Equal("unchanged", result.Status);
    }

    [Fact]
    public void AnalyzeVolumes_WithDifferentRecordCount_ShouldReportChanged()
    {
        var results = Analyze(
            [Record("ABC00000001")],
            [Record("ABC00000001"), Record("ABC00000001")]);

        var result = Assert.Single(results);
        Assert.Equal("changed", result.Status);
    }

    [Fact]
    public void AnalyzeVolumes_WithEqualCountAndDifferentBatesMembership_ShouldReportChanged()
    {
        var results = Analyze(
            [Record("ABC00000001"), Record("ABC00000002")],
            [Record("ABC00000003"), Record("ABC00000004")]);

        var result = Assert.Single(results);
        Assert.Equal("changed", result.Status);
    }

    [Fact]
    public void AnalyzeVolumes_WithDifferentPath_ShouldReportChanged()
    {
        var results = Analyze(
            [Record("ABC00000001", path: "NATIVES/original.pdf")],
            [Record("ABC00000001", path: "NATIVES/replacement.pdf")]);

        var result = Assert.Single(results);
        Assert.Equal("changed", result.Status);
    }

    [Fact]
    public void AnalyzeVolumes_WithDifferentNonEmptyHash_ShouldReportChanged()
    {
        var results = Analyze(
            [Record("ABC00000001", hash: "AAAA")],
            [Record("ABC00000001", hash: "BBBB")]);

        var result = Assert.Single(results);
        Assert.Equal("changed", result.Status);
    }

    [Theory]
    [InlineData("", "BBBB")]
    [InlineData("BBBB", "")]
    public void AnalyzeVolumes_WithEitherHashEmpty_ShouldReportUnchanged(string priorHash, string newHash)
    {
        var results = Analyze(
            [Record("ABC00000001", hash: priorHash)],
            [Record("ABC00000001", hash: newHash)]);

        var result = Assert.Single(results);
        Assert.Equal("unchanged", result.Status);
    }

    [Fact]
    public void AnalyzeVolumes_WithReorderedDuplicateBatesRecords_ShouldReportUnchanged()
    {
        var prior = new List<ComparisonRecord>
        {
            Record("ABC00000001", path: "NATIVES/first.pdf", hash: "AAAA"),
            Record("ABC00000001", path: "NATIVES/second.pdf", hash: "BBBB")
        };
        var newRecords = new List<ComparisonRecord>
        {
            Record("ABC00000001", path: "NATIVES/second.pdf", hash: "BBBB"),
            Record("ABC00000001", path: "NATIVES/first.pdf", hash: "AAAA")
        };

        var results = Analyze(prior, newRecords);

        var result = Assert.Single(results);
        Assert.Equal("unchanged", result.Status);
    }

    [Fact]
    public void AnalyzeVolumes_WithChangedDuplicateBatesRecord_ShouldReportChanged()
    {
        var prior = new List<ComparisonRecord>
        {
            Record("ABC00000001", path: "NATIVES/first.pdf"),
            Record("ABC00000001", path: "NATIVES/second.pdf")
        };
        var newRecords = new List<ComparisonRecord>
        {
            Record("ABC00000001", path: "NATIVES/first.pdf"),
            Record("ABC00000001", path: "NATIVES/replacement.pdf")
        };

        var results = Analyze(prior, newRecords);

        var result = Assert.Single(results);
        Assert.Equal("changed", result.Status);
    }

    [Fact]
    public void AnalyzeVolumes_WithEmptyBatesAndDifferentPath_ShouldReportChanged()
    {
        var results = Analyze(
            [Record(string.Empty, path: "NATIVES/original.pdf")],
            [Record(string.Empty, path: "NATIVES/replacement.pdf")]);

        var result = Assert.Single(results);
        Assert.Equal("changed", result.Status);
    }

    private static List<VolumeResult> Analyze(
        List<ComparisonRecord> priorRecords,
        List<ComparisonRecord> newRecords)
    {
        var results = new List<VolumeResult>();
        VolumeAnalyzer.AnalyzeVolumes(priorRecords, newRecords, results);
        return results;
    }

    private static ComparisonRecord Record(
        string batesNumber,
        string volume = "VOL001",
        string path = "NATIVES/document.pdf",
        string hash = "AAAA")
    {
        return new ComparisonRecord
        {
            BatesNumber = batesNumber,
            Volume = volume,
            FilePath = path,
            Hash = hash,
            ProductionId = "PROD001"
        };
    }
}
