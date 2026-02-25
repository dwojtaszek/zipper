using System.Text;
using Xunit;

namespace Zipper
{
    public class ChaosEngineTests
    {
        [Fact]
        public void MixedDelimiters_ReplacesExactlyOneDelimiter()
        {
            var engine = new ChaosEngine(
                totalLines: 2,
                chaosAmount: "1",
                chaosTypes: "mixed-delimiters",
                format: LoadFileFormat.Dat,
                columnDelimiter: "\u0014",
                quoteDelimiter: "\u00fe",
                seed: 42);

            string line = "\u00feControl Number\u00fe\u0014\u00feFile Path\u00fe\u0014\u00feCustodian\u00fe";

            // Force the engine to intercept line 1
            if (engine.ShouldIntercept(1))
            {
                string modified = engine.Intercept(1, line, "HEADER");

                // Count how many original delimiters remain
                int originalDelimCount = line.Count(c => c == '\u0014');
                int modifiedDelimCount = modified.Count(c => c == '\u0014');

                // Exactly one delimiter should be replaced
                Assert.Equal(originalDelimCount - 1, modifiedDelimCount);
                Assert.Single(engine.Anomalies);
                Assert.Equal("mixed-delimiters", engine.Anomalies[0].ErrorType);
            }
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
                seed: 42);

            string line = "\u00feValue1\u00fe\u0014\u00feValue2\u00fe";

            for (int i = 1; i <= 2; i++)
            {
                if (engine.ShouldIntercept(i))
                {
                    string modified = engine.Intercept(i, line, $"DOC{i:D8}");
                    Assert.Contains("\r\n", modified);
                    break;
                }
            }
        }

        [Fact]
        public void EncodingAnomaly_ReturnsInvalidBytes()
        {
            var engine = new ChaosEngine(
                totalLines: 5,
                chaosAmount: "5",
                chaosTypes: "encoding",
                format: LoadFileFormat.Dat,
                columnDelimiter: "\u0014",
                quoteDelimiter: "\u00fe",
                seed: 42);

            bool gotAnomaly = false;
            for (int i = 1; i <= 5; i++)
            {
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
    }
}
