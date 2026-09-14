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
    public void FindGapsInSequence_WithLargeGapAboveInt32Max_ReturnsExpectedGapRange()
    {
        var gaps = BatesRangeAnalyzer.FindGapsInSequence(new List<string> { "B0000000001", "B2147483650" });
        Assert.Single(gaps);
        Assert.Equal("B0000000002", gaps[0].Start);
        Assert.Equal("B2147483649", gaps[0].End);
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

    [Fact]
    public void AnalyzeBatesRanges_SingleLargeGapAboveInt32Max_EmitsExactPositiveTotal()
    {
        var priorRecords = new List<ComparisonRecord>();
        var newRecords = new List<ComparisonRecord>
        {
            new() { BatesNumber = "B0000000001" },
            new() { BatesNumber = "B2147483650" }
        };
        var batesAnalysis = new BatesAnalysis();
        var details = new ResultDetails();

        BatesRangeAnalyzer.AnalyzeBatesRanges(priorRecords, newRecords, "replacement", batesAnalysis, details);

        Assert.Equal(2147483648L, batesAnalysis.TotalSkippedBates);
        Assert.Single(batesAnalysis.Gaps);
        Assert.Equal("B0000000002", batesAnalysis.Gaps[0].Start);
        Assert.Equal("B2147483649", batesAnalysis.Gaps[0].End);
        Assert.Single(details.Skipped);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_ZeroGapInput_LeavesTotalSkippedAsZero()
    {
        var analysis = new BatesAnalysis { Gaps = new List<BatesRangeReport>() };
        BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis);
        Assert.Equal(0L, analysis.TotalSkippedBates);
    }

    [Fact]
    public void AnalyzeBatesRanges_ZeroGapContiguousRecords_ReportsZeroSkipped()
    {
        var priorRecords = new List<ComparisonRecord>();
        var newRecords = new List<ComparisonRecord>
        {
            new() { BatesNumber = "PR000001" },
            new() { BatesNumber = "PR000002" },
            new() { BatesNumber = "PR000003" }
        };
        var batesAnalysis = new BatesAnalysis();
        var details = new ResultDetails();

        BatesRangeAnalyzer.AnalyzeBatesRanges(priorRecords, newRecords, "replacement", batesAnalysis, details);

        Assert.Equal(0L, batesAnalysis.TotalSkippedBates);
        Assert.Empty(batesAnalysis.Gaps);
        Assert.Empty(details.Skipped);
    }

    [Fact]
    public void AnalyzeBatesRanges_EmptyRecords_ReportsZeroSkipped()
    {
        var batesAnalysis = new BatesAnalysis();
        var details = new ResultDetails();

        BatesRangeAnalyzer.AnalyzeBatesRanges(new List<ComparisonRecord>(), new List<ComparisonRecord>(), "replacement", batesAnalysis, details);

        Assert.Equal(0L, batesAnalysis.TotalSkippedBates);
        Assert.Empty(batesAnalysis.Gaps);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_NumericBoundary_GapOfOne()
    {
        var analysis = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "PR000010", End = "PR000010" }
            }
        };

        BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis);

        Assert.Equal(1L, analysis.TotalSkippedBates);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_NumericBoundary_Int32MaxAndPlusOne()
    {
        var analysisIntMax = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "B0000000001", End = "B2147483647" }
            }
        };
        BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysisIntMax);
        Assert.Equal(2147483647L, analysisIntMax.TotalSkippedBates);

        var analysisIntMaxPlusOne = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "B0000000001", End = "B2147483648" }
            }
        };
        BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysisIntMaxPlusOne);
        Assert.Equal(2147483648L, analysisIntMaxPlusOne.TotalSkippedBates);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_NumericBoundary_LongMaxValueWithoutOverflow()
    {
        var analysis = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "B0000000001", End = $"B{long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}" }
            }
        };

        BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis);

        Assert.Equal(long.MaxValue, analysis.TotalSkippedBates);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_NumericBoundary_SingleGapOverflowThrowsDescriptiveException()
    {
        var analysis = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "B0000000000", End = $"B{long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}" }
            }
        };

        var ex = Assert.Throws<OverflowException>(() => BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis));
        Assert.Contains("Total skipped Bates numbers exceeds the maximum supported value", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_AggregateOverflowAcrossSmallerGaps_ThrowsDescriptiveException()
    {
        long half = (long.MaxValue / 2) + 1;
        var analysis = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "B0000000001", End = $"B{half.ToString(System.Globalization.CultureInfo.InvariantCulture)}" },
                new() { Start = "B0000000001", End = $"B{half.ToString(System.Globalization.CultureInfo.InvariantCulture)}" }
            }
        };

        var ex = Assert.Throws<OverflowException>(() => BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis));
        Assert.Contains("Total skipped Bates numbers exceeds the maximum supported value", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_InvertedGapRange_ThrowsArgumentException()
    {
        var analysis = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "B0000000010", End = "B0000000005" }
            }
        };

        Assert.Throws<ArgumentException>(() => BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis));
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_MismatchedPrefixOrNonNumeric_IncrementsByOne()
    {
        var analysis = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "PREFIXA0001", End = "PREFIXB0005" },
                new() { Start = "NONUMBERA", End = "NONUMBERB" }
            }
        };

        BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis);

        Assert.Equal(2L, analysis.TotalSkippedBates);
    }

    [Fact]
    public void BatesAnalysis_JsonSerialization_PreservesExactPositiveTotalAboveInt32Max()
    {
        var analysis = new BatesAnalysis
        {
            PriorRange = "B0000000001 - B0000000001",
            NewRange = "B0000000001 - B2147483650",
            TotalSkippedBates = 2147483648L,
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "B0000000002", End = "B2147483649" }
            }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(analysis);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var totalSkippedElement = doc.RootElement.GetProperty("totalSkippedBates");

        Assert.Equal(2147483648L, totalSkippedElement.GetInt64());

        var deserialized = System.Text.Json.JsonSerializer.Deserialize<BatesAnalysis>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(2147483648L, deserialized.TotalSkippedBates);
    }

    [Fact]
    public void AnalyzeBatesRanges_SupplementalModeWithLargeGapAboveInt32Max_DetectsGapAndSetsTotal()
    {
        var priorRecords = new List<ComparisonRecord> { new() { BatesNumber = "B0000000001" } };
        var newRecords = new List<ComparisonRecord> { new() { BatesNumber = "B2147483650" } };
        var batesAnalysis = new BatesAnalysis();
        var details = new ResultDetails();

        BatesRangeAnalyzer.AnalyzeBatesRanges(priorRecords, newRecords, "supplemental", batesAnalysis, details);

        Assert.Single(batesAnalysis.Gaps);
        Assert.Equal("B0000000002", batesAnalysis.Gaps[0].Start);
        Assert.Equal("B2147483649", batesAnalysis.Gaps[0].End);
        Assert.Equal(2147483648L, batesAnalysis.TotalSkippedBates);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_NullBatesAnalysis_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => BatesRangeAnalyzer.ResultSummaryBatesAnalysis(null!));
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_NullGapsCollection_SetsZeroTotal()
    {
        var analysis = new BatesAnalysis { Gaps = null! };
        BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis);
        Assert.Equal(0L, analysis.TotalSkippedBates);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_GapsWithNullElement_SkipsNullAndSumsValid()
    {
        var analysis = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                null!,
                new BatesRangeReport { Start = "PR000001", End = "PR000002" }
            }
        };
        BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis);
        Assert.Equal(2L, analysis.TotalSkippedBates);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_GapsWithDifferentCasePrefix_SumsCorrectly()
    {
        var analysis = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "prod000001", End = "PROD000010" }
            }
        };
        BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis);
        Assert.Equal(10L, analysis.TotalSkippedBates);
    }

    [Fact]
    public void ResultSummaryBatesAnalysis_OverflowInFallbackGapHandling_ThrowsDescriptiveException()
    {
        var analysis = new BatesAnalysis
        {
            Gaps = new List<BatesRangeReport>
            {
                new() { Start = "B0000000001", End = $"B{long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}" },
                new() { Start = "PREFIXA0001", End = "PREFIXB0002" }
            }
        };
        var ex = Assert.Throws<OverflowException>(() => BatesRangeAnalyzer.ResultSummaryBatesAnalysis(analysis));
        Assert.Contains("Total skipped Bates numbers exceeds the maximum supported value", ex.Message, StringComparison.Ordinal);
    }
}
