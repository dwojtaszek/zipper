using Xunit;
using Zipper.Cli;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class RequestBuilderTests
{
    private static ParsedArguments CreateParsedArgs()
    {
        return new ParsedArguments
        {
            FileType = "pdf",
            Count = 100,
            OutputPathStr = Directory.GetCurrentDirectory(),
        };
    }

    [Fact]
    public void Build_StandardMode_SetsAllDefaults()
    {
        var parsed = CreateParsedArgs();
        var result = RequestBuilder.Build(parsed);

        Assert.NotNull(result);
        Assert.Equal(Directory.GetCurrentDirectory(), result!.Output.OutputPath);
        Assert.Equal(100, result!.Output.FileCount);
        Assert.Equal("pdf", result!.Output.FileType);
        Assert.Equal(1, result!.Output.Folders);
        Assert.Equal(DistributionType.Proportional, result!.LoadFile.Distribution);
        Assert.False(result!.Metadata.WithMetadata);
        Assert.False(result!.Output.WithText);
        Assert.False(result!.Output.IncludeLoadFile);
        Assert.Equal(0, result!.LoadFile.AttachmentRate);
        Assert.Null(result!.Bates);
    }

    [Fact]
    public void Build_NullArg_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(null!));
    }

    [Fact]
    public void Build_WithValidPath_ResolvesDirectory()
    {
        var parsed = CreateParsedArgs();
        parsed.OutputPathStr = Directory.GetCurrentDirectory();

        var result = RequestBuilder.Build(parsed);

        Assert.NotNull(result);
        Assert.Equal(Directory.GetCurrentDirectory(), result!.Output.OutputPath);
    }

    [Fact]
    public void Build_WithInvalidPath_ReturnsNull()
    {
        var parsed = CreateParsedArgs();
        parsed.OutputPathStr = string.Empty; // Invalid path

        var result = RequestBuilder.Build(parsed);

        Assert.Null(result);
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

        Assert.True(result!.Chaos.ChaosMode);
        Assert.Equal("5%", result!.Chaos.ChaosAmount);
        Assert.Equal("quotes,columns", result!.Chaos.ChaosTypes);
    }

    [Fact]
    public void Build_LoadfileOnly_SetsProperties()
    {
        var parsed = CreateParsedArgs();
        parsed.LoadfileOnly = true;
        parsed.Eol = "LF";
        parsed.LoadFileFormat = "opt";

        var result = RequestBuilder.Build(parsed);

        Assert.True(result!.LoadfileOnly);
        Assert.Equal("LF", result!.Delimiters.EndOfLine);
        Assert.Single(result!.LoadFile.Formats);
        Assert.Equal(LoadFileFormat.Opt, result!.LoadFile.Formats[0]);
    }

    [Fact]
    public void Build_ProductionSet_SetsVolumeSize()
    {
        var parsed = CreateParsedArgs();
        parsed.ProductionSet = true;
        parsed.VolumeSize = 1000;

        var result = RequestBuilder.Build(parsed);

        Assert.True(result!.Production.ProductionSet);
        Assert.Equal(1000, result!.Production.VolumeSize);
    }

    [Fact]
    public void Build_BatesConfig_SetsCorrectly()
    {
        var parsed = CreateParsedArgs();
        parsed.BatesPrefix = "CL001";
        parsed.BatesStart = 100;
        parsed.BatesDigits = 6;

        var result = RequestBuilder.Build(parsed);

        Assert.NotNull(result!.Bates);
        Assert.Equal("CL001", result!.Bates.Prefix);
        Assert.Equal(100, result!.Bates.Start);
        Assert.Equal(6, result!.Bates.Digits);
    }

    [Fact]
    public void Build_ColumnProfile_LoadsProfile()
    {
        var parsed = CreateParsedArgs();
        parsed.ColumnProfile = "standard";

        var result = RequestBuilder.Build(parsed);

        Assert.NotNull(result!.Metadata.ColumnProfile);
    }

    [Fact]
    public void Build_DelimiterPreset_Csv_SetsCommaDelimiters()
    {
        var parsed = CreateParsedArgs();
        parsed.DatDelimiters = "csv";

        var result = RequestBuilder.Build(parsed);

        Assert.Equal(",", result!.Delimiters.ColumnDelimiter);
        Assert.Equal("\"", result!.Delimiters.QuoteDelimiter);
    }

    [Fact]
    public void Build_DelimiterOverride_OverridesPreset()
    {
        var parsed = CreateParsedArgs();
        parsed.DatDelimiters = "csv";
        parsed.DelimiterColumn = "|";
        Assert.True(CliValidator.Validate(parsed));

        var result = RequestBuilder.Build(parsed);

        Assert.Equal("|", result!.Delimiters.ColumnDelimiter);
        Assert.Equal("\"", result!.Delimiters.QuoteDelimiter);
    }

    [Fact]
    public void Build_StrictDelimiters_OverrideOldDelimiters()
    {
        var parsed = CreateParsedArgs();
        parsed.LoadfileOnly = true;
        parsed.DelimiterColumn = ",";
        parsed.ColDelim = "ascii:20";
        Assert.True(CliValidator.Validate(parsed));

        var result = RequestBuilder.Build(parsed);

        Assert.Equal("\u0014", result!.Delimiters.ColumnDelimiter);
    }

    [Fact]
    public void Build_MultiFormat_CreatesFormatList()
    {
        var parsed = CreateParsedArgs();
        parsed.LoadFileFormats = "dat,opt,csv";

        var result = RequestBuilder.Build(parsed);

        Assert.Equal(3, result!.LoadFile.Formats.Count);
        Assert.Contains(LoadFileFormat.Dat, result!.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Opt, result!.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Csv, result!.LoadFile.Formats);
    }

    [Fact]
    public void Build_LoadfileOnlyEncoding_UsesExtendedSet()
    {
        var parsed = CreateParsedArgs();
        parsed.LoadfileOnly = true;
        parsed.Encoding = "WINDOWS-1252";

        var result = RequestBuilder.Build(parsed);

        Assert.Equal("WINDOWS-1252", result!.LoadFile.Encoding);
    }

    [Fact]
    public void Build_Encoding_PreservesNormalizedInputName()
    {
        var parsed = CreateParsedArgs();
        parsed.Encoding = "UTF-16";

        var result = RequestBuilder.Build(parsed);

        Assert.Equal("UTF-16", result!.LoadFile.Encoding);
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
        Assert.Equal(LoadFileFormat.EdrmXml, RequestBuilder.GetLoadFileFormat("xml"));
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

    [Fact]
    public void Build_HashModeActualAndAlgorithms_SetsHashConfig()
    {
        var parsed = CreateParsedArgs();
        parsed.HashMode = "actual";
        parsed.HashAlgorithms = "md5,sha256";

        var result = RequestBuilder.Build(parsed);
        Assert.NotNull(result);

        var hash = result!.Hash;
        Assert.Equal(Config.HashMode.Actual, hash.Mode);
        Assert.Contains(Config.HashAlgorithm.MD5, hash.Algorithms);
        Assert.Contains(Config.HashAlgorithm.SHA256, hash.Algorithms);
        Assert.DoesNotContain(Config.HashAlgorithm.SHA1, hash.Algorithms);
        Assert.True(hash.IsEnabled);
    }

    [Fact]
    public void Build_HashModeSimulated_SetsSimulatedMode()
    {
        var parsed = CreateParsedArgs();
        parsed.HashMode = "simulated";
        parsed.HashAlgorithms = "sha1";

        var result = RequestBuilder.Build(parsed);
        Assert.NotNull(result);

        var hash = result!.Hash;
        Assert.Equal(Config.HashMode.Simulated, hash.Mode);
        Assert.Contains(Config.HashAlgorithm.SHA1, hash.Algorithms);
        Assert.True(hash.IsEnabled);
    }

    [Fact]
    public void Build_HashModeNone_DefaultsToDisabled()
    {
        var parsed = CreateParsedArgs();

        var result = RequestBuilder.Build(parsed);
        Assert.NotNull(result);

        var hash = result!.Hash;
        Assert.Equal(Config.HashMode.None, hash.Mode);
        Assert.Empty(hash.Algorithms);
        Assert.False(hash.IsEnabled);
    }

    [Fact]
    public void ParseHashConfig_ActualMode_ReturnsCorrectConfig()
    {
        var parsed = new ParsedArguments
        {
            HashMode = "actual",
            HashAlgorithms = "md5,sha1,sha256",
        };

        var config = RequestBuilder.ParseHashConfig(parsed);
        Assert.Equal(Config.HashMode.Actual, config.Mode);
        Assert.Equal(3, config.Algorithms.Count);
        Assert.Contains(Config.HashAlgorithm.MD5, config.Algorithms);
        Assert.Contains(Config.HashAlgorithm.SHA1, config.Algorithms);
        Assert.Contains(Config.HashAlgorithm.SHA256, config.Algorithms);
    }

    [Fact]
    public void ParseHashConfig_SimulatedMode_ReturnsSimulatedModeWithDefaultMD5()
    {
        var parsed = new ParsedArguments { HashMode = "simulated" };

        var config = RequestBuilder.ParseHashConfig(parsed);
        Assert.Equal(Config.HashMode.Simulated, config.Mode);
        Assert.Contains(Config.HashAlgorithm.MD5, config.Algorithms);
        Assert.True(config.IsEnabled);
    }

    [Fact]
    public void ParseHashConfig_InvalidMode_DefaultsToNone()
    {
        var parsed = new ParsedArguments { HashMode = "invalid" };

        var config = RequestBuilder.ParseHashConfig(parsed);
        Assert.Equal(Config.HashMode.None, config.Mode);
    }

    [Fact]
    public void ParseHashConfig_Default_NoneModeEmptyAlgorithms()
    {
        var parsed = new ParsedArguments();

        var config = RequestBuilder.ParseHashConfig(parsed);
        Assert.Equal(Config.HashMode.None, config.Mode);
        Assert.Empty(config.Algorithms);
    }

    [Fact]
    public void Build_LoadfileOnlyWithActualHashMode_ReturnsNull()
    {
        var parsed = CreateParsedArgs();
        parsed.LoadfileOnly = true;
        parsed.HashMode = "actual";
        parsed.HashAlgorithms = "md5";

        var result = RequestBuilder.Build(parsed);
        Assert.Null(result);
    }
}
