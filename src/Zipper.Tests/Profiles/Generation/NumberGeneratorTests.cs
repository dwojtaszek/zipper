using Xunit;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

public class NumberGeneratorTests
{
    private static ColumnGenerationContext MakeContext(int seed = 42) => new()
    {
        NativeFileIndex = seed,
        FolderNumber = 1,
        DocumentIndex = seed,
        Now = DateTime.UtcNow,
        Seeded = new Random(seed)
    };

    [Fact]
    public void Generate_WithinRange()
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 10, Max = 20 }
        };
        var generator = new NumberGenerator(col);

        for (int i = 0; i < 50; i++)
        {
            var result = int.Parse(generator.Generate(MakeContext(i)));
            Assert.InRange(result, 10, 20);
        }
    }

    [Fact]
    public void Generate_UniformDistribution()
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 1, Max = 10 }
        };
        var generator = new NumberGenerator(col);

        var context = MakeContext();
        var results = Enumerable.Range(0, 1000).Select(_ => int.Parse(generator.Generate(context))).ToList();

        Assert.Contains(results, x => x == 1);
        Assert.Contains(results, x => x == 10);
        Assert.All(results, x => Assert.InRange(x, 1, 10));
    }

    [Fact]
    public void Generate_GaussianDistribution()
    {
        var col = new ColumnDefinition
        {
            Name = "TestNumber",
            Range = new RangeConfig { Min = 0, Max = 100 },
            Distribution = "gaussian"
        };
        var generator = new NumberGenerator(col);

        var context = MakeContext();
        var results = Enumerable.Range(0, 1000).Select(_ => int.Parse(generator.Generate(context))).ToList();

        Assert.All(results, x => Assert.InRange(x, 0, 100));
        var average = results.Average();
        Assert.InRange(average, 40, 60); // Should be centered around 50
    }

    [Fact]
    public void Generate_MinEqualsMax()
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
