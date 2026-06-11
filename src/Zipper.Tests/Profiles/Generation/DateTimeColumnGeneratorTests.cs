#pragma warning disable RS0030 // Do not use banned APIs
using System.Globalization;
using Xunit;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

/// <summary>
/// Test class for public class DateTimeColumnGeneratorTests
/// </summary>
public class DateTimeColumnGeneratorTests
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
    public void Generate_WithConfiguredRange_ReturnsValuesWithinBounds()
    {
        var col = new ColumnDefinition
        {
            Name = "TestDateTime",
            DateRange = new DateRangeConfig { Min = "2023-01-01", Max = "2023-01-10" }
        };
        var settings = new ProfileSettings { DateTimeFormat = "yyyy-MM-dd HH:mm" };
        var generator = new DateTimeColumnGenerator(col, settings);

        for (int i = 0; i < 50; i++)
        {
            var result = generator.Generate(MakeContext(i));
            var date = DateTime.ParseExact(result, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            Assert.InRange(date, new DateTime(2023, 1, 1), new DateTime(2023, 1, 10, 23, 59, 59));
        }
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_WithSingleDayRange_ProducesVariedTimeComponents()
    {
        var col = new ColumnDefinition
        {
            Name = "TestDateTime",
            DateRange = new DateRangeConfig { Min = "2023-01-01", Max = "2023-01-01" } // One specific day
        };
        var settings = new ProfileSettings { DateTimeFormat = "yyyy-MM-dd HH:mm" };
        var generator = new DateTimeColumnGenerator(col, settings);

        var generatedTimes = new HashSet<string>(StringComparer.Ordinal);
        var context = MakeContext();
        for (int i = 0; i < 50; i++)
        {
            generatedTimes.Add(generator.Generate(context));
        }

        // With 50 generations, we expect multiple unique time values, proving randomization within the day.
        Assert.True(generatedTimes.Count > 10, $"Expected > 10 unique times, got {generatedTimes.Count}");
    }
}
