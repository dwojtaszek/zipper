using Xunit;
using Zipper.ManifestComparison;

namespace Zipper.Tests;

public class ComparisonEngineTests
{
    private static ComparisonRecord Rec(string bates, string control, string path, string hash, string volume, int line = 0) =>
        new() { BatesNumber = bates, ControlNumber = control, FilePath = path, Hash = hash, Volume = volume, SourceLine = line };

    [Fact]
    public void PerformComparison_ReplacementMode_ClassifiesAddedAndRemoved()
    {
        var prior = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "NATIVES/VOL001/doc1.pdf", "h1", "VOL001"),
            Rec("PR000002", "DOC002", "NATIVES/VOL001/doc2.pdf", "h2", "VOL001")
        };
        var newRecs = new List<ComparisonRecord>
        {
            Rec("PR000010", "DOC010", "NATIVES/VOL001/doc10.pdf", "h10", "VOL001")
        };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, "replacement", new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/prior/_manifest.json" }, "/new/_manifest.json");

        Assert.Equal("replacement", result.ComparisonMode);
        Assert.Single(result.Details.Added);
        Assert.Equal(2, result.Details.Removed.Count);
        Assert.Empty(result.Details.Unchanged);
        Assert.Equal(2, result.Summary.TotalPriorRecords);
        Assert.Equal(1, result.Summary.TotalNewRecords);
    }

    [Fact]
    public void PerformComparison_MatchingBates_SameHashPathControl_ClassifiesUnchanged()
    {
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, "replacement", new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        Assert.Single(result.Details.Unchanged);
        Assert.Empty(result.Details.Changed);
        Assert.Empty(result.Details.Added);
        Assert.Empty(result.Details.Removed);
    }

    [Fact]
    public void PerformComparison_MatchingBates_DifferentHash_ClassifiesChanged()
    {
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1-modified", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, "replacement", new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        Assert.Single(result.Details.Changed);
        Assert.Equal("h1", result.Details.Changed[0].PriorHash);
        Assert.Equal("h1-modified", result.Details.Changed[0].NewHash);
    }

    [Fact]
    public void PerformComparison_SupplementalMode_OverlapRecordedAsDuplicateAndAdded_NoRemovals()
    {
        var prior = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };
        var newRecs = new List<ComparisonRecord> { Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001") };

        var result = ComparisonEngine.PerformComparison(prior, newRecs, "supplemental", new List<DuplicateDetail>(), new List<DuplicateDetail>(), new List<string> { "/p" }, "/n");

        // Supplemental mode: overlapping Bates/Control is a duplicate + added; no removals.
        Assert.True(result.Summary.DuplicateCount > 0);
        Assert.Single(result.Details.Added);
        Assert.Empty(result.Details.Removed);
    }

    [Fact]
    public void FindDuplicates_WithRepeatedBates_ReportsDuplicates()
    {
        var records = new List<ComparisonRecord>
        {
            Rec("PR000001", "DOC001", "n1.pdf", "h1", "VOL001", line: 2),
            Rec("PR000001", "DOC002", "n2.pdf", "h2", "VOL001", line: 5)
        };

        var dups = ComparisonEngine.FindDuplicates(records, "prior");

        Assert.Single(dups);
        Assert.Equal("prior", dups[0].Set);
        Assert.Contains("Duplicate Bates Number 'PR000001'", dups[0].Message, StringComparison.Ordinal);
    }
}
