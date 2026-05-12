using Xunit;

namespace Zipper.Tests;

public class ChaosAnomalyTypesTests
{
    [Fact]
    public void Dat_ContainsExpectedTypes()
    {
        Assert.Contains("mixed-delimiters", ChaosAnomalyTypes.Dat);
        Assert.Contains("quotes", ChaosAnomalyTypes.Dat);
        Assert.Contains("columns", ChaosAnomalyTypes.Dat);
        Assert.Contains("eol", ChaosAnomalyTypes.Dat);
        Assert.Contains("encoding", ChaosAnomalyTypes.Dat);
        Assert.Equal(5, ChaosAnomalyTypes.Dat.Count);
    }

    [Fact]
    public void Opt_ContainsExpectedTypes()
    {
        Assert.Contains("opt-boundary", ChaosAnomalyTypes.Opt);
        Assert.Contains("opt-columns", ChaosAnomalyTypes.Opt);
        Assert.Contains("opt-pagecount", ChaosAnomalyTypes.Opt);
        Assert.Contains("opt-path", ChaosAnomalyTypes.Opt);
        Assert.Contains("opt-batesid", ChaosAnomalyTypes.Opt);
        Assert.Equal(5, ChaosAnomalyTypes.Opt.Count);
    }

    [Fact]
    public void ForFormat_Dat_ReturnsDatTypes()
    {
        var types = ChaosAnomalyTypes.ForFormat(LoadFileFormat.Dat);
        Assert.Equal(ChaosAnomalyTypes.Dat, types);
    }

    [Fact]
    public void ForFormat_Opt_ReturnsOptTypes()
    {
        var types = ChaosAnomalyTypes.ForFormat(LoadFileFormat.Opt);
        Assert.Equal(ChaosAnomalyTypes.Opt, types);
    }
}
