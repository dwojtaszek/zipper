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
        Assert.Equal("VOL001", result.VolumeName);
        Assert.Equal("PROD001", result.ProductionId);
        Assert.Equal(string.Empty, result.PriorBatesRange);
        Assert.Equal("ABC00000001 - ABC00000001", result.NewBatesRange);
    }

    [Fact]
    public void AnalyzeVolumes_WithMissingNewVolume_ShouldReportRemoved()
    {
        var results = Analyze([Record("ABC00000001")], []);

        var result = Assert.Single(results);
        Assert.Equal("removed", result.Status);
        Assert.Equal("VOL001", result.VolumeName);
        Assert.Equal("PROD001", result.ProductionId);
        Assert.Equal("ABC00000001 - ABC00000001", result.PriorBatesRange);
        Assert.Equal(string.Empty, result.NewBatesRange);
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
        Assert.Equal("VOL001", result.VolumeName);
        Assert.Equal("PROD001", result.ProductionId);
        Assert.Equal("ABC00000001 - ABC00000002", result.PriorBatesRange);
        Assert.Equal("abc00000001 - abc00000002", result.NewBatesRange);
    }

    [Fact]
    public void AnalyzeVolumes_WithMultipleVolumes_PopulatesCommonLeftOnlyAndRightOnlyResults()
    {
        var prior = new List<ComparisonRecord>
        {
            Record("ABC00000001", volume: "VOL001", path: "NATIVES/VOL001/ABC00000001.pdf", hash: "H1"),
            Record("ABC00000002", volume: "VOL001", path: "NATIVES/VOL001/ABC00000002.pdf", hash: "H2"),
            Record("ABC00000003", volume: "VOL002", path: "NATIVES/VOL002/ABC00000003.pdf", hash: "H3"),
            Record("ABC00000004", volume: "VOL003", path: "NATIVES/VOL003/ABC00000004.pdf", hash: "H4"),
        };
        var newRecords = new List<ComparisonRecord>
        {
            Record("ABC00000001", volume: "VOL001", path: "NATIVES/VOL001/ABC00000001.pdf", hash: "H1"),
            Record("ABC00000002", volume: "VOL001", path: "NATIVES/VOL001/ABC00000002.pdf", hash: "H2"),
            Record("ABC00000003", volume: "VOL002", path: "NATIVES/VOL002/ABC00000003.pdf", hash: "MODIFIED"),
            Record("ABC00000005", volume: "VOL004", path: "NATIVES/VOL004/ABC00000005.pdf", hash: "H5"),
        };

        var results = Analyze(prior, newRecords);

        Assert.Equal(4, results.Count);

        Assert.Equal("VOL001", results[0].VolumeName);
        Assert.Equal("PROD001", results[0].ProductionId);
        Assert.Equal("ABC00000001 - ABC00000002", results[0].PriorBatesRange);
        Assert.Equal("ABC00000001 - ABC00000002", results[0].NewBatesRange);
        Assert.Equal("unchanged", results[0].Status);

        Assert.Equal("VOL002", results[1].VolumeName);
        Assert.Equal("PROD001", results[1].ProductionId);
        Assert.Equal("ABC00000003 - ABC00000003", results[1].PriorBatesRange);
        Assert.Equal("ABC00000003 - ABC00000003", results[1].NewBatesRange);
        Assert.Equal("changed", results[1].Status);

        Assert.Equal("VOL003", results[2].VolumeName);
        Assert.Equal("PROD001", results[2].ProductionId);
        Assert.Equal("ABC00000004 - ABC00000004", results[2].PriorBatesRange);
        Assert.Equal(string.Empty, results[2].NewBatesRange);
        Assert.Equal("removed", results[2].Status);

        Assert.Equal("VOL004", results[3].VolumeName);
        Assert.Equal("PROD001", results[3].ProductionId);
        Assert.Equal(string.Empty, results[3].PriorBatesRange);
        Assert.Equal("ABC00000005 - ABC00000005", results[3].NewBatesRange);
        Assert.Equal("added", results[3].Status);
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
