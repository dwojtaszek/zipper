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
            var args = new[] { "--type", "pdf", "--count", "100", "--output-path", Path.Combine(Directory.GetCurrentDirectory(), "test") };
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
        public void Parse_MissingValueForValueTakingFlags_ReturnsNull()
        {
            var flags = new[]
            {
                "--folders", "--encoding", "--distribution", "--attachment-rate", "--target-zip-size",
                "--load-file-format", "--bates-prefix", "--bates-start", "--bates-digits", "--tiff-pages",
                "--column-profile", "--seed", "--date-format", "--empty-percentage", "--custodian-count",
                "--load-file-formats", "--dat-delimiters", "--loadfile-format", "--eol", "--col-delim",
                "--quote-delim", "--newline-delim", "--multi-delim", "--nested-delim", "--chaos-amount",
                "--chaos-types", "--chaos-scenario", "--volume-size"
            };

            foreach (var flag in flags)
            {
                var result = CliParser.Parse(new[] { flag });
                Assert.Null(result);
            }
        }

        [Fact]
        public void Parse_UnknownFlag_OutputsWarningButContinues()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--unknown-flag" });
            Assert.NotNull(result);
        }

        [Fact]
        public void Parse_AllBooleanFlags_SetCorrectly()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--with-metadata", "--with-text", "--include-load-file", "--with-families", "--loadfile-only", "--chaos-mode", "--production-set", "--production-zip" });
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
            var result = CliParser.Parse(new[] { "--loadfile-only", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--eol", "LF", "--col-delim", "ascii:20", "--quote-delim", "none", "--multi-delim", "char:;", "--nested-delim", "char:\\", "--loadfile-format", "opt" });
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
            var result = CliParser.Parse(new[] { "--loadfile-only", "--count", "50", "--output-path", Directory.GetCurrentDirectory(), "--chaos-mode", "--chaos-amount", "5%", "--chaos-types", "quotes,columns", "--chaos-scenario", "test" });
            Assert.NotNull(result);
            Assert.True(result!.ChaosMode);
            Assert.Equal("5%", result.ChaosAmount);
            Assert.Equal("quotes,columns", result.ChaosTypes);
            Assert.Equal("test", result.ChaosScenario);
        }

        [Fact]
        public void Parse_ProductionSetArgs_ParsesCorrectly()
        {
            var result = CliParser.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--bates-start", "100", "--bates-digits", "6", "--volume-size", "1000", "--count", "20", "--output-path", Directory.GetCurrentDirectory() });
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
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--dat-delimiters", "csv", "--delimiter-column", "|", "--delimiter-quote", "~", "--delimiter-newline", " " });
            Assert.NotNull(result);
            Assert.Equal("csv", result!.DatDelimiters);
            Assert.Equal("|", result.DelimiterColumn);
            Assert.Equal("~", result.DelimiterQuote);
            Assert.Equal(" ", result.DelimiterNewline);
        }

        [Fact]
        public void Parse_ColumnProfileArgs_ParsesCorrectly()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", "standard", "--seed", "42", "--date-format", "yyyy-MM-dd", "--empty-percentage", "15", "--custodian-count", "50" });
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
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "not-a-number", "--output-path", Directory.GetCurrentDirectory() });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_InvalidFolders_ReturnsNull()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--folders", "notanumber" });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_InvalidAttachmentRate_ReturnsNull()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--attachment-rate", "notanumber" });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_InvalidBatesStart_ReturnsNull()
        {
            var result = CliParser.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--bates-start", "notanumber" });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_InvalidBatesDigits_ReturnsNull()
        {
            var result = CliParser.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--bates-digits", "notanumber" });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_InvalidSeed_ReturnsNull()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--seed", "notanumber" });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_InvalidEmptyPercentage_ReturnsNull()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--empty-percentage", "notanumber" });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_InvalidCustodianCount_ReturnsNull()
        {
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", Directory.GetCurrentDirectory(), "--custodian-count", "notanumber" });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_InvalidVolumeSize_ReturnsNull()
        {
            var result = CliParser.Parse(new[] { "--production-set", "--bates-prefix", "CL001", "--count", "5", "--output-path", Directory.GetCurrentDirectory(), "--volume-size", "notanumber" });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_ChaosListNotConsumedAsValueForPrecedingArg()
        {
            // Before fix: --chaos-list would be consumed as the value for --type.
            // After fix: TryGetValue returns false (--chaos-list is parameterless),
            // so --type fails with a missing-value error and Parse returns null.
            var result = CliParser.Parse(new[] { "--type", "--chaos-list" });
            Assert.Null(result);
        }

        [Fact]
        public void Parse_BenchmarkNotConsumedAsValueForPrecedingArg()
        {
            // --benchmark is a parameterless flag; it must not be consumed as a value
            // for a preceding option such as --type.
            var result = CliParser.Parse(new[] { "--type", "--benchmark" });
            Assert.Null(result);
        }

        /// <summary>
        /// REQ-106: A relative path containing ".." that resolves outside CWD must be rejected by
        /// the CLI layer. Before the fix, CliParser called ValidateAndCreateDirectory with no
        /// baseDirectory, bypassing the traversal check entirely.
        /// </summary>
        [Fact]
        public void Parse_OutputPathWithParentTraversal_RejectsPathOutsideCwd()
        {
            // "../escape" resolves to the parent of CWD — outside the allowed base directory.
            var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", "../escape" });

            // Parse must return null (error path) so the caller exits with a non-zero code.
            Assert.Null(result);
        }

        /// <summary>
        /// REQ-106: A path that stays inside CWD must still be accepted after the fix.
        /// Regression guard: adding the base-directory check must not block safe relative paths.
        /// </summary>
        [Fact]
        public void Parse_OutputPathWithinCwd_IsAccepted()
        {
            var uniqueDirName = "output_" + Guid.NewGuid().ToString("N");
            try
            {
                // uniqueDirName is a safe relative subdirectory of CWD and must be accepted.
                var result = CliParser.Parse(new[] { "--type", "pdf", "--count", "10", "--output-path", uniqueDirName });

                Assert.NotNull(result);
                Assert.NotNull(result!.OutputDirectory);
            }
            finally
            {
                if (Directory.Exists(uniqueDirName))
                {
                    Directory.Delete(uniqueDirName, recursive: true);
                }
            }
        }
    }
}
