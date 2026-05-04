using Xunit;
using Zipper.Cli;

namespace Zipper.Tests
{
    [Collection("ConsoleTests")]
    public class RequestBuilderTests
    {
        private static ParsedArguments CreateParsedArgs()
        {
            return new ParsedArguments
            {
                FileType = "pdf",
                Count = 100,
                OutputDirectory = new DirectoryInfo(Path.GetTempPath()),
            };
        }

        [Fact]
        public void Build_StandardMode_SetsAllDefaults()
        {
            var parsed = CreateParsedArgs();
            var result = RequestBuilder.Build(parsed);

            Assert.NotNull(result);
            Assert.Equal(Path.GetTempPath(), result.Output.OutputPath);
            Assert.Equal(100, result.Output.FileCount);
            Assert.Equal("pdf", result.Output.FileType);
            Assert.Equal(1, result.Output.Folders);
            Assert.Equal(DistributionType.Proportional, result.LoadFile.Distribution);
            Assert.False(result.Metadata.WithMetadata);
            Assert.False(result.Output.WithText);
            Assert.False(result.Output.IncludeLoadFile);
            Assert.Equal(0, result.LoadFile.AttachmentRate);
            Assert.Null(result.Bates);
        }

        [Fact]
        public void Build_NullArg_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(null!));
        }

        [Fact]
        public void Build_ChaosMode_SetsChaosProperties()
        {
            var parsed = CreateParsedArgs();
            parsed.LoadfileOnly = true;
            parsed.ChaosMode = true;
            parsed.ChaosAmount = "5%";
            parsed.ChaosTypes = "quotes,columns";

            var result = RequestBuilder.Build(parsed);

            Assert.True(result.Chaos.ChaosMode);
            Assert.Equal("5%", result.Chaos.ChaosAmount);
            Assert.Equal("quotes,columns", result.Chaos.ChaosTypes);
        }

        [Fact]
        public void Build_LoadfileOnly_SetsProperties()
        {
            var parsed = CreateParsedArgs();
            parsed.LoadfileOnly = true;
            parsed.Eol = "LF";
            parsed.LoadFileFormat = "opt";

            var result = RequestBuilder.Build(parsed);

            Assert.True(result.LoadfileOnly);
            Assert.Equal("LF", result.Delimiters.EndOfLine);
            Assert.Equal(LoadFileFormat.Opt, result.LoadFile.LoadFileFormat);
        }

        [Fact]
        public void Build_ProductionSet_SetsVolumeSize()
        {
            var parsed = CreateParsedArgs();
            parsed.ProductionSet = true;
            parsed.VolumeSize = 1000;

            var result = RequestBuilder.Build(parsed);

            Assert.True(result.Production.ProductionSet);
            Assert.Equal(1000, result.Production.VolumeSize);
        }

        [Fact]
        public void Build_BatesConfig_SetsCorrectly()
        {
            var parsed = CreateParsedArgs();
            parsed.BatesPrefix = "CL001";
            parsed.BatesStart = 100;
            parsed.BatesDigits = 6;

            var result = RequestBuilder.Build(parsed);

            Assert.NotNull(result.Bates);
            Assert.Equal("CL001", result.Bates.Prefix);
            Assert.Equal(100, result.Bates.Start);
            Assert.Equal(6, result.Bates.Digits);
        }

        [Fact]
        public void Build_ColumnProfile_LoadsProfile()
        {
            var parsed = CreateParsedArgs();
            parsed.ColumnProfile = "standard";

            var result = RequestBuilder.Build(parsed);

            Assert.NotNull(result.Metadata.ColumnProfile);
        }

        [Fact]
        public void Build_DelimiterPreset_Csv_SetsCommaDelimiters()
        {
            var parsed = CreateParsedArgs();
            parsed.DatDelimiters = "csv";

            var result = RequestBuilder.Build(parsed);

            Assert.Equal(",", result.Delimiters.ColumnDelimiter);
            Assert.Equal("\"", result.Delimiters.QuoteDelimiter);
        }

        [Fact]
        public void Build_DelimiterOverride_OverridesPreset()
        {
            var parsed = CreateParsedArgs();
            parsed.DatDelimiters = "csv";
            parsed.DelimiterColumn = "|";

            var result = RequestBuilder.Build(parsed);

            Assert.Equal("|", result.Delimiters.ColumnDelimiter);
            Assert.Equal("\"", result.Delimiters.QuoteDelimiter);
        }

        [Fact]
        public void Build_StrictDelimiters_OverrideOldDelimiters()
        {
            var parsed = CreateParsedArgs();
            parsed.LoadfileOnly = true;
            parsed.DelimiterColumn = ",";
            parsed.ColDelim = "ascii:20";

            var result = RequestBuilder.Build(parsed);

            Assert.Equal("\u0014", result.Delimiters.ColumnDelimiter);
        }

        [Fact]
        public void Build_MultiFormat_CreatesFormatList()
        {
            var parsed = CreateParsedArgs();
            parsed.LoadFileFormats = "dat,opt,csv";

            var result = RequestBuilder.Build(parsed);

            Assert.NotNull(result.LoadFile.LoadFileFormats);
            Assert.Contains(LoadFileFormat.Dat, result.LoadFile.LoadFileFormats);
            Assert.Contains(LoadFileFormat.Opt, result.LoadFile.LoadFileFormats);
            Assert.Contains(LoadFileFormat.Csv, result.LoadFile.LoadFileFormats);
        }

        [Fact]
        public void Build_LoadfileOnlyEncoding_UsesExtendedSet()
        {
            var parsed = CreateParsedArgs();
            parsed.LoadfileOnly = true;
            parsed.Encoding = "WINDOWS-1252";

            var result = RequestBuilder.Build(parsed);

            Assert.Equal("WINDOWS-1252", result.LoadFile.Encoding);
        }

        [Fact]
        public void ParseSize_ValidSizes_ReturnsBytes()
        {
            Assert.Equal(1024, RequestBuilder.ParseSize("1KB"));
            Assert.Equal(1024 * 1024, RequestBuilder.ParseSize("1MB"));
            Assert.Equal(1024L * 1024 * 1024, RequestBuilder.ParseSize("1GB"));
            Assert.Equal(500L * 1024 * 1024, RequestBuilder.ParseSize("500MB"));
        }

        [Fact]
        public void ParseSize_InvalidSize_ReturnsNull()
        {
            Assert.Null(RequestBuilder.ParseSize("invalid"));
            Assert.Null(RequestBuilder.ParseSize("10XB"));
        }

        [Fact]
        public void GetDistributionFromName_ValidNames_ReturnsCorrectType()
        {
            Assert.Equal(DistributionType.Proportional, RequestBuilder.GetDistributionFromName("proportional"));
            Assert.Equal(DistributionType.Gaussian, RequestBuilder.GetDistributionFromName("gaussian"));
            Assert.Equal(DistributionType.Exponential, RequestBuilder.GetDistributionFromName("exponential"));
        }

        [Fact]
        public void GetDistributionFromName_InvalidName_ReturnsNull()
        {
            Assert.Null(RequestBuilder.GetDistributionFromName("invalid"));
        }

        [Fact]
        public void GetLoadFileFormat_ValidNames_ReturnsCorrectFormat()
        {
            Assert.Equal(LoadFileFormat.Dat, RequestBuilder.GetLoadFileFormat("dat"));
            Assert.Equal(LoadFileFormat.Opt, RequestBuilder.GetLoadFileFormat("opt"));
            Assert.Equal(LoadFileFormat.Csv, RequestBuilder.GetLoadFileFormat("csv"));
            Assert.Equal(LoadFileFormat.Xml, RequestBuilder.GetLoadFileFormat("xml"));
            Assert.Equal(LoadFileFormat.EdrmXml, RequestBuilder.GetLoadFileFormat("edrm-xml"));
            Assert.Equal(LoadFileFormat.Concordance, RequestBuilder.GetLoadFileFormat("concordance"));
        }

        [Fact]
        public void GetLoadFileFormat_InvalidName_ReturnsNull()
        {
            Assert.Null(RequestBuilder.GetLoadFileFormat("invalid"));
        }

        [Theory]
        [InlineData("\\t", "\t")]
        [InlineData("\\n", "\n")]
        [InlineData("\\r", "\r")]
        [InlineData("20", "\u0014")]
        [InlineData("254", "\u00fe")]
        [InlineData("|", "|")]
        public void ParseDelimiterArgument_ValidInputs_ReturnsCorrectValue(string input, string expected)
        {
            Assert.Equal(expected, RequestBuilder.ParseDelimiterArgument(input));
        }

        [Fact]
        public void ParseDelimiterArgument_Empty_Throws()
        {
            Assert.Throws<ArgumentException>(() => RequestBuilder.ParseDelimiterArgument(string.Empty));
        }

        [Theory]
        [InlineData("ascii:20", "\u0014")]
        [InlineData("ascii:254", "\u00fe")]
        [InlineData("char:;", ";")]
        [InlineData("char:|", "|")]
        public void ParseStrictDelimiter_ValidInputs_ReturnsCorrectValue(string input, string expected)
        {
            Assert.Equal(expected, RequestBuilder.ParseStrictDelimiter(input));
        }

        [Fact]
        public void ParseStrictDelimiter_InvalidPrefix_Throws()
        {
            Assert.Throws<ArgumentException>(() => RequestBuilder.ParseStrictDelimiter("20"));
        }
    }
}
