using Xunit;

using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

public class CodedGeneratorTests
{
    private static ColumnDefinition MultiValueColumn(int min = 2, int max = 3) => new()
    {
        Name = "TestCoded",
        Type = "coded",
        MultiValue = true,
        MultiValueCount = new RangeConfig { Min = min, Max = max },
    };

    private static ProfileSettings DefaultSettings() => new() { MultiValueDelimiter = ";" };

    private static ColumnGenerationContext MakeContext(int docIndex, int seed = 42) => new()
    {
        NativeFileIndex = docIndex,
        FolderNumber = 1,
        DocumentIndex = docIndex,
        Seeded = new Random(seed + docIndex),
        Now = DateTime.UtcNow,
    };

    private static int[] WeightedIndices(int count)
    {
        // All point to first value — worst case for the bug
        var indices = new int[count];
        return indices;
    }

    [Fact(Timeout = 2000)]
    public async Task Generate_MultiValue_Weighted_CompletesAndReturnsDistinctValues()
    {
        var values = new string[] { "Alpha", "Beta", "Gamma", "Delta" };
        var indices = WeightedIndices(100);
        var col = MultiValueColumn(min: 2, max: 3);
        var generator = new CodedGenerator(values, indices, col, DefaultSettings());

        var result = await Task.Run(() => generator.Generate(MakeContext(0)));

        var parts = result.Split(';');
        Assert.InRange(parts.Length, 2, 3);
        Assert.Equal(parts.Length, parts.Distinct().Count());
    }

    [Fact(Timeout = 2000)]
    public async Task Generate_MultiValue_Pareto_CompletesAndReturnsDistinctValues()
    {
        var values = new string[] { "Red", "Green", "Blue", "Yellow" };

        // Pareto-like: heavily skewed toward index 0
        var indices = Enumerable.Range(0, 200).Select(i => i < 180 ? 0 : 1).ToArray();
        var col = MultiValueColumn(min: 2, max: 3);
        var generator = new CodedGenerator(values, indices, col, DefaultSettings());

        var result = await Task.Run(() => generator.Generate(MakeContext(5)));

        var parts = result.Split(';');
        Assert.InRange(parts.Length, 2, 3);
        Assert.Equal(parts.Length, parts.Distinct().Count());
    }

    [Fact]
    public void Generate_SingleValue_Weighted_UsesDocumentIndexPath()
    {
        var values = new string[] { "One", "Two", "Three" };
        var indices = new[] { 2, 0, 1 };
        var col = new ColumnDefinition { Name = "Test", Type = "coded", MultiValue = false };
        var generator = new CodedGenerator(values, indices, col, DefaultSettings());

        // index 0 -> distributionIndices[0]=2 -> values[2]="Three"
        var result = generator.Generate(MakeContext(0));
        Assert.Equal("Three", result);

        // index 1 -> distributionIndices[1]=0 -> values[0]="One"
        result = generator.Generate(MakeContext(1));
        Assert.Equal("One", result);
    }
}
