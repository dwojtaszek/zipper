namespace Zipper.Profiles.Generation;

internal sealed class DateGenerator : IColumnValueGenerator
{
    private readonly DateTime minDate;
    private readonly DateTime maxDate;
    private readonly string format;

    public DateGenerator(ColumnDefinition col, ProfileSettings settings)
    {
        this.minDate = DateTime.Parse(col.DateRange?.Min ?? "2020-01-01");
        this.maxDate = DateTime.Parse(col.DateRange?.Max ?? "2024-12-31");
        this.format = col.Format ?? settings.DateFormat;
    }

    public string Generate(ColumnGenerationContext context)
    {
        var range = (this.maxDate - this.minDate).Days;
        var date = range <= 0 ? this.minDate : this.minDate.AddDays(context.Seeded.Next(range + 1));
        return date.ToString(this.format);
    }
}
