using System.Globalization;
using Xunit;
using Zipper.Profiles;
using Zipper.Profiles.Generation;

namespace Zipper.Tests.Profiles.Generation;

public class DateTimeColumnGeneratorTests
{
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
}
