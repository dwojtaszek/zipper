using Xunit;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests;

/// <summary>
/// Test class for public class NumberGeneratorTests
/// </summary>
public class NumberGeneratorTests
{
    /// <summary>
    /// Creates context.
    /// </summary>
    private static ColumnGenerationContext MakeContext(int seed = 42) => new()
    {
        NativeFileIndex = seed,
        FolderNumber = 1,
        DocumentIndex = seed,
        Now = new DateTime(2024, 1, 1, 12, 0, 0, DateTimeKind.Utc),
        Seeded = new Random(seed)
    };

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_WithinRange_ReturnsValuesInBounds()
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 10, Max = 20 }
        };
        var generator = new NumberGenerator(col);

        for (int i = 0; i < 50; i++)
        {
            var result = int.Parse(generator.Generate(MakeContext(i)), System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(result, 10, 20);
        }
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_UniformDistribution_IncludesMinAndMax()
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 1, Max = 10 }
        };
        var generator = new NumberGenerator(col);

        var context = MakeContext();
        var results = Enumerable.Range(0, 1000).Select(_ => int.Parse(generator.Generate(context), System.Globalization.CultureInfo.InvariantCulture)).ToList();

        Assert.Contains(results, x => x == 1);
        Assert.Contains(results, x => x == 10);
        Assert.All(results, x => Assert.InRange(x, 1, 10));
    }

    /// <summary>
    /// Test method verifying Gaussian distribution shows central concentration, nonzero spread, and bounds.
    /// </summary>
    [Fact]
    public void Generate_GaussianDistribution_CenteredAverage()
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 0, Max = 100 },
            Distribution = "gaussian"
        };
        var generator = new NumberGenerator(col);

        var context = MakeContext();
        var results = Enumerable.Range(0, 1000).Select(_ => int.Parse(generator.Generate(context), System.Globalization.CultureInfo.InvariantCulture)).ToList();

        // 1. Legal bounds
        Assert.All(results, x => Assert.InRange(x, 0, 100));

        // 2. Centered average around 50
        var average = results.Average();
        Assert.InRange(average, 45.0, 55.0);

        // 3. Central concentration: middle 40% (30..70) contains majority (>60%), outer 40% (<20 or >80) is sparse (<20%)
        int middleCount = results.Count(x => x >= 30 && x <= 70);
        int outerCount = results.Count(x => x < 20 || x > 80);
        Assert.True(
            middleCount > 600,
            $"Middle 40% should have > 600 of 1000 items in Gaussian distribution, got {middleCount}");
        Assert.True(
            outerCount < 200,
            $"Outer 40% (edges) should have < 200 of 1000 items in Gaussian distribution, got {outerCount}");
        Assert.True(
            middleCount > outerCount * 3,
            $"Middle count ({middleCount}) should be at least 3x the edge count ({outerCount})");

        // 4. Nonzero spread: standard deviation matches bell curve (~16.7) and edges are populated with varied values
        double variance = results.Select(x => Math.Pow(x - average, 2)).Average();
        double stdDev = Math.Sqrt(variance);
        Assert.InRange(stdDev, 12.0, 21.0);
        Assert.True(outerCount > 20, $"Outer edges must have non-zero spread, got {outerCount}");
        Assert.True(results.Distinct().Count() >= 40, "Gaussian distribution should produce varied values across the range");
    }

    /// <summary>
    /// Test method verifying Exponential distribution exhibits low-end bias and monotonic decline across quartiles.
    /// </summary>
    [Fact]
    public void Generate_ExponentialDistribution_ShowsLowEndBias()
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 0, Max = 100 },
            Distribution = "exponential"
        };
        var generator = new NumberGenerator(col);

        var context = MakeContext();
        var results = Enumerable.Range(0, 1000).Select(_ => int.Parse(generator.Generate(context), System.Globalization.CultureInfo.InvariantCulture)).ToList();

        // 1. Legal bounds
        Assert.All(results, x => Assert.InRange(x, 0, 100));

        // 2. Quartiles across [0, 100]: Q1=0..24, Q2=25..49, Q3=50..74, Q4=75..100
        int q1 = results.Count(x => x < 25);
        int q2 = results.Count(x => x >= 25 && x < 50);
        int q3 = results.Count(x => x >= 50 && x < 75);
        int q4 = results.Count(x => x >= 75);

        // Monotonic decline across quartiles
        Assert.True(q1 > q2, $"Q1 ({q1}) should be strictly greater than Q2 ({q2}) in exponential distribution");
        Assert.True(q2 > q3, $"Q2 ({q2}) should be strictly greater than Q3 ({q3}) in exponential distribution");
        Assert.True(q3 > q4, $"Q3 ({q3}) should be strictly greater than Q4 ({q4}) in exponential distribution");

        // Low-end bias: Q1 should hold majority (~53%), Q4 should hold ~10%, mean should be well below midpoint 50
        Assert.InRange(q1, 400, 650);
        Assert.InRange(q4, 50, 160);
        var average = results.Average();
        Assert.InRange(average, 20.0, 42.0);
        Assert.True(results.Distinct().Count() >= 40, "Exponential distribution should produce varied values across the range");
    }

    /// <summary>
    /// Test method verifying that normal alias produces the identical Gaussian sequence for the same seed.
    /// </summary>
    [Fact]
    public void Generate_NormalAlias_ProducesIdenticalSequenceToGaussian()
    {
        var colGaussian = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 0, Max = 100 },
            Distribution = "gaussian"
        };
        var colNormal = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 0, Max = 100 },
            Distribution = "normal"
        };
        var genGaussian = new NumberGenerator(colGaussian);
        var genNormal = new NumberGenerator(colNormal);

        var contextGaussian = MakeContext(seed: 42);
        var contextNormal = MakeContext(seed: 42);

        var gaussianResults = Enumerable.Range(0, 100).Select(_ => genGaussian.Generate(contextGaussian)).ToList();
        var normalResults = Enumerable.Range(0, 100).Select(_ => genNormal.Generate(contextNormal)).ToList();

        Assert.Equal(gaussianResults, normalResults);
    }

    /// <summary>
    /// Test method verifying Min==Max boundary handling returns exact value across distributions without division-by-zero.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("uniform")]
    [InlineData("gaussian")]
    [InlineData("normal")]
    [InlineData("exponential")]
    public void Generate_MinEqualsMax_AcrossDistributions_ReturnsExactValue(string? distribution)
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 42, Max = 42 },
            Distribution = distribution
        };
        var generator = new NumberGenerator(col);

        var result = generator.Generate(MakeContext());
        Assert.Equal("42", result);
    }

    /// <summary>
    /// Test method for int.MaxValue range branch (lines 78-80 in NumberGenerator.cs).
    /// When max == int.MaxValue, the generator uses long arithmetic to avoid overflow.
    /// </summary>
    [Fact]
    public void Generate_IntMaxValueRange_UsesLongArithmeticAndStaysInBounds()
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 0, Max = int.MaxValue },
            Distribution = "uniform"
        };
        var generator = new NumberGenerator(col);

        var context = MakeContext();
        var results = Enumerable.Range(0, 100).Select(_ => long.Parse(generator.Generate(context), System.Globalization.CultureInfo.InvariantCulture)).ToList();

        Assert.All(results, x => Assert.InRange(x, 0L, (long)int.MaxValue));
        // Should produce varied values (not all the same)
        Assert.True(results.Distinct().Count() > 1, "Should produce varied values across the full int range");
    }

    /// <summary>
    /// Test method for int.MaxValue range with non-zero min (lines 78-80).
    /// </summary>
    [Fact]
    public void Generate_IntMaxValueRange_WithNonZeroMin_StaysInBounds()
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 1000, Max = int.MaxValue },
            Distribution = "uniform"
        };
        var generator = new NumberGenerator(col);

        var context = MakeContext();
        var results = Enumerable.Range(0, 100).Select(_ => long.Parse(generator.Generate(context), System.Globalization.CultureInfo.InvariantCulture)).ToList();

        Assert.All(results, x => Assert.InRange(x, 1000L, (long)int.MaxValue));
    }
}
