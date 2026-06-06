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
    public void DateTimeColumnGenerator_WithNonUsCulture_ParsesIsoDatesCorrectly()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            var nonUsCulture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
            nonUsCulture.DateTimeFormat.Calendar = new UmAlQuraCalendar();
            CultureInfo.CurrentCulture = nonUsCulture;
            CultureInfo.CurrentUICulture = nonUsCulture;

            var col = new ColumnDefinition
            {
                Name = "TestDateTime",
                Type = "datetime",
                DateRange = new DateRangeConfig { Min = "2020-01-01", Max = "2020-01-10" }
            };
            var settings = new ProfileSettings { DateTimeFormat = "yyyy-MM-dd HH:mm" };

            // Act
            var generator = new DateTimeColumnGenerator(col, settings);
            var context = new ColumnGenerationContext
            {
                NativeFileIndex = 0,
                FolderNumber = 1,
                DocumentIndex = 0,
                Seeded = new Random(42),
                Now = DateTime.UtcNow
            };

            var value = generator.Generate(context);
            Assert.NotNull(value);
            var parsed = DateTime.ParseExact(value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            Assert.InRange(parsed, new DateTime(2020, 1, 1), new DateTime(2020, 1, 10, 23, 59, 59));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

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

        var generatedTimes = new HashSet<string>();
        var context = MakeContext();
        for (int i = 0; i < 50; i++)
        {
            generatedTimes.Add(generator.Generate(context));
        }

        // With 50 generations, we expect multiple unique time values, proving randomization within the day.
        Assert.True(generatedTimes.Count > 10, $"Expected > 10 unique times, got {generatedTimes.Count}");
    }
}
