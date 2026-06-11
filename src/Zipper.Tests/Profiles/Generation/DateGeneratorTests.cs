#pragma warning disable RS0030 // Do not use banned APIs
using System.Globalization;
using Xunit;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

/// <summary>
/// Test class for public class DateGeneratorTests
/// </summary>
public class DateGeneratorTests
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
    public void Generate_RangeSpecified_ReturnsDatesWithinRange()
    {
        var col = new ColumnDefinition
        {
            Name = "TestDate",
            DateRange = new DateRangeConfig { Min = "2023-01-01", Max = "2023-01-10" }
        };
        var settings = new ProfileSettings { DateFormat = "yyyy-MM-dd" };
        var generator = new DateGenerator(col, settings);

        for (int i = 0; i < 50; i++)
        {
            var result = generator.Generate(MakeContext(i));
            var date = DateTime.ParseExact(result, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Assert.InRange(date, new DateTime(2023, 1, 1), new DateTime(2023, 1, 10));
        }
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_GivenCustomDateFormat_ReturnsFormattedDate()
    {
        var col = new ColumnDefinition
        {
            Name = "TestDate",
            Format = "MM/dd/yyyy",
            DateRange = new DateRangeConfig { Min = "2023-01-15", Max = "2023-01-15" }
        };
        var settings = new ProfileSettings { DateFormat = "yyyy-MM-dd" };
        var generator = new DateGenerator(col, settings);

        var result = generator.Generate(MakeContext());
        Assert.Equal("01/15/2023", result);
    }

    /// <summary>
    /// Test method.
    /// </summary>
    [Fact]
    public void Generate_MinEqualsMaxDate_ShouldReturnExactDate()
    {
        var col = new ColumnDefinition
        {
            Name = "TestDate",
            DateRange = new DateRangeConfig { Min = "2023-01-01", Max = "2023-01-01" }
        };
        var settings = new ProfileSettings { DateFormat = "yyyy-MM-dd" };
        var generator = new DateGenerator(col, settings);

        var result = generator.Generate(MakeContext(42));
        Assert.Equal("2023-01-01", result);
    }
}
