using Xunit;
using Zipper.Cli;

namespace Zipper.Tests
{
    [Collection("ConsoleTests")]
    public class CliParserTests
    {
        [Fact]
        public void Parse_RequiredArgs_ParsesCorrectly()
        {
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", "/tmp/test" };
            var result = CliParser.Parse(args);
            Assert.NotNull(result);
            Assert.Equal("pdf", result!.FileType);
            Assert.Equal(100, result.Count);
        }

        [Fact]
        public void Parse_MissingTypeValue_ReturnsNull()
        {
            var result = CliParser.Parse(new[] { "--type" });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_UnknownFlag_OutputsWarningButContinues()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", "/tmp", "--unknown-flag" });
            Assert.NotNull(result);
        }

        [Fact]
        public void Parse_AllBooleanFlags_SetCorrectly()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "5", "--output-path", "/tmp", "--with-metadata", "--with-text", "--include-load-file", "--with-families", "--loadfile-only", "--chaos-mode", "--production-set", "--production-zip" });
            Assert.NotNull(result);
            Assert.True(result!.WithMetadata);
            Assert.True(result.WithText);
            Assert.True(result.IncludeLoadFile);
            Assert.True(result.WithFamilies);
            Assert.True(result.LoadfileOnly);
            Assert.True(result.ChaosMode);
            Assert.True(result.ProductionSet);
            Assert.True(result.ProductionZip);
        }

        [Fact]
        public void Parse_LoadfileOnlyArgs_ParsesCorrectly()
        {
            var result = CliParser.Parse(new[] { "--loadfile-only", "--count", "10", "--output-path", "/tmp", "--eol", "LF", "--col-delim", "ascii:20", "--quote-delim", "none", "--multi-delim", "char:;", "--nested-delim", "char:\\", "--loadfile-format", "opt" });
            Assert.NotNull(result);
            Assert.True(result!.LoadfileOnly);
            Assert.Equal("LF", result.Eol);
            Assert.Equal("ascii:20", result.ColDelim);
            Assert.Equal("none", result.QuoteDelim);
            Assert.Equal("char:;", result.MultiDelim);
            Assert.Equal("char:\\", result.NestedDelim);
            Assert.Equal("opt", result.LoadFileFormat);
        }

        [Fact]
        public void Parse_ChaosArgs_ParsesCorrectly()
        {
            var result = CliParser.Parse(new[] { "--loadfile-only", "--count", "50", "--output-path", "/tmp", "--chaos-mode", "--chaos-amount", "5%", "--chaos-types", "quotes,columns", "--chaos-scenario", "test" });
            Assert.NotNull(result);
            Assert.True(result!.ChaosMode);
            Assert.Equal("5%", result.ChaosAmount);
            Assert.Equal("quotes,columns", result.ChaosTypes);
            Assert.Equal("test", result.ChaosScenario);
        }

        [Fact]
        public void Parse_ProductionSetArgs_ParsesCorrectly()
        {
            var result = CliParser.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--bates-start", "100", "--bates-digits", "6", "--volume-size", "1000", "--count", "20", "--output-path", "/tmp" });
            Assert.NotNull(result);
            Assert.True(result!.ProductionSet);
            Assert.Equal("CL001", result.BatesPrefix);
            Assert.Equal(100, result.BatesStart);
            Assert.Equal(6, result.BatesDigits);
            Assert.Equal(1000, result.VolumeSize);
        }

        [Fact]
        public void Parse_DelimiterArgs_ParsesCorrectly()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", "/tmp", "--dat-delimiters", "csv", "--delimiter-column", "|", "--delimiter-quote", "~", "--delimiter-newline", " " });
            Assert.NotNull(result);
            Assert.Equal("csv", result!.DatDelimiters);
            Assert.Equal("|", result.DelimiterColumn);
            Assert.Equal("~", result.DelimiterQuote);
            Assert.Equal(" ", result.DelimiterNewline);
        }

        [Fact]
        public void Parse_ColumnProfileArgs_ParsesCorrectly()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", "/tmp", "--column-profile", "standard", "--seed", "42", "--date-format", "yyyy-MM-dd", "--empty-percentage", "15", "--custodian-count", "50" });
            Assert.NotNull(result);
            Assert.Equal("standard", result!.ColumnProfile);
            Assert.Equal(42, result.Seed);
            Assert.Equal("yyyy-MM-dd", result.DateFormat);
            Assert.Equal(15, result.EmptyPercentage);
            Assert.Equal(50, result.CustodianCount);
        }

        [Fact]
        public void Parse_WithNullArgs_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => CliParser.Parse(null!));
        }

        [Fact]
        public void Parse_InvalidCount_ReturnsNull()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "not-a-number", "--output-path", "/tmp" });
            Assert.Null(result);
        }
    }
}
