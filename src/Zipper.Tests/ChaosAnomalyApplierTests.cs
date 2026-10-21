using Xunit;

namespace Zipper.Tests;

public class DatAnomalyApplierTests
{
    [Fact]
    public void Apply_MixedDelimiters_NoDelimiterInLine_ReturnsUnchangedLine_AndDoesNotRecordAnomaly()
    {
        // Arrange: line with no delimiter
        var applier = new DatAnomalyApplier(new Random(42), "\u0014", "\u00fe", "\r\n");
        var anomalies = new List<ChaosAnomaly>();
        const string line = "NoDelimiterHere";

        // Act
        string result = applier.Apply(1, line, "DOC001", "mixed-delimiters", anomalies);

        // Assert
        Assert.Equal(line, result); // Line unchanged
        Assert.Empty(anomalies); // No anomaly recorded
    }

    [Fact]
    public void Apply_MixedDelimiters_WithDelimiter_ReplacesOneDelimiter_AndRecordsAnomaly()
    {
        // Arrange: line with delimiters
        var applier = new DatAnomalyApplier(new Random(42), "\u0014", "\u00fe", "\r\n");
        var anomalies = new List<ChaosAnomaly>();
        const string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe\u0014\u00feValue3\u00fe";

        // Act
        string result = applier.Apply(1, line, "DOC001", "mixed-delimiters", anomalies);

        // Assert
        Assert.NotEqual(line, result); // Line changed
        Assert.Single(anomalies);
        Assert.Equal("mixed-delimiters", anomalies[0].ErrorType);
        Assert.Contains("Delimiter", anomalies[0].Column, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_ColumnShift_AddTrue_AlwaysChangesLine_AndRecordsAnomaly()
    {
        // Arrange: line with delimiter
        const string line = "Value1\u0014Value2";

        // Run multiple times to catch both add and remove cases
        bool sawAdd = false;
        bool sawRemove = false;

        for (int seed = 0; seed < 20; seed++)
        {
            var testApplier = new DatAnomalyApplier(new Random(seed), "\u0014", "\u00fe", "\r\n");
            var testAnomalies = new List<ChaosAnomaly>();
            string result = testApplier.Apply(1, line, "DOC001", "columns", testAnomalies);

            if (result != line)
            {
                if (result.Count(c => c == '\u0014') > line.Count(c => c == '\u0014'))
                    sawAdd = true;
                else
                    sawRemove = true;

                Assert.Single(testAnomalies);
                Assert.Equal("columns", testAnomalies[0].ErrorType);
            }
        }

        Assert.True(sawAdd, "Should have seen add case");
        Assert.True(sawRemove, "Should have seen remove case");
    }

    [Fact]
    public void Apply_ColumnShift_RemoveWithNoDelimiter_ReturnsUnchangedLine_AndDoesNotRecordAnomaly()
    {
        // Arrange: line with NO delimiter
        const string line = "NoDelimiterHere";

        // Act - we need to force remove (add=false). 
        // Since it's random, we run multiple seeds until we hit remove.
        bool foundRemoveCase = false;

        for (int seed = 0; seed < 20; seed++)
        {
            var testApplier = new DatAnomalyApplier(new Random(seed), "\u0014", "\u00fe", "\r\n");
            var testAnomalies = new List<ChaosAnomaly>();
            string result = testApplier.Apply(1, line, "DOC001", "columns", testAnomalies);

            if (result == line) // remove case with no delimiter = no change
            {
                foundRemoveCase = true;
                // This is the bug: anomaly should NOT be recorded when line unchanged
                Assert.Empty(testAnomalies); // Currently fails: anomaly is recorded
                break;
            }
        }

        Assert.True(foundRemoveCase, "Should have found a seed that picks remove");
    }

    [Fact]
    public void Apply_Quotes_NoQuoteDelimiter_ReturnsUnchangedLine_AndDoesNotRecordAnomaly()
    {
        // Arrange: quote delimiter is empty
        var applier = new DatAnomalyApplier(new Random(42), "\u0014", string.Empty, "\r\n");
        var anomalies = new List<ChaosAnomaly>();
        const string line = "Value1\u0014Value2";

        // Act
        string result = applier.Apply(1, line, "DOC001", "quotes", anomalies);

        // Assert
        Assert.Equal(line, result);
        Assert.Empty(anomalies);
    }

    [Fact]
    public void Apply_Eol_AlwaysChangesLine_AndRecordsAnomaly()
    {
        var applier = new DatAnomalyApplier(new Random(42), "\u0014", "\u00fe", "\r\n");
        var anomalies = new List<ChaosAnomaly>();
        const string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe";

        string result = applier.Apply(1, line, "DOC001", "eol", anomalies);

        Assert.Contains("\r\n", result, StringComparison.Ordinal);
        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
        Assert.Equal("eol", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_Encoding_DoesNotChangeLine_RecordsAnomalyInGetEncodingAnomaly()
    {
        var applier = new DatAnomalyApplier(new Random(42), "\u0014", "\u00fe", "\r\n");
        var anomalies = new List<ChaosAnomaly>();
        const string line = "Value1\u0014Value2";

        string result = applier.Apply(1, line, "DOC001", "encoding", anomalies);

        Assert.Equal(line, result); // Line unchanged by Apply
        Assert.Empty(anomalies); // No anomaly in Apply

        // Anomaly is recorded in GetEncodingAnomaly
        var bytes = applier.GetEncodingAnomaly(1, 2, System.Text.Encoding.UTF8, anomalies);
        Assert.NotNull(bytes);
        Assert.Single(anomalies);
        Assert.Equal("encoding", anomalies[0].ErrorType);
    }
}

public class OptAnomalyApplierTests
{
    [Fact]
    public void Apply_OptBoundary_ValidLine_FlipsBoundary_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        string result = applier.Apply(1, line, "IMG001", "opt-boundary", anomalies);

        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
        Assert.Equal("opt-boundary", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_OptBoundary_InvalidLine_ReturnsUnchanged_AndDoesNotRecordAnomaly()
    {
        // Line with fewer than 4 fields
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001";

        string result = applier.Apply(1, line, "IMG001", "opt-boundary", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies); // Currently fails: anomaly recorded
    }

    [Fact]
    public void Apply_OptColumns_AddComma_AlwaysChangesLine_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        // We can't control the random choice, but add case always changes
        // Run multiple times to catch add case
        bool sawAdd = false;
        for (int seed = 0; seed < 20; seed++)
        {
            var testApplier = new OptAnomalyApplier(new Random(seed));
            var testAnomalies = new List<ChaosAnomaly>();
            string result = testApplier.Apply(1, line, "IMG001", "opt-columns", testAnomalies);

            if (result.Count(c => c == ',') > line.Count(c => c == ','))
            {
                sawAdd = true;
                Assert.Single(testAnomalies);
                Assert.Equal("opt-columns", testAnomalies[0].ErrorType);
                break;
            }
        }
        Assert.True(sawAdd);
    }

    [Fact]
    public void Apply_OptColumns_RemoveComma_NoCommaInLine_ReturnsUnchanged_AndDoesNotRecordAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "NO_COMMAS_HERE";

        // We need to hit the remove case. Run multiple seeds.
        bool foundRemoveCase = false;
        for (int seed = 0; seed < 20; seed++)
        {
            var testApplier = new OptAnomalyApplier(new Random(seed));
            var testAnomalies = new List<ChaosAnomaly>();
            string result = testApplier.Apply(1, line, "IMG001", "opt-columns", testAnomalies);

            if (result == line) // remove with no comma = no change
            {
                foundRemoveCase = true;
                Assert.Empty(testAnomalies); // Currently fails: anomaly recorded
                break;
            }
        }
        Assert.True(foundRemoveCase);
    }

    [Fact]
    public void Apply_OptPagecount_ValidLine_CorruptsLastField_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        string result = applier.Apply(1, line, "IMG001", "opt-pagecount", anomalies);

        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
        Assert.Equal("opt-pagecount", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_OptPagecount_NoCommaInLine_ReturnsUnchanged_AndDoesNotRecordAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "NO_COMMAS_HERE";

        string result = applier.Apply(1, line, "IMG001", "opt-pagecount", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies); // Currently fails: anomaly recorded
    }

    [Fact]
    public void Apply_OptPath_ValidLine_CorruptsPath_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        string result = applier.Apply(1, line, "IMG001", "opt-path", anomalies);

        Assert.NotEqual(line, result);
        Assert.Contains("invalid", result, StringComparison.Ordinal);
        Assert.Single(anomalies);
        Assert.Equal("opt-path", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_OptPath_InvalidLine_ReturnsUnchanged_AndDoesNotRecordAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001"; // Only 2 fields, need 3

        string result = applier.Apply(1, line, "IMG001", "opt-path", anomalies);

        Assert.Equal(line, result);
        Assert.Empty(anomalies); // Currently fails: anomaly recorded
    }

    [Fact]
    public void Apply_OptBatesNumber_ValidLine_RemovesBates_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        string result = applier.Apply(1, line, "IMG001", "opt-batesnumber", anomalies);

        Assert.NotEqual(line, result);
        Assert.StartsWith(",", result, StringComparison.Ordinal);
        Assert.Single(anomalies);
        Assert.Equal("opt-batesnumber", anomalies[0].ErrorType);
    }

    [Fact]
    public void Apply_OptBatesNumber_NoCommaInLine_ReturnsEmptyString_AndRecordsAnomaly()
    {
        var applier = new OptAnomalyApplier(new Random(42));
        var anomalies = new List<ChaosAnomaly>();
        const string line = "NO_COMMAS";

        string result = applier.Apply(1, line, "IMG001", "opt-batesnumber", anomalies);

        // Current behavior: returns empty string (changed)
        Assert.NotEqual(line, result);
        Assert.Single(anomalies);
    }
}
