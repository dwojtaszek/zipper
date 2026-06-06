using System.Globalization;
using Xunit;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

public class DateGeneratorTests
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
    public void DateGenerator_WithNonUsCulture_ParsesIsoDatesCorrectly()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            // Create a custom culture using ar-SA but explicitly set to UmAlQuraCalendar
            var nonUsCulture = (CultureInfo)CultureInfo.GetCultureInfo("ar-SA").Clone();
            nonUsCulture.DateTimeFormat.Calendar = new UmAlQuraCalendar();
            CultureInfo.CurrentCulture = nonUsCulture;
            CultureInfo.CurrentUICulture = nonUsCulture;

            var col = new ColumnDefinition
            {
                Name = "TestDate",
                Type = "date",
                DateRange = new DateRangeConfig { Min = "2020-01-01", Max = "2020-01-10" }
            };
            var settings = new ProfileSettings { DateFormat = "yyyy-MM-dd" };

            // Act & Assert
            // This should parse the date correctly as Gregorian year 2020
            var generator = new DateGenerator(col, settings);
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

            // Verify it generates a valid date within the range in invariant culture
            var parsed = DateTime.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            Assert.InRange(parsed, new DateTime(2020, 1, 1), new DateTime(2020, 1, 10));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void Generate_WithinSpecifiedRange()
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

    [Fact]
    public void Generate_CustomDateFormats()
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

    [Fact]
    public void Generate_MinEqualsMaxDate()
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
