using Xunit;
using Zipper.Cli;

namespace Zipper.Tests;

[Collection("ConsoleTests")]
public class ArgumentHelpersTests
{
    [Fact]
    public void ParseSize_ValidSizes_ReturnsBytes()
    {
        Assert.Equal(1024, ArgumentHelpers.ParseSize("1KB"));
        Assert.Equal(1024 * 1024, ArgumentHelpers.ParseSize("1MB"));
        Assert.Equal(1024L * 1024 * 1024, ArgumentHelpers.ParseSize("1GB"));
        Assert.Equal(500L * 1024 * 1024, ArgumentHelpers.ParseSize("500MB"));
    }

    [Fact]
    public void ParseSize_InvalidSize_ReturnsNull()
    {
        Assert.Null(ArgumentHelpers.ParseSize("invalid"));
        Assert.Null(ArgumentHelpers.ParseSize("10XB"));
    }

    [Fact]
    public void GetDistributionFromName_ValidNames_ReturnsCorrectType()
    {
        Assert.Equal(DistributionType.Proportional, ArgumentHelpers.GetDistributionFromName("proportional"));
        Assert.Equal(DistributionType.Gaussian, ArgumentHelpers.GetDistributionFromName("gaussian"));
        Assert.Equal(DistributionType.Exponential, ArgumentHelpers.GetDistributionFromName("exponential"));
    }

    [Fact]
    public void GetDistributionFromName_InvalidName_ReturnsNull()
    {
        Assert.Null(ArgumentHelpers.GetDistributionFromName("invalid"));
    }

    [Fact]
    public void GetLoadFileFormat_ValidNames_ReturnsCorrectFormat()
    {
        Assert.Equal(LoadFileFormat.Dat, ArgumentHelpers.GetLoadFileFormat("dat"));
        Assert.Equal(LoadFileFormat.Opt, ArgumentHelpers.GetLoadFileFormat("opt"));
        Assert.Equal(LoadFileFormat.Csv, ArgumentHelpers.GetLoadFileFormat("csv"));
        Assert.Equal(LoadFileFormat.EdrmXml, ArgumentHelpers.GetLoadFileFormat("xml"));
        Assert.Equal(LoadFileFormat.EdrmXml, ArgumentHelpers.GetLoadFileFormat("edrm-xml"));
        Assert.Equal(LoadFileFormat.Concordance, ArgumentHelpers.GetLoadFileFormat("concordance"));
    }

    [Fact]
    public void GetLoadFileFormat_InvalidName_ReturnsNull()
    {
        Assert.Null(ArgumentHelpers.GetLoadFileFormat("invalid"));
    }
}
