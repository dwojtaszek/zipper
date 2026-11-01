using System.Globalization;
using System.Text;
using Xunit;
using Zipper.LoadFiles;

namespace Zipper.Tests;

public class ChaosEngineTests
{
    [Fact]
    public void Constructor_TotalLinesExceedsIntMax_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ChaosEngine(
            totalLines: (long)int.MaxValue + 1,
            chaosAmount: "1%",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42));

        Assert.Contains("Chaos Engine does not support load files larger than Int32.MaxValue lines", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_OptEncodingTotalLinesExceedsIntMax_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ChaosEngine(
            totalLines: (long)int.MaxValue + 1,
            chaosAmount: "0",
            chaosTypes: "opt-encoding",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42));

        Assert.Contains("Chaos Engine does not support load files larger than Int32.MaxValue lines", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedDelimiters_ReplacesExactlyOneDelimiter()
    {
        var engine = new ChaosEngine(
            totalLines: 1,
            chaosAmount: "1",
            chaosTypes: "mixed-delimiters",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        string line = "\u00feControl Number\u00fe\u0014\u00feFile Path\u00fe\u0014\u00feCustodian\u00fe";

        bool intercepted = false;
        for (int i = 1; i <= 1; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                string modified = engine.Intercept(i, line, "HEADER");

                // Count how many original delimiters remain
                int originalDelimCount = line.Count(c => c == '\u0014');
                int modifiedDelimCount = modified.Count(c => c == '\u0014');

                // Exactly one delimiter should be replaced
                Assert.Equal(originalDelimCount - 1, modifiedDelimCount);
                Assert.Single(engine.Anomalies);
                Assert.Equal("mixed-delimiters", engine.Anomalies[0].ErrorType);
                intercepted = true;
            }
        }

        Assert.True(intercepted, "ChaosEngine should have intercepted at least one line");
    }

    [Fact]
    public void Quotes_DropsClosingQuote()
    {
        var engine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "2",
            chaosTypes: "quotes",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe";

        for (int i = 1; i <= 2; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                string modified = engine.Intercept(i, line, $"DOC{i:D8}");

                int originalQuotes = line.Count(c => c == '\u00fe');
                int modifiedQuotes = modified.Count(c => c == '\u00fe');

                Assert.True(modifiedQuotes < originalQuotes, "Should have fewer quotes after dropping one");
                break;
            }
        }
    }

    [Fact]
    public void Columns_ChangesColumnCount()
    {
        var engine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "2",
            chaosTypes: "columns",
            format: LoadFileFormat.Dat,
            columnDelimiter: "|",
            quoteDelimiter: "\"",
            eol: "\r\n",
            seed: 42);

        string line = "\"Val1\"|\"Val2\"|\"Val3\"";
        int originalDelims = line.Count(c => c == '|');

        for (int i = 1; i <= 2; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                string modified = engine.Intercept(i, line, $"DOC{i:D8}");
                int modifiedDelims = modified.Count(c => c == '|');

                // Column count should be off by 1
                Assert.True(Math.Abs(modifiedDelims - originalDelims) == 1);
                break;
            }
        }
    }

    [Fact]
    public void Eol_InjectsRawNewline()
    {
        var engine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "2",
            chaosTypes: "eol",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\n",
            seed: 42);

        string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe";

        for (int i = 1; i <= 2; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                string modified = engine.Intercept(i, line, $"DOC{i:D8}");
                Assert.Contains("\n", modified, StringComparison.Ordinal);
                Assert.DoesNotContain("\r\n", modified, StringComparison.Ordinal);
                break;
            }
        }
    }

    [Fact]
    public void EncodingAnomaly_ReturnsNullUntilEncodingLineIsIntercepted()
    {
        var engine = new ChaosEngine(
            totalLines: 5,
            chaosAmount: "1",
            chaosTypes: "encoding",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        for (int i = 1; i <= 5; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                continue;
            }

            Assert.Null(engine.GetEncodingAnomaly(i, i + 1, Encoding.UTF8));
        }
    }

    [Fact]
    public void EncodingAnomaly_ReturnsInvalidBytesForInterceptedEncodingLine()
    {
        var engine = new ChaosEngine(
            totalLines: 5,
            chaosAmount: "5",
            chaosTypes: "encoding",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        bool gotAnomaly = false;
        for (int i = 1; i <= 5; i++)
        {
            if (!engine.ShouldIntercept(i))
            {
                continue;
            }

            engine.Intercept(i, "Value1\u0014Value2", $"DOC{i:D8}");
            var bytes = engine.GetEncodingAnomaly(i, i + 1, Encoding.UTF8);
            if (bytes is not null)
            {
                Assert.True(bytes.Length > 0);
                gotAnomaly = true;
                break;
            }
        }

        Assert.True(gotAnomaly, "Should inject at least one encoding anomaly");
    }

    [Fact]
    public void OptEncodingAnomaly_ReturnsUtf8BomBetweenRecordsAndSkipsEndOfFile()
    {
        var engine = new ChaosEngine(
            totalLines: 3,
            chaosAmount: "100%",
            chaosTypes: "opt-encoding",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        for (int lineNumber = 1; lineNumber <= 2; lineNumber++)
        {
            Assert.True(engine.ShouldIntercept(lineNumber));
            Assert.Equal(line, engine.Intercept(lineNumber, line, $"IMG{lineNumber:D3}"));
        }
        Assert.False(engine.ShouldIntercept(3));
        Assert.Empty(engine.Anomalies);

        var bytes = engine.GetEncodingAnomaly(1, 2, Encoding.Latin1);

        Assert.NotNull(bytes);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes);
        var anomaly = Assert.Single(engine.Anomalies);
        Assert.Equal("Boundary 1-2", anomaly.LineNumber);
        Assert.Equal("opt-encoding", anomaly.ErrorType);
        bytes = engine.GetEncodingAnomaly(2, 3, Encoding.Latin1);
        Assert.NotNull(bytes);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes);
        Assert.Null(engine.GetEncodingAnomaly(1, 2, Encoding.Latin1));
        Assert.Null(engine.GetEncodingAnomaly(3, 4, Encoding.Latin1));
        Assert.Equal(2, engine.Anomalies.Count);
    }

    [Fact]
    public void OptEncodingAnomaly_DoesNotEmitBytesAtEndOfSingleRecordFile()
    {
        var engine = new ChaosEngine(
            totalLines: 1,
            chaosAmount: "1",
            chaosTypes: "opt-encoding",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        Assert.False(engine.ShouldIntercept(1));
        Assert.Equal(line, engine.Intercept(1, line, "IMG001"));

        Assert.Null(engine.GetEncodingAnomaly(1, 2, Encoding.Latin1));
        Assert.Empty(engine.Anomalies);
    }

    [Fact]
    public void OptEncodingAnomaly_SamplesOnlyAvailableRecordBoundaries()
    {
        var engine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "100%",
            chaosTypes: "opt-encoding",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);

        Assert.True(engine.ShouldIntercept(1));
        Assert.False(engine.ShouldIntercept(2));
    }

    [Fact]
    public void OptEncodingAnomaly_AtTerminalRecordUsesAnotherEnabledType()
    {
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        foreach (int totalLines in new[] { 2, 3 })
        {
            var engine = new ChaosEngine(
                totalLines,
                chaosAmount: totalLines.ToString(CultureInfo.InvariantCulture),
                chaosTypes: "opt-encoding,opt-boundary",
                format: LoadFileFormat.Opt,
                columnDelimiter: ",",
                quoteDelimiter: string.Empty,
                eol: "\r\n",
                seed: 42);

            for (int lineNumber = 1; lineNumber < totalLines; lineNumber++)
            {
                Assert.True(engine.ShouldIntercept(lineNumber));
                engine.Intercept(lineNumber, line, $"IMG{lineNumber:D3}");
                engine.GetEncodingAnomaly(lineNumber, lineNumber + 1, Encoding.Latin1);
            }

            Assert.True(engine.ShouldIntercept(totalLines));
            string terminalLine = engine.Intercept(totalLines, line, $"IMG{totalLines:D3}");

            Assert.NotEqual(line, terminalLine);
            Assert.Contains(engine.Anomalies, anomaly =>
                anomaly.LineNumber == totalLines.ToString(CultureInfo.InvariantCulture) &&
                anomaly.ErrorType == "opt-boundary");
        }
    }

    [Fact]
    public void OptEncodingAnomaly_IsNotEnabledByDefault()
    {
        var engine = new ChaosEngine(
            totalLines: 100,
            chaosAmount: "100%",
            chaosTypes: null,
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);
        const string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,3";

        for (int lineNumber = 1; lineNumber < 100; lineNumber++)
        {
            Assert.True(engine.ShouldIntercept(lineNumber));
            engine.Intercept(lineNumber, line, $"IMG{lineNumber:D3}");
            Assert.Null(engine.GetEncodingAnomaly(lineNumber, lineNumber + 1, Encoding.Latin1));
        }

        Assert.DoesNotContain(engine.Anomalies, anomaly => anomaly.ErrorType == "opt-encoding");
    }

    [Fact]
    public void OptBoundary_FlipsDocumentBreakFlag()
    {
        var engine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "2",
            chaosTypes: "opt-boundary",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);

        string line = "IMG00000001,VOL001,IMAGES\\IMG00000001.tif,Y,,,3";

        for (int i = 1; i <= 2; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                string modified = engine.Intercept(i, line, "IMG00000001");
                var parts = modified.Split(',');

                // Column 4 (index 3) should have been flipped
                Assert.NotEqual("Y", parts[3]);
                break;
            }
        }
    }

    [Fact]
    public void OptColumns_ChangesCommaCount()
    {
        var engine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "2",
            chaosTypes: "opt-columns",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);

        string line = "IMG00000001,VOL001,IMAGES\\IMG00000001.tif,Y,,,3";
        int originalCommas = line.Count(c => c == ',');

        for (int i = 1; i <= 2; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                string modified = engine.Intercept(i, line, "IMG00000001");
                int modifiedCommas = modified.Count(c => c == ',');

                Assert.NotEqual(originalCommas, modifiedCommas);
                break;
            }
        }
    }

    [Fact]
    public void OptPagecount_CorruptsLastField()
    {
        var engine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "2",
            chaosTypes: "opt-pagecount",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);

        string line = "IMG00000001,VOL001,IMAGES\\IMG00000001.tif,Y,,,3";

        for (int i = 1; i <= 2; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                string modified = engine.Intercept(i, line, "IMG00000001");
                string lastField = modified.Split(',').Last();

                // Should be either "ABC" or "-1"
                Assert.True(string.Equals(lastField, "ABC", StringComparison.Ordinal) || string.Equals(lastField, "-1", StringComparison.Ordinal), $"Last field was '{lastField}'");
                break;
            }
        }
    }

    [Fact]
    public void ChaosAmount_Percentage_SelectsCorrectCount()
    {
        var engine = new ChaosEngine(
            totalLines: 100,
            chaosAmount: "10%",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        int interceptCount = 0;
        for (int i = 1; i <= 100; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                interceptCount++;
            }
        }

        // 10% of 100 = 10
        Assert.Equal(10, interceptCount);
    }

    [Fact]
    public void ChaosAmount_ExactCount_SelectsCorrectCount()
    {
        var engine = new ChaosEngine(
            totalLines: 100,
            chaosAmount: "5",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        int interceptCount = 0;
        for (int i = 1; i <= 100; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                interceptCount++;
            }
        }

        Assert.Equal(5, interceptCount);
    }

    [Fact]
    public void ChaosTypes_Filtering_OnlyUsesSpecifiedTypes()
    {
        var engine = new ChaosEngine(
            totalLines: 10,
            chaosAmount: "10",
            chaosTypes: "quotes",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        string line = "\u00feValue\u00fe";
        for (int i = 1; i <= 10; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                engine.Intercept(i, line, $"DOC{i:D8}");
            }
        }

        // All anomalies should be of type "quotes"
        foreach (var anomaly in engine.Anomalies)
        {
            Assert.Equal("quotes", anomaly.ErrorType);
        }
    }

    [Fact]
    public void QuoteDelimNone_DisablesQuotesChaosType()
    {
        var engine = new ChaosEngine(
            totalLines: 10,
            chaosAmount: "10",
            chaosTypes: "quotes",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: string.Empty, // "none"
            eol: "\r\n",
            seed: 42);

        // With quotes disabled and nothing else enabled, intercept should be a no-op
        string line = "Value1\u0014Value2";
        for (int i = 1; i <= 10; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                string modified = engine.Intercept(i, line, $"DOC{i:D8}");

                // With no enabled types, line should be unchanged
                Assert.Equal(line, modified);
            }
        }
    }

    [Fact]
    public void OptPath_CorruptsImagePath()
    {
        var engine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "2",
            chaosTypes: "opt-path",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);

        string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,1";

        for (int i = 1; i <= 2; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                string modified = engine.Intercept(i, line, "IMG001");
                var parts = modified.Split(',');
                Assert.Contains("invalid", parts[2], StringComparison.Ordinal);
                break;
            }
        }
    }

    [Fact]
    public void OptBatesNumber_RemovesBatesNumber()
    {
        var engine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "2",
            chaosTypes: "opt-batesnumber",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);

        string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,1";

        for (int i = 1; i <= 2; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                string modified = engine.Intercept(i, line, "IMG001");
                Assert.StartsWith(",VOL001", modified, StringComparison.Ordinal);
                break;
            }
        }
    }

    [Fact]
    public void EncodingAnomaly_SingleAuditEntry()
    {
        var engine = new ChaosEngine(
            totalLines: 1,
            chaosAmount: "1",
            chaosTypes: "encoding",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        string line = "Value1\u0014Value2";
        if (engine.ShouldIntercept(1))
        {
            // Calling Intercept should not add an anomaly for "encoding" type
            engine.Intercept(1, line, "DOC001");
            Assert.Empty(engine.Anomalies);

            // Getting the anomaly byte array should add exactly one
            engine.GetEncodingAnomaly(1, 2, Encoding.UTF8);
            Assert.Single(engine.Anomalies);
        }
    }

    [Fact]
    public void ChaosAmount_IntMaxBoundary_SelectsExactCountWithinRange()
    {
        // Verify Floyd's algorithm works at scale using 100k lines
        // (int.MaxValue not iterable, but 100k exercises same code path)
        var engine = new ChaosEngine(
            totalLines: 100_000,
            chaosAmount: "1",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        int interceptCount = 0;
        for (int i = 1; i <= 100_000; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                interceptCount++;
            }
        }

        Assert.Equal(1, interceptCount);
    }

    [Fact]
    public void Constructor_TotalLinesZero_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ChaosEngine(
            totalLines: 0,
            chaosAmount: "1%",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42));

        Assert.Contains("Chaos Engine requires a positive totalLines count", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Constructor_TotalLinesOne_SelectsCorrectCount()
    {
        var engine = new ChaosEngine(
            totalLines: 1,
            chaosAmount: "1",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        Assert.True(engine.ShouldIntercept(1));
    }

    [Fact]
    public void ChaosAmount_ZeroPercent_ReturnsZero()
    {
        var engine = new ChaosEngine(
            totalLines: 100,
            chaosAmount: "0%",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        int interceptCount = 0;
        for (int i = 1; i <= 100; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                interceptCount++;
            }
        }

        Assert.Equal(0, interceptCount);
    }

    [Fact]
    public void ChaosAmount_HundredPercent_AllLinesCorrupted()
    {
        var engine = new ChaosEngine(
            totalLines: 50,
            chaosAmount: "100%",
            chaosTypes: "quotes",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        int interceptCount = 0;
        for (int i = 1; i <= 50; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                interceptCount++;
            }
        }

        Assert.Equal(50, interceptCount);
    }

    [Fact]
    public void ChaosAmount_ExceedsTotalLines_ClampedToTotal()
    {
        var engine = new ChaosEngine(
            totalLines: 10,
            chaosAmount: "100",
            chaosTypes: "quotes",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        int interceptCount = 0;
        for (int i = 1; i <= 10; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                interceptCount++;
            }
        }

        Assert.Equal(10, interceptCount);
    }

    [Fact]
    public void ChaosAmount_InvalidString_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => new ChaosEngine(
            totalLines: 200,
            chaosAmount: "abc",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42));

        Assert.Contains("Invalid chaos amount format", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChaosTypes_EmptyString_AllTypesEnabled()
    {
        var engine = new ChaosEngine(
            totalLines: 100,
            chaosAmount: "10",
            chaosTypes: string.Empty,
            format: LoadFileFormat.Dat,
            columnDelimiter: "|",
            quoteDelimiter: "\"",
            eol: "\r\n",
            seed: 42,
            dateColumnNames: ["Date Created"]);

        var record = new LoadFileRecord
        {
            Columns = ["Control Number", "File Path", "Date Created", "Author"],
            Values = ["DOC00000001", "NATIVES\\001\\DOC00000001.pdf", "2024-01-15", "Jane Doe"],
            RecordId = "DOC00000001",
        };
        var serializer = new DatSerializer('|', '"', "~");
        string line = serializer.RenderRecord(record);

        int interceptedCount = 0;
        for (int i = 1; i <= 100; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                engine.Intercept(i, line, record.RecordId, record, serializer);
                _ = engine.GetEncodingAnomaly(i, i + 1, Encoding.UTF8);
                interceptedCount++;
            }
        }

        Assert.Equal(10, interceptedCount);
        var actualTypes = engine.Anomalies.Select(anomaly => anomaly.ErrorType).ToHashSet(StringComparer.Ordinal);
        Assert.True(new HashSet<string>(ChaosAnomalyTypes.Dat, StringComparer.Ordinal).SetEquals(actualTypes));
    }

    [Fact]
    public void AnomalyLineNumbers_AllMatchShouldInterceptTargets()
    {
        // Verifies that every numeric LineNumber in Anomalies corresponds to a line
        // where ShouldIntercept returned true — the invariant the E2E tests rely on.
        const int totalLines = 100;
        var engine = new ChaosEngine(
            totalLines: totalLines,
            chaosAmount: "10",
            chaosTypes: "mixed-delimiters,quotes,columns,eol",
            format: LoadFileFormat.Dat,
            columnDelimiter: "\u0014",
            quoteDelimiter: "\u00fe",
            eol: "\r\n",
            seed: 42);

        string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe\u0014\u00feValue3\u00fe";
        var interceptedLines = new HashSet<long>();

        for (int i = 1; i <= totalLines; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                engine.Intercept(i, line, $"DOC{i:D8}");
                interceptedLines.Add(i);
            }
        }

        // Every numeric line number in Anomalies must be an intercepted line
        foreach (var anomaly in engine.Anomalies)
        {
            if (long.TryParse(anomaly.LineNumber, System.Globalization.CultureInfo.InvariantCulture, out var lineNum))
            {
                Assert.Contains(lineNum, interceptedLines);
            }
        }

        // Count should match (each intercepted line produces exactly one anomaly for non-encoding types)
        Assert.Equal(interceptedLines.Count, engine.Anomalies.Count);
    }

    [Fact]
    public void AnomalyLineNumbers_NonInterceptedLines_HaveNoAnomalies()
    {
        // Verifies that lines NOT selected by ShouldIntercept appear in no anomaly record.
        const int totalLines = 50;
        var engine = new ChaosEngine(
            totalLines: totalLines,
            chaosAmount: "5",
            chaosTypes: "columns",
            format: LoadFileFormat.Dat,
            columnDelimiter: "|",
            quoteDelimiter: "\"",
            eol: "\r\n",
            seed: 42);

        string line = "\"Val1\"|\"Val2\"|\"Val3\"";

        for (int i = 1; i <= totalLines; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                engine.Intercept(i, line, $"DOC{i:D8}");
            }
        }

        var anomalyLines = engine.Anomalies
            .Select(a => long.TryParse(a.LineNumber, System.Globalization.CultureInfo.InvariantCulture, out var n) ? (long?)n : null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToHashSet();

        // Non-intercepted lines must not appear in any anomaly
        for (int i = 1; i <= totalLines; i++)
        {
            if (!engine.ShouldIntercept(i))
            {
                Assert.DoesNotContain((long)i, anomalyLines);
            }
        }
    }

    [Theory]
    [InlineData(",VOL001,IMAGES\\IMG001.tif,Y,,,1", ",VOL001,IMAGES\\IMG001.tif,Y,,,1")]
    [InlineData("NO_COMMAS_HERE", "")]
    public void OptBatesNumber_WithEdgeCaseLines_HandlesCorrectly(string input, string expected)
    {
        var engine = new ChaosEngine(1, "1", "opt-batesnumber", LoadFileFormat.Opt, ",", "", "\n", 42);
        var result = engine.Intercept(1, input, "DOC");
        Assert.Equal(expected, result);
    }

    // Issue 1: Chaos types × load-file format silently no-op
    [Fact]
    public void Constructor_MultiFormatChaosTypes_EmptyTypesForOpt_ProducesNoAnomalies()
    {
        // When chaos types contain only DAT-specific types (e.g., "mixed-delimiters"),
        // and the engine is constructed for OPT format, the enabled types set becomes empty.
        // This results in 0 anomalies being produced (silent no-op).
        // The CLI validation (CrossCuttingRules) now rejects this combination,
        // but the engine itself gracefully handles empty types by not intercepting.
        var engine = new ChaosEngine(
            totalLines: 10,
            chaosAmount: "10",
            chaosTypes: "mixed-delimiters",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);

        string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,1";
        int interceptedCount = 0;
        for (int i = 1; i <= 10; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                engine.Intercept(i, line, $"DOC{i:D8}");
                interceptedCount++;
            }
        }

        // With empty enabledTypes, Intercept returns line unchanged and records no anomalies
        Assert.Empty(engine.Anomalies);
        Assert.True(interceptedCount >= 0); // Sampler may still select lines, but Intercept is no-op
    }

    [Fact]
    public void Constructor_ValidTypesForFormat_ProducesAnomalies()
    {
        // Types valid for the format should produce anomalies
        var engine = new ChaosEngine(
            totalLines: 10,
            chaosAmount: "10",
            chaosTypes: "opt-boundary",
            format: LoadFileFormat.Opt,
            columnDelimiter: ",",
            quoteDelimiter: string.Empty,
            eol: "\r\n",
            seed: 42);

        string line = "IMG001,VOL001,IMAGES\\IMG001.tif,Y,,,1";
        int interceptedCount = 0;
        for (int i = 1; i <= 10; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                engine.Intercept(i, line, $"DOC{i:D8}");
                interceptedCount++;
            }
        }

        Assert.Equal(interceptedCount, engine.Anomalies.Count);
        Assert.All(engine.Anomalies, a => Assert.Equal("opt-boundary", a.ErrorType));
    }

    [Theory]
    [InlineData("path-pointer", "File Path")]
    [InlineData("field-length", "Author")]
    [InlineData("date-format", "Date Created")]
    [InlineData("null-byte", "Author")]
    [InlineData("trailing-empties", "Trailing empty columns after Author")]
    public void DatRecordAnomalies_OnlyInterceptDataRecords_AndAuditTheColumn(string type, string expectedColumn)
    {
        IReadOnlyList<string> columns = ["Control Number", "File Path", "Date Created", "Author"];
        var record = new LoadFileRecord
        {
            Columns = columns,
            Values = ["DOC00000001", "NATIVES\\001\\DOC00000001.pdf", "2024-01-15", "Jane Doe"],
            RecordId = "DOC00000001",
        };
        var serializer = new DatSerializer('|', '"', "~");
        string line = serializer.RenderRecord(record);
        var engine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "1",
            chaosTypes: type,
            format: LoadFileFormat.Dat,
            columnDelimiter: "|",
            quoteDelimiter: "\"",
            eol: "\r\n",
            seed: 42,
            dateColumnNames: ["Date Created"]);

        Assert.False(engine.ShouldIntercept(1), "Record anomalies must never target the header.");
        Assert.True(engine.ShouldIntercept(2), "An exact count should target the sole data record.");

        string result = engine.Intercept(2, line, record.RecordId, record, serializer);

        Assert.NotEqual(line, result);
        var anomaly = Assert.Single(engine.Anomalies);
        Assert.Equal(type, anomaly.ErrorType);
        Assert.Equal(expectedColumn, anomaly.Column);
        Assert.Equal(record.RecordId, anomaly.RecordID);
    }

    [Theory]
    [InlineData("path-pointer")]
    [InlineData("field-length")]
    [InlineData("date-format")]
    [InlineData("null-byte")]
    [InlineData("trailing-empties")]
    public void DatRecordAnomalies_HeaderOnlyFile_DoesNotInterceptHeader(string type)
    {
        var engine = new ChaosEngine(
            totalLines: 1,
            chaosAmount: "1",
            chaosTypes: type,
            format: LoadFileFormat.Dat,
            columnDelimiter: "|",
            quoteDelimiter: "\"",
            eol: "\r\n",
            seed: 42);

        Assert.False(engine.ShouldIntercept(1));
        Assert.Empty(engine.Anomalies);
    }

    [Fact]
    public void DatRecordAnomalies_MixedWithLegacyTypes_OnlyInterceptDataRecords()
    {
        var engine = new ChaosEngine(
            totalLines: 5,
            chaosAmount: "100%",
            chaosTypes: "mixed-delimiters,path-pointer",
            format: LoadFileFormat.Dat,
            columnDelimiter: "|",
            quoteDelimiter: "\"",
            eol: "\r\n",
            seed: 42);
        var serializer = new DatSerializer('|', '"', "~");

        Assert.False(engine.ShouldIntercept(1));
        for (int lineNumber = 2; lineNumber <= 5; lineNumber++)
        {
            Assert.True(engine.ShouldIntercept(lineNumber));
            var record = new LoadFileRecord
            {
                Columns = ["File Path", "Author"],
                Values = [$"NATIVES\\001\\DOC{lineNumber.ToString("D8", CultureInfo.InvariantCulture)}.pdf", "Jane Doe"],
                RecordId = $"DOC{lineNumber.ToString("D8", CultureInfo.InvariantCulture)}",
            };
            string line = serializer.RenderRecord(record);

            string result = engine.Intercept(lineNumber, line, record.RecordId, record, serializer);

            Assert.NotEqual(line, result);
        }

        Assert.Equal(2, engine.Anomalies.Count(anomaly => anomaly.ErrorType == "mixed-delimiters"));
        Assert.Equal(2, engine.Anomalies.Count(anomaly => anomaly.ErrorType == "path-pointer"));
    }

    [Fact]
    public void DatDefaultAnomalies_PreservePreviouslyEnabledTypes()
    {
        const int totalLines = 100;
        var engine = new ChaosEngine(
            totalLines,
            chaosAmount: "10",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "|",
            quoteDelimiter: "\"",
            eol: "\r\n",
            seed: 42);
        var legacyEngine = new ChaosEngine(
            totalLines,
            chaosAmount: "10",
            chaosTypes: "mixed-delimiters,quotes,columns,eol,encoding",
            format: LoadFileFormat.Dat,
            columnDelimiter: "|",
            quoteDelimiter: "\"",
            eol: "\r\n",
            seed: 42);
        var headerEngine = new ChaosEngine(
            totalLines: 2,
            chaosAmount: "100%",
            chaosTypes: null,
            format: LoadFileFormat.Dat,
            columnDelimiter: "|",
            quoteDelimiter: "\"",
            eol: "\r\n",
            seed: 42);
        Assert.True(headerEngine.ShouldIntercept(1));
        Assert.True(headerEngine.ShouldIntercept(2));
        string line = "\"DOC00000001\"|\"NATIVES\\DOC00000001.pdf\"|\"2024-01-15\"|\"Jane Doe\"";

        for (int i = 1; i <= totalLines; i++)
        {
            bool shouldIntercept = engine.ShouldIntercept(i);
            Assert.Equal(legacyEngine.ShouldIntercept(i), shouldIntercept);
            if (shouldIntercept)
            {
                string recordId = $"DOC{i:D8}";
                Assert.Equal(
                    legacyEngine.Intercept(i, line, recordId),
                    engine.Intercept(i, line, recordId));
                _ = engine.GetEncodingAnomaly(i, i + 1, Encoding.UTF8);
                _ = legacyEngine.GetEncodingAnomaly(i, i + 1, Encoding.UTF8);
            }
        }

        Assert.Equal(10, engine.Anomalies.Count);
        Assert.DoesNotContain(engine.Anomalies, anomaly =>
            ChaosAnomalyTypes.DatRecordOnly.Contains(anomaly.ErrorType, StringComparer.Ordinal));
        Assert.Equal(
            legacyEngine.Anomalies.Select(anomaly => (anomaly.LineNumber, anomaly.ErrorType, anomaly.Column, anomaly.Description)),
            engine.Anomalies.Select(anomaly => (anomaly.LineNumber, anomaly.ErrorType, anomaly.Column, anomaly.Description)));
    }

    [Fact]
    public void Intercept_CsvDefaults_OnlyProducesCsvAnomalies()
    {
        var engine = new ChaosEngine(5, "5", null, LoadFileFormat.Csv, ",", "\"", "\r\n", 42);
        const string line = "\"say \"\"hi\"\"\",value";

        for (int i = 1; i <= 5; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                _ = engine.Intercept(i, line, $"DOC{i:D8}");
            }
        }

        Assert.NotEmpty(engine.Anomalies);
        Assert.All(engine.Anomalies, anomaly => Assert.Contains(anomaly.ErrorType, ChaosAnomalyTypes.Csv));
    }

    [Fact]
    public void Intercept_ConcordanceDefaults_OnlyProducesConcordanceAnomalies()
    {
        var engine = new ChaosEngine(5, "5", null, LoadFileFormat.Concordance, "\u0014", "\u00fe", "\r\n", 42);
        const string line = "\u00feValue\u00fe\u0014\u00feOther\u00fe";

        for (int i = 1; i <= 5; i++)
        {
            if (engine.ShouldIntercept(i))
            {
                _ = engine.Intercept(i, line, $"DOC{i:D8}");
            }
        }

        Assert.NotEmpty(engine.Anomalies);
        Assert.All(engine.Anomalies, anomaly => Assert.Contains(anomaly.ErrorType, ChaosAnomalyTypes.Concordance));
    }

    [Fact]
    public void ApplyXmlChaos_WithUnclosedTag_ProducesMalformedXml_AndAuditsAnomaly()
    {
        var engine = new ChaosEngine(2, "1", "xml-unclosed-tag", LoadFileFormat.EdrmXml, "|", "\"", "\r\n", 42);
        const string xml = "<Root><Batch><Document DocID=\"DOC1\"><Tags/></Document><Document DocID=\"DOC2\"><Tags/></Document></Batch></Root>";

        string result = engine.ApplyXmlChaos(xml);

        Assert.NotEqual(xml, result);
        var anomaly = Assert.Single(engine.Anomalies);
        Assert.Equal("xml-unclosed-tag", anomaly.ErrorType);
        Assert.Contains(anomaly.RecordID, new[] { "DOC1", "DOC2" });
        Assert.Throws<System.Xml.XmlException>(() => System.Xml.Linq.XDocument.Parse(result));
    }

    [Fact]
    public void ApplyXmlChaos_WithBadEntity_ProducesMalformedXml_AndAuditsAnomaly()
    {
        var engine = new ChaosEngine(2, "1", "xml-bad-entity", LoadFileFormat.EdrmXml, "|", "\"", "\r\n", 42);
        const string xml = "<Root><Batch><Document DocID=\"DOC1\"><Tags/></Document><Document DocID=\"DOC2\"><Tags/></Document></Batch></Root>";

        string result = engine.ApplyXmlChaos(xml);

        Assert.Contains("&chaos;", result, StringComparison.Ordinal);
        var anomaly = Assert.Single(engine.Anomalies);
        Assert.Equal("xml-bad-entity", anomaly.ErrorType);
        Assert.Throws<System.Xml.XmlException>(() => System.Xml.Linq.XDocument.Parse(result));
    }

    [Fact]
    public void ApplyXmlChaos_WithExactCount_InjectsAnomalyIntoEachTargetedDocument()
    {
        var engine = new ChaosEngine(2, "2", "xml-bad-entity", LoadFileFormat.EdrmXml, "|", "\"", "\r\n", 42);
        const string xml = "<Root><Batch><Document DocID=\"DOC1\"><Tags/></Document><Document DocID=\"DOC2\"><Tags/></Document></Batch></Root>";

        string result = engine.ApplyXmlChaos(xml);

        Assert.Equal(2, engine.Anomalies.Count);
        Assert.Equal(new[] { "DOC1", "DOC2" }, engine.Anomalies.Select(anomaly => anomaly.RecordID));
        Assert.Equal(2, result.Split("&chaos;", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void ApplyXmlChaos_WithEncodingLie_ChangesDeclaration_AndAuditsAnomaly()
    {
        var engine = new ChaosEngine(1, "1", "xml-encoding-lie", LoadFileFormat.EdrmXml, "|", "\"", "\r\n", 42);
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?><Root><Batch><Document DocID=\"DOC1\"><Tags/></Document></Batch></Root>";

        string result = engine.ApplyXmlChaos(xml);

        Assert.Contains("encoding=\"utf-16\"", result, StringComparison.Ordinal);
        Assert.DoesNotContain("encoding=\"utf-8\"", result, StringComparison.Ordinal);
        var anomaly = Assert.Single(engine.Anomalies);
        Assert.Equal("xml-encoding-lie", anomaly.ErrorType);
        Assert.Equal("XML declaration", anomaly.Column);
    }

    [Fact]
    public void ApplyXmlChaos_WithEncodingLieAcrossMultipleDocuments_AuditsOneFileWideAnomaly()
    {
        var engine = new ChaosEngine(2, "100%", "xml-encoding-lie", LoadFileFormat.EdrmXml, "|", "\"", "\r\n", 42);
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?><Root><Batch><Document DocID=\"DOC1\"><Tags/></Document><Document DocID=\"DOC2\"><Tags/></Document></Batch></Root>";

        string result = engine.ApplyXmlChaos(xml);

        Assert.Contains("encoding=\"utf-16\"", result, StringComparison.Ordinal);
        var anomaly = Assert.Single(engine.Anomalies);
        Assert.Equal("xml-encoding-lie", anomaly.ErrorType);
        Assert.Equal("1", anomaly.LineNumber);
        Assert.Equal("N/A", anomaly.RecordID);
        Assert.Equal("XML declaration", anomaly.Column);
    }

    [Fact]
    public void CsvBom_WithEveryRecordTargeted_DoesNotInjectAtEndOfFile()
    {
        var engine = new ChaosEngine(3, "100%", "csv-bom", LoadFileFormat.Csv, ",", "\"", "\r\n", 42);

        for (int lineNumber = 1; lineNumber <= 3; lineNumber++)
        {
            if (engine.ShouldIntercept(lineNumber))
            {
                _ = engine.Intercept(lineNumber, "value,field", $"DOC{lineNumber:D8}");
            }

            _ = engine.GetEncodingAnomaly(lineNumber, lineNumber + 1, Encoding.UTF8);
        }

        Assert.Equal(2, engine.Anomalies.Count);
        Assert.DoesNotContain(engine.Anomalies, anomaly => anomaly.LineNumber == "Boundary 3-4");
    }

    [Fact]
    public void Intercept_CsvBomWithOtherAnomalyTypes_KeepsLastRecordTargetable()
    {
        var engine = new ChaosEngine(3, "100%", "csv-bom,csv-quote-escape", LoadFileFormat.Csv, ",", "\"", "\r\n", 42);

        for (int lineNumber = 1; lineNumber <= 3; lineNumber++)
        {
            if (engine.ShouldIntercept(lineNumber))
            {
                _ = engine.Intercept(lineNumber, "value,field", $"DOC{lineNumber:D8}");
            }

            _ = engine.GetEncodingAnomaly(lineNumber, lineNumber + 1, Encoding.UTF8);
        }

        Assert.Contains(engine.Anomalies, anomaly => anomaly.LineNumber == "3" && anomaly.ErrorType == "csv-quote-escape");
    }

    [Fact]
    public void Intercept_CsvDefaults_IncludesCsvBomAnomaly()
    {
        var engine = new ChaosEngine(6, "100%", null, LoadFileFormat.Csv, ",", "\"", "\r\n", 42);

        for (int lineNumber = 1; lineNumber <= 6; lineNumber++)
        {
            if (engine.ShouldIntercept(lineNumber))
            {
                _ = engine.Intercept(lineNumber, "value,field", $"DOC{lineNumber:D8}");
            }

            _ = engine.GetEncodingAnomaly(lineNumber, lineNumber + 1, Encoding.UTF8);
        }

        Assert.Contains(engine.Anomalies, anomaly => anomaly.ErrorType == "csv-bom");
    }

    [Fact]
    public void Constructor_EmailTotalItemsExceedsIntMax_ThrowsArgumentOutOfRangeException()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => new ChaosEngine(
            totalItems: (long)int.MaxValue + 1,
            chaosAmount: "1%",
            chaosTypes: null,
            seed: 42));

        Assert.Contains("Chaos Engine does not support load files larger than Int32.MaxValue lines", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmailChaos_ShouldInterceptAndApplyAnomalies()
    {
        var engine = new ChaosEngine(5, "100%", "encoded-word,header-fold", seed: 42);
        byte[] emailBytes = Encoding.UTF8.GetBytes("From: a@b.com\r\nTo: c@d.com\r\nSubject: Hello\r\n\r\nBody\r\n");

        for (int i = 1; i <= 5; i++)
        {
            var content = new GeneratedFileContent { Content = emailBytes };
            var item = new FileWorkItem { Index = i, FileName = $"DOC{i:D4}.eml" };
            var result = engine.ApplyEmailChaos(item, content);
            Assert.True(result.Content.Length > 0);
        }

        Assert.Equal(5, engine.Anomalies.Count);
        Assert.All(engine.Anomalies, a =>
        {
            Assert.True(a.ErrorType is "encoded-word" or "header-fold");
            Assert.NotNull(a.File);
            Assert.NotNull(a.Location);
        });
    }

    [Fact]
    public void EmailChaos_WhenAmountIsZero_DoesNotMutateAndKeepsOutputIdentical()
    {
        var engine = new ChaosEngine(3, "0", "full-chaos", seed: 42);
        byte[] emailBytes = Encoding.UTF8.GetBytes("From: a@b.com\r\nTo: c@d.com\r\nSubject: Hello\r\n\r\nBody\r\n");

        for (int i = 1; i <= 3; i++)
        {
            var content = new GeneratedFileContent { Content = emailBytes };
            var item = new FileWorkItem { Index = i, FileName = $"DOC{i:D4}.eml" };
            var result = engine.ApplyEmailChaos(item, content);
            Assert.Same(emailBytes, result.Content);
        }

        Assert.Empty(engine.Anomalies);
    }

    [Fact]
    public void EmailChaos_IsDeterministicForSameSeed()
    {
        var engine1 = new ChaosEngine(10, "50%", null, seed: 99);
        var engine2 = new ChaosEngine(10, "50%", null, seed: 99);
        byte[] emailBytes = Encoding.UTF8.GetBytes("From: a@b.com\r\nTo: c@d.com\r\nSubject: Hello\r\n\r\nBody\r\n");

        for (int i = 1; i <= 10; i++)
        {
            var c1 = new GeneratedFileContent { Content = (byte[])emailBytes.Clone() };
            var c2 = new GeneratedFileContent { Content = (byte[])emailBytes.Clone() };
            var item = new FileWorkItem { Index = i, FileName = $"DOC{i:D4}.eml" };

            var r1 = engine1.ApplyEmailChaos(item, c1);
            var r2 = engine2.ApplyEmailChaos(item, c2);

            Assert.Equal(r1.Content, r2.Content);
        }

        Assert.Equal(engine1.Anomalies.Count, engine2.Anomalies.Count);
        for (int i = 0; i < engine1.Anomalies.Count; i++)
        {
            Assert.Equal(engine1.Anomalies[i].ErrorType, engine2.Anomalies[i].ErrorType);
            Assert.Equal(engine1.Anomalies[i].LineNumber, engine2.Anomalies[i].LineNumber);
            Assert.Equal(engine1.Anomalies[i].Description, engine2.Anomalies[i].Description);
        }
    }

    private static FileWorkItem EmlWorkItem(long index) =>
        new() { Index = index, FileName = $"DOC{index:D8}.eml" };

    private static GeneratedFileContent EmlContent() =>
        new() { Content = Encoding.UTF8.GetBytes("From: a@b.com\r\nTo: c@d.com\r\nSubject: Hello\r\n\r\nBody\r\n") };

    [Fact]
    public void ApplyEmailChaos_ResultDoesNotDependOnCallOrder_ForSameSeed()
    {
        const int count = 40;
        var forward = new ChaosEngine(count, "50%", null, seed: 7);
        var reverse = new ChaosEngine(count, "50%", null, seed: 7);

        var forwardOutputs = new byte[count + 1][];
        for (int i = 1; i <= count; i++)
        {
            forwardOutputs[i] = forward.ApplyEmailChaos(EmlWorkItem(i), EmlContent()).Content;
        }

        var reverseOutputs = new byte[count + 1][];
        for (int i = count; i >= 1; i--)
        {
            reverseOutputs[i] = reverse.ApplyEmailChaos(EmlWorkItem(i), EmlContent()).Content;
        }

        for (int i = 1; i <= count; i++)
        {
            Assert.Equal(forwardOutputs[i], reverseOutputs[i]);
        }

        Assert.Equal(forward.Anomalies, reverse.Anomalies);
    }

    [Fact]
    public void ApplyEmailChaos_ConcurrentCalls_MatchSequentialOutputAndAudit()
    {
        const int count = 200;
        var sequential = new ChaosEngine(count, "30%", null, seed: 11);
        var concurrent = new ChaosEngine(count, "30%", null, seed: 11);

        var expected = new byte[count + 1][];
        for (int i = 1; i <= count; i++)
        {
            expected[i] = sequential.ApplyEmailChaos(EmlWorkItem(i), EmlContent()).Content;
        }

        var actual = new byte[count + 1][];
        Parallel.For(1, count + 1, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            actual[i] = concurrent.ApplyEmailChaos(EmlWorkItem(i), EmlContent()).Content;
        });

        for (int i = 1; i <= count; i++)
        {
            Assert.Equal(expected[i], actual[i]);
        }

        Assert.Equal(sequential.Anomalies, concurrent.Anomalies);
    }

    [Fact]
    public void ApplyEmailChaos_Anomalies_AreAuditedInWorkItemOrder()
    {
        const int count = 25;
        var engine = new ChaosEngine(count, "100%", null, seed: 3);

        for (int i = count; i >= 1; i--)
        {
            engine.ApplyEmailChaos(EmlWorkItem(i), EmlContent());
        }

        var expectedFiles = Enumerable.Range(1, count).Select(i => $"DOC{i:D8}.eml").ToList();
        Assert.Equal(expectedFiles, engine.Anomalies.Select(a => a.File));
    }

    [Fact]
    public void ApplyEmailChaos_UnsampledItems_ReturnTheInputBytesUnchanged()
    {
        var engine = new ChaosEngine(10, "2", null, seed: 5);
        var inputs = new List<GeneratedFileContent>();
        var results = new List<GeneratedFileContent>();

        for (int i = 1; i <= 10; i++)
        {
            var input = EmlContent();
            inputs.Add(input);
            results.Add(engine.ApplyEmailChaos(EmlWorkItem(i), input));
        }

        int untouched = results.Zip(inputs).Count(pair => ReferenceEquals(pair.First.Content, pair.Second.Content));
        Assert.Equal(8, untouched);
        Assert.Equal(2, engine.Anomalies.Count);
    }

    [Fact]
    public void ApplyEmailChaos_WhenNoEmailTypeIsSelected_LeavesEveryItemUnchanged()
    {
        var engine = new ChaosEngine(5, "100%", "quotes", seed: 1);
        var input = EmlContent();

        for (int i = 1; i <= 5; i++)
        {
            Assert.Same(input.Content, engine.ApplyEmailChaos(EmlWorkItem(i), input).Content);
        }

        Assert.Empty(engine.Anomalies);
    }

    [Fact]
    public void ApplyEmailChaos_FullSample_ExercisesEveryEmailType()
    {
        var engine = new ChaosEngine(200, "100%", null, seed: 9);

        for (int i = 1; i <= 200; i++)
        {
            engine.ApplyEmailChaos(EmlWorkItem(i), EmlContent());
        }

        var observedTypes = engine.Anomalies.Select(a => a.ErrorType).Distinct().OrderBy(t => t, StringComparer.Ordinal);
        Assert.Equal(ChaosAnomalyTypes.Email.OrderBy(t => t, StringComparer.Ordinal), observedTypes);
    }

    [Fact]
    public void ApplyEmailChaos_WithItemIndexes_SamplesOnlyEligibleItems()
    {
        IReadOnlyList<long> eligible = new long[] { 2, 5, 9 };
        var engine = new ChaosEngine(eligible.Count, "100%", null, seed: 4, itemIndexes: eligible);
        var input = EmlContent();

        var mutatedIndexes = new List<long>();
        for (int i = 1; i <= 10; i++)
        {
            var result = engine.ApplyEmailChaos(EmlWorkItem(i), input);
            if (!ReferenceEquals(result.Content, input.Content))
            {
                mutatedIndexes.Add(i);
            }
        }

        Assert.Equal(eligible, mutatedIndexes);
        Assert.Equal(eligible.Select(i => $"DOC{i:D8}.eml"), engine.Anomalies.Select(a => a.File));
    }

    [Fact]
    public void ApplyEmailChaos_FullSample_AssignsTypesRoundRobinInWorkItemOrder()
    {
        var engine = new ChaosEngine(8, "100%", null, seed: 9);

        for (int i = 1; i <= 8; i++)
        {
            engine.ApplyEmailChaos(EmlWorkItem(i), EmlContent());
        }

        var sortedTypes = ChaosAnomalyTypes.Email.OrderBy(t => t, StringComparer.Ordinal).ToArray();
        var expected = Enumerable.Range(0, 8).Select(i => sortedTypes[i % sortedTypes.Length]).ToArray();
        Assert.Equal(expected, engine.Anomalies.Select(a => a.ErrorType).ToArray());
    }
}
