using System.Text;
using Xunit;

namespace Zipper
{
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

            Assert.Contains("Chaos Engine does not support load files larger than Int32.MaxValue lines", ex.Message);
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
                    Assert.Contains("\n", modified);
                    Assert.DoesNotContain("\r\n", modified);
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
                if (bytes != null)
                {
                    Assert.True(bytes.Length > 0);
                    gotAnomaly = true;
                    break;
                }
            }

            Assert.True(gotAnomaly, "Should inject at least one encoding anomaly");
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
                    Assert.True(lastField == "ABC" || lastField == "-1", $"Last field was '{lastField}'");
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
                    Assert.Contains("invalid", parts[2]);
                    break;
                }
            }
        }

        [Fact]
        public void OptBatesId_RemovesBatesNumber()
        {
            var engine = new ChaosEngine(
                totalLines: 2,
                chaosAmount: "2",
                chaosTypes: "opt-batesid",
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
                    Assert.StartsWith(",VOL001", modified);
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

            Assert.Contains("Chaos Engine requires a positive totalLines count", ex.Message);
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
        public void ChaosAmount_ZeroPercent_ClampedToMinimumOne()
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

            Assert.Equal(1, interceptCount);
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
        public void ChaosAmount_InvalidString_FallsBackToDefault()
        {
            var engine = new ChaosEngine(
                totalLines: 200,
                chaosAmount: "abc",
                chaosTypes: null,
                format: LoadFileFormat.Dat,
                columnDelimiter: "\u0014",
                quoteDelimiter: "\u00fe",
                eol: "\r\n",
                seed: 42);

            int interceptCount = 0;
            for (int i = 1; i <= 200; i++)
            {
                if (engine.ShouldIntercept(i))
                {
                    interceptCount++;
                }
            }

            Assert.Equal(2, interceptCount);
        }

        [Fact]
        public void ChaosTypes_EmptyString_AllTypesEnabled()
        {
            var engine = new ChaosEngine(
                totalLines: 100,
                chaosAmount: "10",
                chaosTypes: string.Empty,
                format: LoadFileFormat.Dat,
                columnDelimiter: "\u0014",
                quoteDelimiter: "\u00fe",
                eol: "\r\n",
                seed: 42);

            string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe";

            int interceptedCount = 0;
            for (int i = 1; i <= 100; i++)
            {
                if (engine.ShouldIntercept(i))
                {
                    engine.Intercept(i, line, $"DOC{i:D8}");
                    interceptedCount++;
                }
            }

            Assert.Equal(10, interceptedCount);
            Assert.True(engine.Anomalies.Count > 0);

            var types = engine.Anomalies.Select(a => a.ErrorType).Distinct().ToList();
            Assert.True(
                types.Count >= 2,
                $"Expected at least 2 distinct anomaly types with all types enabled, got {types.Count}: {string.Join(", ", types)}");
        }
    }
}
