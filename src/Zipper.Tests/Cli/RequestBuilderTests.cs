using Xunit;
using Zipper.Cli;
using Zipper.Config;

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
        var result = RequestBuilderTestHelper.Build(parsed);

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
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(null!, new DelimiterConfig(), new TiffConfig(), new ChaosConfig(), new HashConfig()));
    }

    [Fact]
    public void Build_NullConfigArg_ThrowsArgumentNullException()
    {
        var parsed = new ParsedArguments();
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(parsed, null!, new TiffConfig(), new ChaosConfig(), new HashConfig()));
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(parsed, new DelimiterConfig(), null!, new ChaosConfig(), new HashConfig()));
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(parsed, new DelimiterConfig(), new TiffConfig(), null!, new HashConfig()));
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(parsed, new DelimiterConfig(), new TiffConfig(), new ChaosConfig(), null!));
    }

    [Fact]
    public void Build_WithValidPath_ResolvesDirectory()
    {
        var parsed = CreateParsedArgs();
        parsed.OutputPathStr = Directory.GetCurrentDirectory();

        var result = RequestBuilderTestHelper.Build(parsed);

        Assert.NotNull(result);
        Assert.Equal(Directory.GetCurrentDirectory(), result!.Output.OutputPath);
    }

    [Fact]
    public void Build_WithInvalidPath_ReturnsNull()
    {
        var parsed = CreateParsedArgs();
        parsed.OutputPathStr = string.Empty; // Invalid path

        var result = RequestBuilderTestHelper.Build(parsed);

        Assert.Null(result);
    }

    [Fact]
    public void Build_LoadfileOnly_SetsProperties()
    {
        var parsed = CreateParsedArgs();
        parsed.LoadfileOnly = true;
        parsed.LoadFileFormat = "opt";

        var result = RequestBuilderTestHelper.Build(parsed);

        Assert.True(result!.LoadfileOnly);
        Assert.Single(result!.LoadFile.Formats);
        Assert.Equal(LoadFileFormat.Opt, result!.LoadFile.Formats[0]);
    }

    [Fact]
    public void Build_ProductionSet_SetsVolumeSize()
    {
        var parsed = CreateParsedArgs();
        parsed.ProductionSet = true;
        parsed.VolumeSize = 1000;

        var result = RequestBuilderTestHelper.Build(parsed);

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

        var result = RequestBuilderTestHelper.Build(parsed);

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

        var result = RequestBuilderTestHelper.Build(parsed);

        Assert.NotNull(result!.Metadata.ColumnProfile);
    }

    [Fact]
    public void Build_MultiFormat_CreatesFormatList()
    {
        var parsed = CreateParsedArgs();
        parsed.LoadFileFormats = "dat,opt,csv";

        var result = RequestBuilderTestHelper.Build(parsed);

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

        var result = RequestBuilderTestHelper.Build(parsed);

        Assert.Equal("WINDOWS-1252", result!.LoadFile.Encoding);
    }

    [Fact]
    public void Build_Encoding_PreservesNormalizedInputName()
    {
        var parsed = CreateParsedArgs();
        parsed.Encoding = "UTF-16";

        var result = RequestBuilderTestHelper.Build(parsed);

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
}
