using System.Globalization;

namespace Zipper.Profiles.Generation;

internal sealed class DateTimeColumnGenerator : IColumnValueGenerator
{
    private readonly DateTime minDate;
    private readonly DateTime maxDate;
    private readonly string format;

    public DateTimeColumnGenerator(ColumnDefinition col, ProfileSettings settings)
    {
        this.minDate = DateTime.Parse(col.DateRange?.Min ?? "2020-01-01", CultureInfo.InvariantCulture);
        this.maxDate = DateTime.Parse(col.DateRange?.Max ?? "2024-12-31", CultureInfo.InvariantCulture);
        this.format = col.Format ?? settings.DateTimeFormat;
    }

    public string Generate(ColumnGenerationContext context)
    {
        var range = (this.maxDate - this.minDate).Days;
        var date = range <= 0
            ? this.minDate.AddHours(context.Seeded.Next(24)).AddMinutes(context.Seeded.Next(60))
            : this.minDate.AddDays(context.Seeded.Next(range + 1)).AddHours(context.Seeded.Next(24)).AddMinutes(context.Seeded.Next(60));
        return date.ToString(this.format, CultureInfo.InvariantCulture);
    }
}
