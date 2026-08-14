using Xunit;
using Zipper.Cli;
using Zipper.Cli.Modules;
using Zipper.Config;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class RequestBuilderTests
{
    private static (ParsedArguments? Parsed, CliModuleSet Modules) CreateParsedArgs()
        => RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory() });

    [Fact]
    public void Build_StandardMode_SetsAllDefaults()
    {
        var (_, modules) = CreateParsedArgs();
        var result = RequestBuilderTestHelper.Build(modules: modules);

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
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(null!, new MetadataConfig(), new LoadFileConfig(), new DelimiterConfig(), null, new TiffConfig(), new ChaosConfig(), new HashConfig(), new ProductionConfig(), null, false, false));
    }

    [Fact]
    public void Build_NullConfigArg_ThrowsArgumentNullException()
    {
        var output = new OutputConfig();
        var metadata = new MetadataConfig();
        var loadFile = new LoadFileConfig();
        var delimiters = new DelimiterConfig();
        var tiff = new TiffConfig();
        var chaos = new ChaosConfig();
        var hash = new HashConfig();
        var production = new ProductionConfig();
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(null!, metadata, loadFile, delimiters, null, tiff, chaos, hash, production, null, false, false));
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(output, null!, loadFile, delimiters, null, tiff, chaos, hash, production, null, false, false));
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(output, metadata, null!, delimiters, null, tiff, chaos, hash, production, null, false, false));
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(output, metadata, loadFile, null!, null, tiff, chaos, hash, production, null, false, false));
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(output, metadata, loadFile, delimiters, null, null!, chaos, hash, production, null, false, false));
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(output, metadata, loadFile, delimiters, null, tiff, null!, hash, production, null, false, false));
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(output, metadata, loadFile, delimiters, null, tiff, chaos, null!, production, null, false, false));
        Assert.Throws<ArgumentNullException>(() => RequestBuilder.Build(output, metadata, loadFile, delimiters, null, tiff, chaos, hash, null!, null, false, false));
    }

    [Fact]
    public void Build_WithValidPath_ResolvesDirectory()
    {
        var (_, modules) = CreateParsedArgs();

        var result = RequestBuilderTestHelper.Build(modules: modules);

        Assert.NotNull(result);
        Assert.Equal(Directory.GetCurrentDirectory(), result!.Output.OutputPath);
    }

    [Fact]
    public void Build_LoadfileOnly_SetsProperties()
    {
        var (_, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-only", "--load-file-format", "opt" });

        var result = RequestBuilderTestHelper.Build(modules: modules);

        Assert.True(result!.LoadfileOnly);
        Assert.Single(result!.LoadFile.Formats);
        Assert.Equal(LoadFileFormat.Opt, result!.LoadFile.Formats[0]);
    }

    [Fact]
    public void Build_ProductionSet_SetsVolumeSize()
    {
        var (_, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--production-set", "--bates-prefix", "PREFIX", "--volume-size", "1000" });

        var result = RequestBuilderTestHelper.Build(modules: modules);

        Assert.True(result!.Production.ProductionSet);
        Assert.Equal(1000, result!.Production.VolumeSize);
    }

    [Fact]
    public void Build_BatesConfig_SetsCorrectly()
    {
        var (_, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--bates-prefix", "CL001", "--bates-start", "100", "--bates-digits", "6" });

        var result = RequestBuilderTestHelper.Build(modules: modules);

        Assert.NotNull(result!.Bates);
        Assert.Equal("CL001", result!.Bates.Prefix);
        Assert.Equal(100, result!.Bates.Start);
        Assert.Equal(6, result!.Bates.Digits);
    }

    [Fact]
    public void Build_ColumnProfile_LoadsProfile()
    {
        var (_, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--column-profile", "standard" });

        var result = RequestBuilderTestHelper.Build(modules: modules);

        Assert.NotNull(result!.Metadata.ColumnProfile);
    }

    [Fact]
    public void Build_MultiFormat_CreatesFormatList()
    {
        var (_, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--load-file-formats", "dat,opt,csv" });

        var result = RequestBuilderTestHelper.Build(modules: modules);

        Assert.Equal(3, result!.LoadFile.Formats.Count);
        Assert.Contains(LoadFileFormat.Dat, result!.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Opt, result!.LoadFile.Formats);
        Assert.Contains(LoadFileFormat.Csv, result!.LoadFile.Formats);
    }

    [Fact]
    public void Build_LoadfileOnlyEncoding_UsesExtendedSet()
    {
        var (_, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--loadfile-only", "--encoding", "WINDOWS-1252" });

        var result = RequestBuilderTestHelper.Build(modules: modules);

        Assert.Equal("WINDOWS-1252", result!.LoadFile.Encoding);
    }

    [Fact]
    public void Build_Encoding_PreservesNormalizedInputName()
    {
        var (_, modules) = RequestBuilderTestHelper.Parse(new[] { "--type", "pdf", "--count", "100", "--output-path", Directory.GetCurrentDirectory(), "--encoding", "UTF-16" });

        var result = RequestBuilderTestHelper.Build(modules: modules);

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
