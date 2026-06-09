using Xunit;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

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
    /// Test method.
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

        Assert.All(results, x => Assert.InRange(x, 0, 100));
        var average = results.Average();
        Assert.InRange(average, 40, 60); // Should be centered around 50
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_MinEqualsMax_ReturnsExactValue()
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 42, Max = 42 }
        };
        var generator = new NumberGenerator(col);

        var result = generator.Generate(MakeContext());
        Assert.Equal("42", result);
    }
}
