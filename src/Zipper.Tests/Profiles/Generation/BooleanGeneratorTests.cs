using Xunit;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

public class BooleanGeneratorTests
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
    public void Generate_YnFormat()
    {
        var col = new ColumnDefinition { Name = "Test", Format = "yn", TruePercentage = 50 };
        var generator = new BooleanGenerator(col);
        var result = generator.Generate(MakeContext());
        Assert.Contains(result, new[] { "Y", "N" });
    }

    [Fact]
    public void Generate_TrueFalseFormat()
    {
        var col = new ColumnDefinition { Name = "Test", Format = "truefalse", TruePercentage = 50 };
        var generator = new BooleanGenerator(col);
        var result = generator.Generate(MakeContext());
        Assert.Contains(result, new[] { "True", "False" });
    }

    [Fact]
    public void Generate_10Format()
    {
        var col = new ColumnDefinition { Name = "Test", Format = "10", TruePercentage = 50 };
        var generator = new BooleanGenerator(col);
        var result = generator.Generate(MakeContext());
        Assert.Contains(result, new[] { "1", "0" });
    }

    [Fact]
    public void Generate_TruePercentage_100()
    {
        var col = new ColumnDefinition { Name = "Test", Format = "yn", TruePercentage = 100 };
        var generator = new BooleanGenerator(col);
        var context = MakeContext();
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal("Y", generator.Generate(context));
        }
    }

    [Fact]
    public void Generate_TruePercentage_0()
    {
        var col = new ColumnDefinition { Name = "Test", Format = "yn", TruePercentage = 0 };
        var generator = new BooleanGenerator(col);
        var context = MakeContext();
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal("N", generator.Generate(context));
        }
    }
}
