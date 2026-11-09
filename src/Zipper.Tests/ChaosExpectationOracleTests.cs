using Xunit;

namespace Zipper.Tests;

public class ChaosExpectationOracleTests
{
    public static IEnumerable<object[]> AllAnomalyTypes =>
        ChaosAnomalyTypes.Dat
            .Concat(ChaosAnomalyTypes.Opt)
            .Concat(ChaosAnomalyTypes.Csv)
            .Concat(ChaosAnomalyTypes.Concordance)
            .Concat(ChaosAnomalyTypes.EdrmXml)
            .Concat(ChaosAnomalyTypes.CrossFormat.All)
            .Concat(ChaosAnomalyTypes.Email)
            .Concat(ChaosAnomalyTypes.Tiff)
            .Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(AllAnomalyTypes))]
    public void ExpectedOutcomeClass_EveryCatalogAnomalyType_IsExplicit(string anomalyType)
    {
        var outcomeClass = ChaosExpectationOracle.ExpectedOutcomeClass(anomalyType);

        Assert.Contains(outcomeClass, ChaosOutcomeClasses.All);
    }

    [Fact]
    public void ExpectedOutcomeClass_WithRfc822DepthAlias_ReturnsCanonicalClass()
    {
        Assert.Equal(
            ChaosExpectationOracle.ExpectedOutcomeClass("rf822-depth"),
            ChaosExpectationOracle.ExpectedOutcomeClass("rfc822-depth"));
    }

    [Theory]
    [InlineData("columns", ChaosOutcomeClasses.ParseFails)]
    [InlineData("path-pointer", ChaosOutcomeClasses.ParseSucceedsWithLoss)]
    [InlineData("header-fold", ChaosOutcomeClasses.SilentPass)]
    public void ExpectedOutcomeClass_RepresentativeAnomaly_ReturnsCatalogClass(string anomalyType, string expected)
    {
        Assert.Equal(expected, ChaosExpectationOracle.ExpectedOutcomeClass(anomalyType));
    }

    [Fact]
    public void ExpectedOutcomeClass_UnknownType_Throws()
    {
        Assert.Throws<ArgumentException>(() => ChaosExpectationOracle.ExpectedOutcomeClass("not-a-chaos-type"));
    }
}
