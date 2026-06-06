using Xunit;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

/// <summary>
/// Test class for public class BooleanGeneratorTests
/// </summary>
public class BooleanGeneratorTests
{
    /// <summary>
    /// Creates context.
    /// </summary>
    private static ColumnGenerationContext MakeContext(int seed = 42) => new()
    {
        NativeFileIndex = seed,
        FolderNumber = 1,
        DocumentIndex = seed,
        Now = DateTime.UtcNow,
        Seeded = new Random(seed)
    };

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_YnFormat_ReturnsYOrN()
    {
        var col = new ColumnDefinition { Name = "Test", Format = "yn", TruePercentage = 50 };
        var generator = new BooleanGenerator(col);

        var results = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            results.Add(generator.Generate(MakeContext(i)));
        }

        Assert.Contains("Y", results);
        Assert.Contains("N", results);
        Assert.Subset(new HashSet<string> { "Y", "N" }, results);
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_TrueFalseFormat_ReturnsTrueOrFalse()
    {
        var col = new ColumnDefinition { Name = "Test", Format = "truefalse", TruePercentage = 50 };
        var generator = new BooleanGenerator(col);

        var results = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            results.Add(generator.Generate(MakeContext(i)));
        }

        Assert.Contains("True", results);
        Assert.Contains("False", results);
        Assert.Subset(new HashSet<string> { "True", "False" }, results);
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_10Format_Returns1Or0()
    {
        var col = new ColumnDefinition { Name = "Test", Format = "10", TruePercentage = 50 };
        var generator = new BooleanGenerator(col);

        var results = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            results.Add(generator.Generate(MakeContext(i)));
        }

        Assert.Contains("1", results);
        Assert.Contains("0", results);
        Assert.Subset(new HashSet<string> { "1", "0" }, results);
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_NullFormat_ReturnsYOrN()
    {
        var col = new ColumnDefinition { Name = "Test", Format = null, TruePercentage = 50 };
        var generator = new BooleanGenerator(col);

        var results = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            results.Add(generator.Generate(MakeContext(i)));
        }

        Assert.Contains("Y", results);
        Assert.Contains("N", results);
        Assert.Subset(new HashSet<string> { "Y", "N" }, results);
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_UnrecognizedFormat_ReturnsYOrN()
    {
        var col = new ColumnDefinition { Name = "Test", Format = "unknown", TruePercentage = 50 };
        var generator = new BooleanGenerator(col);

        var results = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            results.Add(generator.Generate(MakeContext(i)));
        }

        Assert.Contains("Y", results);
        Assert.Contains("N", results);
        Assert.Subset(new HashSet<string> { "Y", "N" }, results);
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_TruePercentage100_AlwaysReturnsY()
    {
        var col = new ColumnDefinition { Name = "Test", Format = "yn", TruePercentage = 100 };
        var generator = new BooleanGenerator(col);
        var context = MakeContext();
        for (int i = 0; i < 50; i++)
        {
            Assert.Equal("Y", generator.Generate(context));
        }
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_TruePercentage0_AlwaysReturnsN()
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
