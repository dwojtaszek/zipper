using Xunit;
using Zipper.ManifestComparison;

namespace Zipper.Tests;

public class BatesRangeAnalyzerTests
{
    [Fact]
    public void TryParseBates_WithPrefixAndNumber_ShouldExtractPrefixValueDigits()
    {
        var ok = BatesRangeAnalyzer.TryParseBates("SUPP00000007", out var prefix, out long num, out var digits);
        Assert.True(ok);
        Assert.Equal("SUPP", prefix);
        Assert.Equal(7, num);
        Assert.Equal(8, digits);
    }

    [Fact]
    public void TryParseBates_WithEmptyValue_ShouldReturnFalse()
    {
        Assert.False(BatesRangeAnalyzer.TryParseBates(string.Empty, out _, out _, out _));
    }

    [Fact]
    public void FormatBates_PreservesZeroPaddedWidth()
    {
        Assert.Equal("SUPP00000010", BatesRangeAnalyzer.FormatBates(10, "SUPP", 8));
    }

    [Fact]
    public void FindGapsInSequence_WithGap_ShouldReportSkippedRange()
    {
        var gaps = BatesRangeAnalyzer.FindGapsInSequence(new List<string> { "PR000001", "PR000002", "PR000005" });
        Assert.Single(gaps);
        Assert.Equal("PR000003", gaps[0].Start);
        Assert.Equal("PR000004", gaps[0].End);
    }

    [Fact]
    public void FindGapsInSequence_WithContiguousSequence_ShouldReportNoGaps()
    {
        var gaps = BatesRangeAnalyzer.FindGapsInSequence(new List<string> { "PR000001", "PR000002", "PR000003" });
        Assert.Empty(gaps);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_SumsSkippedBatesAcrossGaps()
    {
        var analysis = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "PR000003", End = "PR000005" },
                new() { Start = "PR000010", End = "PR000010" }
            }
        };

        BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis);

        Assert.Equal(4, analysis.TotalSkippedBates); // 3 (003-005) + 1 (010)
    }
}
